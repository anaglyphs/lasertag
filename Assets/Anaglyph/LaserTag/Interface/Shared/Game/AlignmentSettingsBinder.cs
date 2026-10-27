using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
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

		private readonly Slider tagSizeSlider;
		private readonly Label tagSizeNote;
		private readonly Label tagStatus;
		private readonly Button unregisterAllTagsButton;
		private readonly Button setupTagsButton;
		private const float tagSizeSettleSeconds = 0.1f;
		private float? pendingTagSizeCm;
		private Guid pendingSizeFrame;
		private float pendingTagSizeTime;
		private readonly bool operatorMode;
		private readonly VisualElement tagConfigurationSection;

		public bool InSession => SyncBus.Active && ColocationManager.Instance != null;
		/// <summary>The shown space's method: the host's in a session, since the space itself is the host's.</summary>
		public ColocationManager.ColocationMethod DisplayedMethod => Preference;
		public bool UsesTags => DisplayedMethod == ColocationManager.ColocationMethod.AprilTag;

		// the methods offered, in dropdown order
		private static readonly ColocationManager.ColocationMethod[] ListedMethods =
		{
			ColocationManager.ColocationMethod.MetaSharedAnchor, ColocationManager.ColocationMethod.AprilTag,
			ColocationManager.ColocationMethod.Visual
		};
		public string Status { get; private set; }

		private static SpaceAlignment Alignment => SpaceManager.Instance?.Alignment;
		private static ColocationManager.ColocationMethod Preference => Alignment?.SelectedMethod ?? AlignmentSettings.Default.method;

		public event Action Changing;
		public event Action Changed;

		public AlignmentSettingsBinder(VisualElement root, bool operatorMode = false)
		{
			this.operatorMode = operatorMode;
			tagConfigurationSection = Require<VisualElement>(root, "tag-configuration-section");
			setupTagsButton = Require<Button>(root, "start-apriltag-setup");
			setupTagsButton.MakeActOnPress();
			bindings.Click(setupTagsButton, () => { FlushPendingSize(); SpaceManager.Instance?.TagSetup.Begin(); Refresh(); });
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
			choosePair = Require<Button>(root, "choose-two-tag-pair");
			bindings.Click(choosePair, () => Alignment?.ChooseAnotherTagPair());
			RefreshChoices();
			bindings.Value(methodField, OnMethodChanged);
			MenuCopy.Changed += OnCopyChanged;
			Refresh();
		}

		public void Dispose()
		{
			MenuCopy.Changed -= OnCopyChanged;
			bindings.Dispose();
			FlushPendingSize();
		}

		private void RefreshChoices()
		{
			var choices = new List<string>();
			foreach (var method in ListedMethods) choices.Add(MethodName(method));
			methodField.choices = choices;
		}

		// visual relocalization has no localized copy yet
		private static string MethodName(ColocationManager.ColocationMethod method) => method switch
		{
			ColocationManager.ColocationMethod.MetaSharedAnchor => MenuCopy.Get("Map", "alignment.anchors"),
			ColocationManager.ColocationMethod.AprilTag => MenuCopy.Get("Map", "alignment.tags"),
			ColocationManager.ColocationMethod.Visual => "Visual relocalization",
			_ => method.ToString()
		};

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
			Alignment?.SetMethod(ListedMethods[index]);
			Refresh(contextStatus);
			Changed?.Invoke();
		}

		public void Refresh(string contextStatus = null)
		{
			if (pendingTagSizeCm.HasValue && Time.unscaledTime - pendingTagSizeTime >= tagSizeSettleSeconds)
				FlushPendingSize();
			this.contextStatus = contextStatus;
			SpaceAlignment alignment = Alignment;
			var preference = Preference;
			var displayed = DisplayedMethod;
			bool inSession = InSession;
			methodField.label = MenuCopy.Get("Map", inSession ? "alignment.session-label" : "alignment.preference-label");
			// a hidden method still shows its own name when it is the one in use
			methodField.SetValueWithoutNotify(MethodName(displayed));
			SetMessage(preferenceStatus, null);

			string blocker = alignment != null ? alignment.MethodBlocker(displayed) : MenuCopy.Get("Map", "maps.unavailable");
			methodField.SetEnabled(blocker == null);
			methodField.tooltip = blocker ?? MenuCopy.Get("Map", inSession ? "alignment.session-scope" : "alignment.scope");
			Status = blocker ?? contextStatus;
			if (inSession && displayed == ColocationManager.ColocationMethod.MetaSharedAnchor &&
				ColocationManager.Instance.AnchorProvider != null)
			{
				var anchors = ColocationManager.Instance.AnchorProvider;
				Status ??= anchors.HasMinter
					? MenuCopy.Format("Map", "alignment.anchor-minter", anchors.Minter.clientId)
					: MenuCopy.Get("Map", "alignment.waiting-for-anchor-headset");
			}
			if (!ColocationManager.UsesSavedReferences(displayed)) Status = MenuCopy.Get("Map", "alignment.provisional");
			bool twoTags = ColocationManager.IsTwoTagMode(displayed, SpaceManager.Instance?.Space);
			if (twoTags) Status ??= MenuCopy.Get("Map", "alignment.two-tags-description");
			choosePair.text = MenuCopy.Get("Map", "alignment.choose-pair");
			choosePair.style.display = twoTags ? DisplayStyle.Flex : DisplayStyle.None;
			choosePair.SetEnabled(blocker == null);
			SetMessage(statusLabel, Status);
			RefreshTagControls();
		}

		// A slider drag never takes focus and reports no end event. Commit only the size
		// it settles on so the session does not negotiate every intermediate value.
		private void OnTagSizeChanged(ChangeEvent<float> change)
		{
			pendingSizeFrame = SpaceManager.Instance?.Frame.frameId ?? Guid.Empty;
			pendingTagSizeCm = change.newValue;
			pendingTagSizeTime = Time.unscaledTime;
		}

		public void FlushPendingSize()
		{
			if (!pendingTagSizeCm.HasValue) return;
			float centimeters = pendingTagSizeCm.Value;
			pendingTagSizeCm = null;
			SpaceManager manager = SpaceManager.Instance;
			if (manager != null && pendingSizeFrame == manager.Frame.frameId && !manager.Alignment.SetTagSize(centimeters))
				RefreshTagControls();
		}

		private void OnUnregisterAllTagsClicked()
		{
			Alignment?.UnregisterAllTags();
			RefreshTagControls();
		}

		private void RefreshTagControls()
		{
			bool aprilTags = DisplayedMethod == ColocationManager.ColocationMethod.AprilTag;
			tagConfigurationSection.style.display = aprilTags || operatorMode && UsesTags ? DisplayStyle.Flex : DisplayStyle.None;
			setupTagsButton.style.display = aprilTags ? DisplayStyle.Flex : DisplayStyle.None;
			TagSetup setup = SpaceManager.Instance?.TagSetup;
			setupTagsButton.SetEnabled(setup != null && setup.StartBlocker == null);
			setupTagsButton.tooltip = setup?.StartBlocker;
			tagSizeSlider.style.display = operatorMode ? DisplayStyle.Flex : DisplayStyle.None;
			bool registeredTags = operatorMode && DisplayedMethod == ColocationManager.ColocationMethod.AprilTag;
			unregisterAllTagsButton.style.display = !registeredTags ? DisplayStyle.None : DisplayStyle.Flex;
			SpaceManager manager = SpaceManager.Instance;
			if (manager == null)
			{
				SetMessage(tagStatus, null);
				tagSizeSlider.SetEnabled(false);
				unregisterAllTagsButton.SetEnabled(false);
				SetMessage(tagSizeNote, null);
				return;
			}

			string sizeBlocker = manager.Alignment.TagSizeBlocker();
			tagSizeSlider.SetEnabled(operatorMode && UsesTags && sizeBlocker == null);
			SetMessage(tagSizeNote, !operatorMode || sizeBlocker == Status ? null : sizeBlocker);
			if (!pendingTagSizeCm.HasValue && !IsBeingEdited(tagSizeSlider))
			{
				tagSizeSlider.highValue = Mathf.Max(50f, manager.Alignment.TagSizeCm);
				tagSizeSlider.SetValueWithoutNotify(manager.Alignment.TagSizeCm);
			}

			int registered = manager.Space != null ? manager.Space.tags.Count : 0;
			unregisterAllTagsButton.SetEnabled(registeredTags && registered > 0 && manager.Alignment.TagRemovalBlocker() == null);
			SetMessage(tagStatus, registeredTags ? MenuCopy.Format("Map", "alignment.tag-count", registered) : null);
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
