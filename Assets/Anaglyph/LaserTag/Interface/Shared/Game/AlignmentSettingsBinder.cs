using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Binds alignment selection and method-specific settings without owning navigation or headset tools.</summary>
	public sealed class AlignmentSettingsBinder : IDisposable
	{
		private readonly DropdownField methodField;
		private readonly Label statusLabel;
		private readonly Label preferenceStatus;
		private readonly UIEventBindings bindings = new();
		private string contextStatus;
		private readonly Button choosePair;
		private readonly Label pairStatus;

		private readonly Slider tagSizeSlider;
		private readonly Label tagSizeNote;
		private readonly Label tagStatus;
		private readonly Button unregisterAllTagsButton;
		private const float tagSizeSettleSeconds = 0.1f;
		private float? pendingTagSizeCm;
		private Guid pendingSizeContext;
		private float pendingTagSizeTime;
		private readonly bool operatorMode;
		private readonly VisualElement tagConfigurationSection;

		public bool InSession => SyncBus.Active && ColocationManager.Instance != null;
		private bool SettingUpTags => ColocationManager.Instance != null && ColocationManager.Instance.IsSettingUpTags;
		public ColocationManager.ColocationMethod DisplayedMethod => LaserTagMapCoordinator.Instance?.IsChangingColocation == true
			? LaserTagMapCoordinator.Instance.AlignmentTransition.Target : LaserTagMapCoordinator.Instance?.WaitingForAlignmentAuthor == true
			? LaserTagMapCoordinator.Instance.CurrentSpace.pendingSetupMethod : InSession ? ColocationManager.Instance.Method : Preference;
		public bool UsesTags => DisplayedMethod is ColocationManager.ColocationMethod.AprilTag or
			ColocationManager.ColocationMethod.TwoAprilTags;
		public string Status { get; private set; }

		private static ColocationManager.ColocationMethod Preference =>
			LaserTagMapCoordinator.Instance?.CurrentSpace?.preferredColocationMethod ??
			ColocationManager.ColocationMethod.MetaSharedAnchor;

		public event Action Changing;
		public event Action Changed;

		public AlignmentSettingsBinder(VisualElement root, bool operatorMode = false)
		{
			this.operatorMode = operatorMode;
			tagConfigurationSection = Require<VisualElement>(root, "tag-configuration-section");
			tagSizeSlider = Require<Slider>(root, "tag-size-slider");
			tagSizeNote = Require<Label>(root, "tag-size-note");
			tagStatus = Require<Label>(root, "tag-status");
			unregisterAllTagsButton = Require<Button>(root, "unregister-all-tags-button");
			TextField input = tagSizeSlider.Q<TextField>();
			if (input != null)
			{
				input.isDelayed = true;
				input.keyboardType = TouchScreenKeyboardType.DecimalPad;
			}
			if (operatorMode)
			{
				bindings.Value(tagSizeSlider, OnTagSizeChanged);
				bindings.Click(unregisterAllTagsButton, OnUnregisterAllTagsClicked);
			}
			methodField = Require<DropdownField>(root, "colocation-method-field");
			statusLabel = Require<Label>(root, "colocation-status");
			preferenceStatus = Require<Label>(root, "colocation-preference-status");
			choosePair = Require<Button>(root, "choose-two-tag-pair"); pairStatus = Require<Label>(root, "two-tag-pair-status");
			bindings.Click(choosePair, () => LaserTagMapCoordinator.Instance?.ChooseAnotherTagPair());
			RefreshChoices();
			bindings.Value(methodField, OnMethodChanged);
			MenuCopy.Changed += OnCopyChanged;
		}

		public void Dispose()
		{
			MenuCopy.Changed -= OnCopyChanged;
			bindings.Dispose();
			FlushPendingSize();
		}

		private void RefreshChoices()
		{
			methodField.choices = new List<string>
			{
				MenuCopy.Get("Game", "alignment.anchors"), MenuCopy.Get("Game", "alignment.tags"),
				MenuCopy.Get("Game", "alignment.system"), MenuCopy.Get("Game", "alignment.two-tags")
			};
		}

		private void OnCopyChanged()
		{
			RefreshChoices();
			Refresh(contextStatus);
		}

		private void OnMethodChanged(ChangeEvent<string> change)
		{
			int index = methodField.choices.IndexOf(change.newValue);
			if (index < 0) return;
			FlushPendingSize();
			Changing?.Invoke();
			LaserTagMapCoordinator.Instance?.SetPreferredColocationMethod((ColocationManager.ColocationMethod)index);
			Refresh(contextStatus);
			Changed?.Invoke();
		}

		public void Refresh(string contextStatus = null)
		{
			if (pendingTagSizeCm.HasValue && Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingSize();
			this.contextStatus = contextStatus;
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			var preference = Preference;
			var displayed = DisplayedMethod;
			bool inSession = InSession;
			methodField.label = MenuCopy.Get("Game", inSession && !SettingUpTags ? "alignment.session-label" : "alignment.preference-label");
			methodField.SetValueWithoutNotify(methodField.choices[(int)displayed]);
			SetMessage(preferenceStatus, inSession && displayed != preference
				? MenuCopy.Format("Game", "alignment.saved-preference", methodField.choices[(int)preference]) : null);

			string blocker = manager != null ? manager.DescribeColocationPreferenceBlocker() : MenuCopy.Get("Game", "maps.unavailable");
			methodField.SetEnabled(blocker == null);
			methodField.tooltip = blocker ?? MenuCopy.Get("Game", inSession ? "alignment.session-scope" : "alignment.scope");
			Status = blocker ?? contextStatus;
			if (manager != null && manager.IsChangingColocation)
			{
				var transition = manager.AlignmentTransition;
				var phase = MenuCopy.Get("Game", "alignment.phase." + transition.Phase);
				Status = ColocationManager.IsColocated
					? MenuCopy.Format("Game", "alignment.transition", phase,
						methodField.choices[(int)ColocationManager.Instance.ActiveMethod])
					: MenuCopy.Format("Game", "alignment.transition-unresolved", phase);
				Status = MenuCopy.Format("Game", "alignment.method-status", methodField.choices[(int)transition.Target], Status);
			}
			else if (manager != null && manager.WaitingForAlignmentAuthor)
			{
				var pendingSpace = manager.CurrentSpace;
				bool needsSource = pendingSpace.HasReferenceBasedData &&
					(displayed == ColocationManager.ColocationMethod.AprilTag ? !pendingSpace.HasTags : pendingSpace.anchors.Count == 0);
				Status = MenuCopy.Get("Game", needsSource ? "alignment.waiting-for-reference-headset" : "alignment.waiting-for-headset");
			}
			else if (inSession && displayed == ColocationManager.ColocationMethod.MetaSharedAnchor &&
				ColocationManager.Instance.AnchorProvider != null)
			{
				var anchors = ColocationManager.Instance.AnchorProvider;
				Status ??= anchors.HasMinter
					? MenuCopy.Format("Game", "alignment.anchor-minter", anchors.Minter.clientId)
					: MenuCopy.Get("Game", "alignment.waiting-for-anchor-headset");
			}
			else if (SettingUpTags)
				Status ??= MenuCopy.Get("Game", "alignment.system-tag-setup");
			else if (displayed == ColocationManager.ColocationMethod.SystemDetermined)
				Status ??= MenuCopy.Get("Game", "alignment.system-description");
			if (!ColocationManager.UsesSavedReferences(displayed)) Status = MenuCopy.Get("Game", "alignment.provisional");
			choosePair.text = MenuCopy.Get("Game", "alignment.choose-pair");
			choosePair.EnableInClassList("map-field-hidden", displayed != ColocationManager.ColocationMethod.TwoAprilTags);
			choosePair.SetEnabled(blocker == null);
			var space = manager?.CurrentSpace;
			SetMessage(pairStatus, displayed != ColocationManager.ColocationMethod.TwoAprilTags ? null : space?.firstTagId >= 0
				? MenuCopy.Format("Game", "alignment.pair", space.firstTagId, space.secondTagId) : MenuCopy.Get("Game", "alignment.pair-waiting"));
			SetMessage(statusLabel, Status);
			RefreshTagControls();
		}

		// A slider drag never takes focus and reports no end event. Commit only the size
		// it settles on so the session does not negotiate every intermediate value.
		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingSizeContext = LaserTagMapCoordinator.Instance?.ReferenceContext ?? Guid.Empty;
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
		}

		public void FlushPendingSize()
		{
			if (!pendingTagSizeCm.HasValue) return;
			float centimeters = pendingTagSizeCm.Value;
			pendingTagSizeCm = null;
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager != null && pendingSizeContext == manager.ReferenceContext && !manager.SetTagSize(centimeters))
				RefreshTagControls();
		}

		private void OnUnregisterAllTagsClicked()
		{
			LaserTagMapCoordinator.Instance?.UnregisterAllTags();
			RefreshTagControls();
		}

		private void RefreshTagControls()
		{
			tagConfigurationSection.EnableInClassList("map-field-hidden", !operatorMode || !UsesTags);
			bool registeredTags = operatorMode && DisplayedMethod == ColocationManager.ColocationMethod.AprilTag;
			unregisterAllTagsButton.EnableInClassList("map-field-hidden", !registeredTags);
			LaserTagMapCoordinator manager = LaserTagMapCoordinator.Instance;
			if (manager == null)
			{
				SetMessage(tagStatus, null);
				tagSizeSlider.SetEnabled(false);
				unregisterAllTagsButton.SetEnabled(false);
				SetMessage(tagSizeNote, null);
				return;
			}

			string sizeBlocker = manager.DescribeTagSizeBlocker();
			tagSizeSlider.SetEnabled(operatorMode && UsesTags && sizeBlocker == null);
			SetMessage(tagSizeNote, sizeBlocker == Status ? null : sizeBlocker);
			if (!pendingTagSizeCm.HasValue && !IsBeingEdited(tagSizeSlider))
			{
				tagSizeSlider.highValue = Mathf.Max(50f, manager.EffectiveTagSizeCm);
				tagSizeSlider.SetValueWithoutNotify(manager.EffectiveTagSizeCm);
			}

			int registered = manager.CurrentSpace != null ? manager.CurrentSpace.tags.Count : 0;
			unregisterAllTagsButton.SetEnabled(registeredTags && registered > 0 && manager.DescribeTagRemovalBlocker() == null);
			SetMessage(tagStatus, registeredTags ? MenuCopy.Format("Game", "alignment.tag-count", registered) : null);
		}

		private static bool IsBeingEdited(VisualElement field)
		{
			Focusable focused = field.panel?.focusController?.focusedElement;
			return focused is VisualElement element && (element == field || field.Contains(element));
		}

		private static void SetMessage(Label label, string message)
		{
			label.text = message ?? "";
			label.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
		}
	}
}
