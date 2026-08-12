using Anaglyph.Menu.UIToolkit;
using Anaglyph.Permissions;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	// UIDocument builds its visual tree at the default execution order, while the
	// three normal menu controllers initialize at 100.
	[DefaultExecutionOrder(50)]
	public sealed class PermissionGateMenu : MonoBehaviour
	{
		[SerializeField] private UIDocument permissionDocument;
		[SerializeField] private GameObject[] gatedPanels;

		private bool[] gatedPanelStates;
		private bool gateOpen;
		private bool sceneRequestInFlight;
		private bool cameraRequestInFlight;
		private bool limitedSupportAcknowledged;
		private string statusMessage;

		private UIToolkitNavPages navView;
		private UIToolkitNavPage limitedSupportModal;
		private Toggle scenePermissionToggle;
		private Toggle cameraPermissionToggle;
		private Label statusLabel;

		private void Awake()
		{
			if (permissionDocument == null)
				throw new InvalidOperationException(
					"PermissionGateMenu requires a permission UIDocument.");

			gatedPanelStates = new bool[gatedPanels?.Length ?? 0];
			CacheGatedPanelStates();
			SetGatedPanelsActive(false);
			permissionDocument.gameObject.SetActive(true);
		}

		private void OnEnable()
		{
			RefreshGate();
		}

		private void OnDisable()
		{
			DisposeUI();
		}

		private void OnApplicationFocus(bool hasFocus)
		{
			if (hasFocus && isActiveAndEnabled)
				RefreshGate();
		}

		private void OnApplicationPause(bool isPaused)
		{
			if (!isPaused && isActiveAndEnabled)
				RefreshGate();
		}

		private void RefreshGate()
		{
			PermissionAuthorization sceneAuthorization =
				MetaPermissionChecks.CheckScene().authorization;
			PermissionAuthorization cameraAuthorization =
				MetaPermissionChecks.CheckPassthroughCamera().authorization;

			bool permissionsSatisfied =
				IsSatisfied(sceneAuthorization) && IsSatisfied(cameraAuthorization);
			bool warnLimitedSupport = permissionsSatisfied && NeedsLimitedSupportWarning();

			if (permissionsSatisfied && !warnLimitedSupport)
			{
				OpenGate();
				return;
			}

			CloseGate();
			UpdatePermissionToggle(
				scenePermissionToggle,
				sceneAuthorization,
				sceneRequestInFlight);
			UpdatePermissionToggle(
				cameraPermissionToggle,
				cameraAuthorization,
				cameraRequestInFlight);
			UpdateStatus();
			navView.SetModalPresented(limitedSupportModal, warnLimitedSupport, 100);
		}

		// Quest 2 and Quest Pro cannot measure depth, so they cannot map a room
		// themselves and depend on another player's headset streaming one to them.
		private bool NeedsLimitedSupportWarning()
		{
			return !limitedSupportAcknowledged &&
				MetaPermissionChecks.CheckEnvironmentDepth() == CapabilitySupport.Unsupported;
		}

		private void OnLimitedSupportAcknowledged()
		{
			limitedSupportAcknowledged = true;
			RefreshGate();
		}

		private void OpenGate()
		{
			if (gateOpen)
				return;

			gateOpen = true;
			DisposeUI();
			permissionDocument.gameObject.SetActive(false);
			RestoreGatedPanelStates();
		}

		private void CloseGate()
		{
			if (gateOpen)
				CacheGatedPanelStates();

			gateOpen = false;
			SetGatedPanelsActive(false);

			if (!permissionDocument.gameObject.activeSelf)
				permissionDocument.gameObject.SetActive(true);

			InitializeUI();
		}

		private void InitializeUI()
		{
			if (navView != null)
				return;

			VisualElement root = permissionDocument.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"PermissionGateMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = new UIToolkitNavPages(Require<VisualElement>(root, "pages"));
			UIToolkitNavPage permissionPage =
				navView.AddPage("permissions-page", false);
			limitedSupportModal = navView.AddPage("limited-support-modal", false);

			Require<Button>(root, "acknowledge-limited-support-button").clicked +=
				OnLimitedSupportAcknowledged;

			scenePermissionToggle =
				Require<Toggle>(root, "scene-permission-toggle");
			cameraPermissionToggle =
				Require<Toggle>(root, "camera-permission-toggle");
			statusLabel = Require<Label>(root, "permission-status");

			scenePermissionToggle.RegisterValueChangedCallback(
				OnScenePermissionToggleChanged);
			cameraPermissionToggle.RegisterValueChangedCallback(
				OnCameraPermissionToggleChanged);

			navView.Start(permissionPage);
		}

		private void DisposeUI()
		{
			if (scenePermissionToggle != null)
				scenePermissionToggle.UnregisterValueChangedCallback(
					OnScenePermissionToggleChanged);
			if (cameraPermissionToggle != null)
				cameraPermissionToggle.UnregisterValueChangedCallback(
					OnCameraPermissionToggleChanged);

			navView?.Dispose();
			navView = null;
			limitedSupportModal = null;
			scenePermissionToggle = null;
			cameraPermissionToggle = null;
			statusLabel = null;
		}

		private void OnScenePermissionToggleChanged(ChangeEvent<bool> change)
		{
			if (!change.newValue || sceneRequestInFlight)
				return;

			scenePermissionToggle.SetValueWithoutNotify(false);
			sceneRequestInFlight = true;
			statusMessage = "Requesting room-scanning permission...";
			RefreshGate();
			MetaPermissionChecks.RequestScenePermission(
				result => OnPermissionRequestCompleted(result, true));
		}

		private void OnCameraPermissionToggleChanged(ChangeEvent<bool> change)
		{
			if (!change.newValue || cameraRequestInFlight)
				return;

			cameraPermissionToggle.SetValueWithoutNotify(false);
			cameraRequestInFlight = true;
			statusMessage = "Requesting passthrough-camera permission...";
			RefreshGate();
			MetaPermissionChecks.RequestPassthroughCameraPermission(
				result => OnPermissionRequestCompleted(result, false));
		}

		private void OnPermissionRequestCompleted(
			PermissionRequestResult result,
			bool wasSceneRequest)
		{
			if (wasSceneRequest)
				sceneRequestInFlight = false;
			else
				cameraRequestInFlight = false;

			statusMessage = MessageFor(result);

			if (isActiveAndEnabled)
				RefreshGate();
		}

		private void CacheGatedPanelStates()
		{
			if (gatedPanels == null)
				return;

			for (int i = 0; i < gatedPanels.Length; i++)
				if (gatedPanels[i] != null)
					gatedPanelStates[i] = gatedPanels[i].activeSelf;
		}

		private void RestoreGatedPanelStates()
		{
			if (gatedPanels == null)
				return;

			for (int i = 0; i < gatedPanels.Length; i++)
				if (gatedPanels[i] != null)
					gatedPanels[i].SetActive(gatedPanelStates[i]);
		}

		private void SetGatedPanelsActive(bool active)
		{
			if (gatedPanels == null)
				return;

			foreach (GameObject panel in gatedPanels)
				if (panel != null)
					panel.SetActive(active);
		}

		private void UpdateStatus()
		{
			if (statusLabel == null)
				return;

			statusLabel.text = statusMessage ?? "";
			statusLabel.style.display =
				string.IsNullOrEmpty(statusMessage)
					? DisplayStyle.None
					: DisplayStyle.Flex;
		}

		private static void UpdatePermissionToggle(
			Toggle toggle,
			PermissionAuthorization authorization,
			bool requestInFlight)
		{
			bool satisfied = IsSatisfied(authorization);
			toggle.SetValueWithoutNotify(satisfied);
			toggle.SetEnabled(!satisfied && !requestInFlight);
			toggle.EnableInClassList("permission-granted", satisfied);
		}

		private static bool IsSatisfied(PermissionAuthorization authorization)
		{
			return authorization is
				PermissionAuthorization.Granted or
				PermissionAuthorization.NotRequired;
		}

		private static string MessageFor(PermissionRequestResult result)
		{
			return result.outcome switch
			{
				PermissionRequestOutcome.Denied or
					PermissionRequestOutcome.Dismissed =>
					"Permission was not granted. Select it to try again.",
				PermissionRequestOutcome.Unavailable =>
					"This permission is not available on this device.",
				PermissionRequestOutcome.AvailabilityUnknown =>
					"Could not determine whether this permission is available. Try again.",
				_ => null
			};
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
