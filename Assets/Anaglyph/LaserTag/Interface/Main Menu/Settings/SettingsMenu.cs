using System;
using Anaglyph.Debugging;
using Anaglyph.InGameConsole;
using Anaglyph.Menu;
using Anaglyph.Netcode;
using Anaglyph.VariableObjects;
using Anaglyph.XR.AprilTags;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.Input;
using Anaglyph.XR.SharedSpaces;
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

		private NavView navView;
		private NavPage consolePage;
		private InGameConsoleView consoleView;
		private Toggle debugModeToggle;
		private Toggle showDebugMeshToggle;
		private DemoControlsBinder demoControls;
		private Slider debugMeshOpacitySlider;
		private RadioButtonGroup sightingModeRadio;
		private Toggle sightingRawTrackingToggle;
		private Toggle sightingRightEyeToggle;
		private Label sightingStatus;
		private Toggle healthPassthroughTintToggle;
		private Toggle lightEffectsToggle;
		private Label provisioningStatus;
		private Button unprovisionButton;
		private NavButton operatorProvisioningButton;
		private Button dominantEyeLeftButton, dominantEyeRightButton;
		private Button dominantHandLeftButton, dominantHandRightButton;
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

			dominantEyeLeftButton = Require<Button>(root, "dominant-eye-left-button");
			dominantEyeRightButton = Require<Button>(root, "dominant-eye-right-button");
			dominantHandLeftButton = Require<Button>(root, "dominant-hand-left-button");
			dominantHandRightButton = Require<Button>(root, "dominant-hand-right-button");
			bindings.Click(dominantEyeLeftButton, () => Dominance.Eye = Handedness.Left);
			bindings.Click(dominantEyeRightButton, () => Dominance.Eye = Handedness.Right);
			bindings.Click(dominantHandLeftButton, () => Dominance.Hand = Handedness.Left);
			bindings.Click(dominantHandRightButton, () => Dominance.Hand = Handedness.Right);

			debugModeToggle = Require<Toggle>(root, "debug-mode-toggle");
			showDebugMeshToggle = Require<Toggle>(root, "show-debug-mesh-toggle");
			demoControls = new DemoControlsBinder(navView.GetPage("demo-page"));

			bindings.Value(debugModeToggle,
				change => AnaglyphDebugging.SetDebugMode(change.newValue));
			bindings.Value(showDebugMeshToggle, change =>
			{
				showDebugMesh = change.newValue;
				if (EnvMesher.Instance != null)
					EnvMesher.Instance.SetChunksVisible(showDebugMesh);
			});
			showDebugMeshToggle.SetValueWithoutNotify(showDebugMesh);

			debugMeshOpacitySlider = Require<Slider>(root, "debug-mesh-opacity-slider");
			bindings.Value(debugMeshOpacitySlider,
				change => EnvMesher.DebugMeshOpacity = change.newValue);
			debugMeshOpacitySlider.SetValueWithoutNotify(EnvMesher.DebugMeshOpacity);

			sightingModeRadio = Require<RadioButtonGroup>(root, "sighting-mode-radio");
			sightingRawTrackingToggle = Require<Toggle>(root, "sighting-raw-tracking-toggle");
			sightingRightEyeToggle = Require<Toggle>(root, "sighting-right-eye-toggle");
			sightingStatus = Require<Label>(root, "sighting-status");
			bindings.Value(sightingModeRadio,
				change => AlignmentSightingTool.Mode = (AlignmentSightingTool.ToolMode)change.newValue);
			bindings.Value(sightingRawTrackingToggle,
				change => AlignmentSightingTool.ShowRawTracking = change.newValue);
			bindings.Value(sightingRightEyeToggle,
				change => AlignmentSightingTool.CrosshairInRightEye = change.newValue);
			bindings.Click(Require<Button>(root, "sighting-clear-button"), AlignmentSightingTool.ClearMarkers);

			Toggle forceCamera2Toggle = Require<Toggle>(root, "force-camera2-toggle");
			bindings.Value(forceCamera2Toggle, change => PassthroughFrames.ForceCamera2 = change.newValue);
			forceCamera2Toggle.SetValueWithoutNotify(PassthroughFrames.ForceCamera2);

			if (!Debug.isDebugBuild)
			{
				Require<VisualElement>(root, "alignment-sighting-button").style.display = DisplayStyle.None;
				forceCamera2Toggle.style.display = DisplayStyle.None;
			}

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

			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			AlignmentSightingTool.Changed += OnSightingChanged;
			healthPassthroughTintSetting.Changed += OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed += OnLightEffectsChanged;
			panel.VisibleChanged += OnPanelVisibleChanged;
			HeadsetConfiguration.Changed += RefreshHeadsetConfiguration;
			Dominance.Changed += OnDominanceChanged;

			UpdateConsoleVisible();
			OnDebugModeChanged(AnaglyphDebugging.DebugMode);
			OnSightingChanged();
			OnHealthPassthroughTintChanged(healthPassthroughTintSetting.Value);
			OnLightEffectsChanged(lightEffectsSetting.Value);
			RefreshHeadsetConfiguration();
			OnDominanceChanged();
		}

		private void OnDisable()
		{
			bindings.Dispose();
			MenuCopy.Changed -= RefreshHeadsetConfiguration;
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			AlignmentSightingTool.Changed -= OnSightingChanged;
			healthPassthroughTintSetting.Changed -= OnHealthPassthroughTintChanged;
			lightEffectsSetting.Changed -= OnLightEffectsChanged;
			if (panel != null) panel.VisibleChanged -= OnPanelVisibleChanged;
			HeadsetConfiguration.Changed -= RefreshHeadsetConfiguration;
			Dominance.Changed -= OnDominanceChanged;
			if (navView != null)
			{
				navView.Changed -= OnNavPageChange;
				navView = null;
			}

			consoleView?.Dispose();
			consoleView = null;
			demoControls?.Dispose();
			demoControls = null;
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

		// a debugging tool: the status reports its readings verbatim, unlocalized
		private void OnSightingChanged()
		{
			sightingModeRadio.SetValueWithoutNotify((int)AlignmentSightingTool.Mode);
			sightingRawTrackingToggle.SetValueWithoutNotify(AlignmentSightingTool.ShowRawTracking);
			sightingRightEyeToggle.SetValueWithoutNotify(AlignmentSightingTool.CrosshairInRightEye);
			sightingStatus.text = AlignmentSightingTool.Status;
		}

		private void OnDominanceChanged()
		{
			const string selected = "option--selected";
			dominantEyeLeftButton.EnableInClassList(selected, Dominance.Eye == Handedness.Left);
			dominantEyeRightButton.EnableInClassList(selected, Dominance.Eye == Handedness.Right);
			dominantHandLeftButton.EnableInClassList(selected, Dominance.Hand == Handedness.Left);
			dominantHandRightButton.EnableInClassList(selected, Dominance.Hand == Handedness.Right);
		}

		private void OnLightEffectsChanged(bool enabled)
		{
			lightEffectsToggle.SetValueWithoutNotify(enabled);
		}

		private void OnHealthPassthroughTintChanged(bool enabled)
		{
			healthPassthroughTintToggle.SetValueWithoutNotify(enabled);
		}

		// a debugging tool: the status reports the folder path verbatim, unlocalized
		// private async void CaptureAlignmentDiagnosis()
		// {
		// 	if (AlignmentDiagnosis.InProgress) return;
		// 	captureAlignmentDiagnosisButton.SetEnabled(false);
		// 	alignmentDiagnosisStatus.text = "Capturing…";
		// 	try
		// 	{
		// 		string folder = await AlignmentDiagnosis.CaptureAsync();
		// 		if (alignmentDiagnosisStatus != null) alignmentDiagnosisStatus.text = $"Saved to {folder}";
		// 	}
		// 	catch (Exception e)
		// 	{
		// 		Debug.LogException(e, this);
		// 		if (alignmentDiagnosisStatus != null) alignmentDiagnosisStatus.text = $"Capture failed: {e.Message}";
		// 	}
		// 	finally
		// 	{
		// 		captureAlignmentDiagnosisButton?.SetEnabled(true);
		// 	}
		// }

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
				? MenuCopy.Format("SettingsMenu", locked ? "provisioning.host-locked" : "provisioning.host", HeadsetConfiguration.PinnedHostAddress)
				: MenuCopy.Get("SettingsMenu", locked ? "provisioning.locked" : "provisioning.none");
			unprovisionButton.style.display = provisioned ? DisplayStyle.Flex : DisplayStyle.None;
			operatorProvisioningButton.style.display = provisioned ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
