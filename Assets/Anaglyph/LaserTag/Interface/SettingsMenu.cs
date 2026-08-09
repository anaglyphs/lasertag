using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.Menu.UIToolkit;
using Anaglyph.Netcode;
using System;
using UnityEngine;
using UnityEngine.UIElements;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(100)]
	public class SettingsMenu : MonoBehaviour
	{
		[SerializeField] private BoolObject lightEffectsSetting;
		[SerializeField] private StringObject buildNumber;

		private UIToolkitNavPages navView;
		private UIToolkitNavPage consolePage;
		private InGameConsoleView consoleView;
		private Toggle debugModeToggle;
		private Toggle showDebugMeshToggle;
		private Button showDebugMeshForEveryone;
		private Button hideDebugMeshForEveryone;
		private Toggle lightEffectsToggle;
		private bool showDebugMesh;

		private void InitializeUI()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"SettingsMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = new UIToolkitNavPages(Require<VisualElement>(root, "pages"));
			UIToolkitNavPage homePage = navView.AddPage("home-page", false);
			UIToolkitNavPage debuggingPage = navView.AddPage("debugging-page");
			UIToolkitNavPage graphicsPage = navView.AddPage("graphics-page");
			consolePage = navView.AddPage("console-page");

			Require<Button>(root, "debugging-button").clicked += debuggingPage.NavigateHere;
			Require<Button>(root, "graphics-button").clicked += graphicsPage.NavigateHere;
			Require<Button>(root, "console-button").clicked += consolePage.NavigateHere;

			// the console only processes log messages while its page is on screen
			consoleView = new InGameConsoleView(consolePage.Root);
			navView.Changed += OnNavPageChanged;

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

			lightEffectsToggle = Require<Toggle>(root, "light-effects-toggle");

			lightEffectsToggle.RegisterValueChangedCallback(
				change => lightEffectsSetting.Value = change.newValue);

			Label version = Require<Label>(root, "version");
			version.text =
				$"Version: {Application.version}\nBuild: {(buildNumber ? buildNumber.Value : "")}";

			navView.Start(homePage);
		}

		private void OnEnable()
		{
			InitializeUI();

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			lightEffectsSetting.Changed += OnLightEffectsChanged;

			OnNetcodeStateChanged(NetcodeManagement.State);
			OnDebugModeChanged(AnaglyphDebugging.DebugMode);
			OnLightEffectsChanged(lightEffectsSetting.Value);
		}

		private void OnDisable()
		{
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			lightEffectsSetting.Changed -= OnLightEffectsChanged;
			navView?.Dispose();
			navView = null;
			consoleView?.Dispose();
			consoleView = null;
			consolePage = null;
		}

		private void OnNavPageChanged(UIToolkitNavPage page)
		{
			consoleView?.SetVisible(page == consolePage);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			bool connected = state == NetcodeState.Connected;
			showDebugMeshForEveryone.SetEnabled(connected);
			hideDebugMeshForEveryone.SetEnabled(connected);
		}

		private void OnDebugModeChanged(bool enabled)
		{
			debugModeToggle.SetValueWithoutNotify(enabled);
		}

		private void OnLightEffectsChanged(bool enabled)
		{
			lightEffectsToggle.SetValueWithoutNotify(enabled);
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
