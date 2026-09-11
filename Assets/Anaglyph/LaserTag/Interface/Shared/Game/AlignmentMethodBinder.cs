using System;
using System.Collections.Generic;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Binds the alignment method and its status without requiring tag or navigation controls.</summary>
	public sealed class AlignmentMethodBinder : IDisposable
	{
		private readonly DropdownField methodField;
		private readonly Label methodStatus;
		private readonly Label preferenceStatus;
		private readonly UIEventBindings bindings = new();
		private string contextStatus;

		public bool InSession => SyncBus.Active && ColocationManager.Instance != null;
		private bool SettingUpTags => ColocationManager.Instance != null && ColocationManager.Instance.IsSettingUpTags;
		public ColocationManager.ColocationMethod DisplayedMethod => InSession && !SettingUpTags
			? ColocationManager.Instance.Method : Preference;
		public bool UsesTags => DisplayedMethod == ColocationManager.ColocationMethod.AprilTag;
		public string Status { get; private set; }

		private static ColocationManager.ColocationMethod Preference =>
			LaserTagMapCoordinator.Instance?.CurrentMap?.preferredColocationMethod ??
			ColocationManager.ColocationMethod.MetaSharedAnchor;

		public event Action Changing;
		public event Action Changed;

		public AlignmentMethodBinder(VisualElement root)
		{
			methodField = Require<DropdownField>(root, "colocation-method-field");
			methodStatus = Require<Label>(root, "colocation-method-status");
			preferenceStatus = Require<Label>(root, "colocation-preference-status");
			RefreshChoices();
			bindings.Value(methodField, OnMethodChanged);
			MenuCopy.Changed += OnCopyChanged;
		}

		public void Dispose()
		{
			MenuCopy.Changed -= OnCopyChanged;
			bindings.Dispose();
		}

		private void RefreshChoices()
		{
			methodField.choices = new List<string>
			{
				MenuCopy.Get("Game", "alignment.anchors"), MenuCopy.Get("Game", "alignment.tags"),
				MenuCopy.Get("Game", "alignment.system")
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
			Changing?.Invoke();
			LaserTagMapCoordinator.Instance?.SetPreferredColocationMethod((ColocationManager.ColocationMethod)index);
			Refresh(contextStatus);
			Changed?.Invoke();
		}

		public void Refresh(string contextStatus = null)
		{
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
				Status = MenuCopy.Get("Game", "alignment.preparing");
			else if (SettingUpTags)
				Status ??= MenuCopy.Get("Game", "alignment.system-tag-setup");
			else if (displayed == ColocationManager.ColocationMethod.SystemDetermined)
				Status ??= MenuCopy.Get("Game", "alignment.system-description");
			SetMessage(methodStatus, Status);
		}

		private static void SetMessage(Label label, string message)
		{
			label.text = message ?? "";
			label.style.display = message == null ? DisplayStyle.None : DisplayStyle.Flex;
		}
	}
}
