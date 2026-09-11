using Anaglyph.Netcode.SyncVariables;
using Anaglyph.LaserTag.EnvSyncing;
using System;
using Anaglyph.Debugging;
using Anaglyph.InGameConsole;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
using Anaglyph.XR.DepthKit.EnvScanning;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class SettingsMenu : MonoBehaviour
	{
		[SerializeField] private BoolObject healthPassthroughTintSetting;
		[SerializeField] private BoolObject lightEffectsSetting;

		private readonly UIEventBindings bindings = new();

		private Button showDebugMeshForEveryone;
		private Button hideDebugMeshForEveryone;
		private NavView navView;
		private NavPage consolePage;
		private InGameConsoleView consoleView;
		private Toggle debugModeToggle;
		private Toggle showDebugMeshToggle;
		private Toggle healthPassthroughTintToggle;
		private Toggle lightEffectsToggle;
		private Label provisioningStatus;
		private Button unprovisionButton;
		private NavButton operatorProvisioningButton;
		private bool showDebugMesh;
		private UIToolkitPanelXRSetup panel;

		private void Awake()
		{
			panel = GetComponent<UIToolkitPanelXRSetup>();
		}

		private void OnAuthorityChanged(bool hasAuthority) => UpdateDebugMeshForEveryoneEnabled();

		private void UpdateDebugMeshForEveryoneEnabled()
		{
			bool canSetForEveryone = SyncBus.Active && SyncBus.IsAuthority;
			showDebugMeshForEveryone.SetEnabled(canSetForEveryone);
			hideDebugMeshForEveryone.SetEnabled(canSetForEveryone);
		}

		private void InitializeUI()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"SettingsMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = NavView.RequireIn(root);
			consolePage = navView.GetPage("console-page");

			// the console only processes log messages while its page is on screen
			consoleView = new InGameConsoleView(consolePage);
			navView.Changed += OnNavPageChange;

			debugModeToggle = Require<Toggle>(root, "debug-mode-toggle");
			showDebugMeshToggle = Require<Toggle>(root, "show-debug-mesh-toggle");

			bindings.Value(debugModeToggle,
				change => AnaglyphDebugging.SetDebugMode(change.newValue));
			bindings.Value(showDebugMeshToggle, change =>
			{
				showDebugMesh = change.newValue;
				if (EnvMesher.Instance != null)
					EnvMesher.Instance.SetChunksVisible(showDebugMesh);
			});
			showDebugMeshToggle.SetValueWithoutNotify(showDebugMesh);


			showDebugMeshForEveryone =
				Require<Button>(root, "show-debug-mesh-for-everyone");
			hideDebugMeshForEveryone =
				Require<Button>(root, "hide-debug-mesh-for-everyone");

			bindings.Click(showDebugMeshForEveryone,
				() => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(true));
			bindings.Click(hideDebugMeshForEveryone,
				() => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(false));

			healthPassthroughTintToggle =
				Require<Toggle>(root, "health-passthrough-tint-toggle");
			lightEffectsToggle = Require<Toggle>(root, "light-effects-toggle");
			provisioningStatus = Require<Label>(root, "provisioning-status");
			unprovisionButton = Require<Button>(root, "unprovision-headset-button");
			operatorProvisioningButton = Require<NavButton>(root, "operator-provisioning-button");
			bindings.Click(unprovisionButton, UnprovisionHeadset);

			bindings.Value(healthPassthroughTintToggle,
				change => healthPassthroughTintSetting.Value = change.newValue);
			bindings.Value(lightEffectsToggle,
				change => lightEffectsSetting.Value = change.newValue);

			Label version = Require<Label>(root, "version");
			MenuCopy.SetVariable(version, "version", NetcodeManagement.GameVersion);
		}

		private void OnEnable()
		{
			InitializeUI();
			MenuCopy.Changed += RefreshHeadsetConfiguration;
			UpdateDebugMeshForEveryoneEnabled();
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			SyncBus.Deactivated += UpdateDebugMeshForEveryoneEnabled;
			SyncBus.Activated += UpdateDebugMeshForEveryoneEnabled;

			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			healthPassthroughTintSetting.Changed += OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed += OnLightEffectsChanged;
			panel.VisibleChanged += OnPanelVisibleChanged;
			HeadsetConfiguration.Changed += RefreshHeadsetConfiguration;

			UpdateConsoleVisible();
			OnDebugModeChanged(AnaglyphDebugging.DebugMode);
			OnHealthPassthroughTintChanged(healthPassthroughTintSetting.Value);
			OnLightEffectsChanged(lightEffectsSetting.Value);
			RefreshHeadsetConfiguration();
		}

		private void OnDisable()
		{
			bindings.Dispose();
			MenuCopy.Changed -= RefreshHeadsetConfiguration;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= UpdateDebugMeshForEveryoneEnabled;
			SyncBus.Activated -= UpdateDebugMeshForEveryoneEnabled;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			healthPassthroughTintSetting.Changed -= OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed -= OnLightEffectsChanged;
			if (panel != null) panel.VisibleChanged -= OnPanelVisibleChanged;
			HeadsetConfiguration.Changed -= RefreshHeadsetConfiguration;
			if (navView != null)
			{
				navView.Changed -= OnNavPageChange;
				navView = null;
			}

			consoleView?.Dispose();
			consoleView = null;
			consolePage = null;
		}

		private void OnPanelVisibleChanged(bool visible)
		{
			UpdateConsoleVisible();
		}

		private void OnNavPageChange(NavPage page)
		{
			UpdateConsoleVisible();
		}

		// redrawing the log is expensive, so the console only listens while it is on screen
		private void UpdateConsoleVisible()
		{
			consoleView?.SetVisible(panel.IsVisible && navView?.CurrentPage == consolePage);
		}

		private void OnDebugModeChanged(bool enabled)
		{
			debugModeToggle.SetValueWithoutNotify(enabled);
		}

		private void OnLightEffectsChanged(bool enabled)
		{
			lightEffectsToggle.SetValueWithoutNotify(enabled);
		}

		private void OnHealthPassthroughTintChanged(bool enabled)
		{
			healthPassthroughTintToggle.SetValueWithoutNotify(enabled);
		}

		private void UnprovisionHeadset()
		{
			HeadsetConfiguration.Instance?.Unprovision();
		}

		private void RefreshHeadsetConfiguration()
		{
			bool pinned = HeadsetConfiguration.PinnedHostEnabled;
			bool locked = HeadsetConfiguration.MenuPasswordRequired;
			bool provisioned = HeadsetConfiguration.IsProvisioned;
			provisioningStatus.text = pinned
				? MenuCopy.Format("Settings", locked ? "provisioning.host-locked" : "provisioning.host", HeadsetConfiguration.PinnedHostAddress)
				: MenuCopy.Get("Settings", locked ? "provisioning.locked" : "provisioning.none");
			unprovisionButton.style.display = provisioned ? DisplayStyle.Flex : DisplayStyle.None;
			operatorProvisioningButton.style.display = provisioned ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
