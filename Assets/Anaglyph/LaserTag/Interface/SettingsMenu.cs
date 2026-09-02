using System;
using Anaglyph.Debugging;
using Anaglyph.InGameConsole;
using Anaglyph.LaserTag.EnvSyncing;
using Anaglyph.Menu;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.VariableObjects;
using Anaglyph.XR.DepthKit.EnvScanning;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	[DefaultExecutionOrder(100)]
	public class SettingsMenu : MonoBehaviour
	{
		[SerializeField] private BoolObject healthPassthroughTintSetting;
		[SerializeField] private BoolObject lightEffectsSetting;
		[SerializeField] private StringObject buildNumber;

		private NavView navView;
		private NavPage consolePage;
		private InGameConsoleView consoleView;
		private Toggle debugModeToggle;
		private Toggle showDebugMeshToggle;
		private Button showDebugMeshForEveryone;
		private Button hideDebugMeshForEveryone;
		private Toggle healthPassthroughTintToggle;
		private Toggle lightEffectsToggle;
		private bool showDebugMesh;
		private UIToolkitPanelXRSetup panel;

		private void Awake()
		{
			panel = GetComponent<UIToolkitPanelXRSetup>();
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
			showDebugMeshForEveryone =
				Require<Button>(root, "show-debug-mesh-for-everyone");
			hideDebugMeshForEveryone =
				Require<Button>(root, "hide-debug-mesh-for-everyone");

			debugModeToggle.RegisterValueChangedCallback(
				change => AnaglyphDebugging.SetDebugMode(change.newValue));
			showDebugMeshToggle.RegisterValueChangedCallback(change =>
			{
				showDebugMesh = change.newValue;
				if (EnvMesher.Instance != null)
					EnvMesher.Instance.SetChunksVisible(showDebugMesh);
			});
			showDebugMeshToggle.SetValueWithoutNotify(showDebugMesh);
			showDebugMeshForEveryone.clicked +=
				() => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(true);
			hideDebugMeshForEveryone.clicked +=
				() => EnvMeshSync.Instance?.SetEnvMeshVisibleEveryone(false);

			healthPassthroughTintToggle =
				Require<Toggle>(root, "health-passthrough-tint-toggle");
			lightEffectsToggle = Require<Toggle>(root, "light-effects-toggle");

			healthPassthroughTintToggle.RegisterValueChangedCallback(
				change => healthPassthroughTintSetting.Value = change.newValue);
			lightEffectsToggle.RegisterValueChangedCallback(
				change => lightEffectsSetting.Value = change.newValue);

			Label version = Require<Label>(root, "version");
			version.text =
				$"Version: {Application.version}\nBuild: {(buildNumber ? buildNumber.Value : "")}";
		}

		private void OnEnable()
		{
			InitializeUI();

			SyncBus.Activated += UpdateDebugMeshForEveryoneEnabled;
			SyncBus.Deactivated += UpdateDebugMeshForEveryoneEnabled;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			healthPassthroughTintSetting.Changed += OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed += OnLightEffectsChanged;
			panel.VisibleChanged += OnPanelVisibleChanged;

			UpdateConsoleVisible();
			UpdateDebugMeshForEveryoneEnabled();
			OnDebugModeChanged(AnaglyphDebugging.DebugMode);
			OnHealthPassthroughTintChanged(healthPassthroughTintSetting.Value);
			OnLightEffectsChanged(lightEffectsSetting.Value);
		}

		private void OnDisable()
		{
			SyncBus.Activated -= UpdateDebugMeshForEveryoneEnabled;
			SyncBus.Deactivated -= UpdateDebugMeshForEveryoneEnabled;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			healthPassthroughTintSetting.Changed -= OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed -= OnLightEffectsChanged;
			panel.VisibleChanged -= OnPanelVisibleChanged;
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

		private void OnAuthorityChanged(bool hasAuthority)
		{
			UpdateDebugMeshForEveryoneEnabled();
		}

		// only the host may change the debug mesh for everyone mid-game
		private void UpdateDebugMeshForEveryoneEnabled()
		{
			bool canSetForEveryone = SyncBus.Active && SyncBus.IsAuthority;
			showDebugMeshForEveryone.SetEnabled(canSetForEveryone);
			hideDebugMeshForEveryone.SetEnabled(canSetForEveryone);
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

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
