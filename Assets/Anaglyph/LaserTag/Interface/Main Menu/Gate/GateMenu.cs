using System;
using Anaglyph.Menu;
using Anaglyph.Permissions;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	// UIDocument builds its visual tree at the default execution order, while the
	// three normal menu controllers initialize at 100.
	[DefaultExecutionOrder(50)]
	public sealed class GateMenu : MonoBehaviour
	{
		[SerializeField] private UIDocument permissionDocument;

		public enum AccessState { Permissions, Password, Granted }

		public AccessState Access { get; private set; }
		public bool HasCheckedPermissions { get; private set; }
		public event Action AccessChanged = delegate { };
		private bool passwordUnlocked;
		private bool requiredPermissionsGranted;
		private bool sceneRequestInFlight;
		private bool cameraRequestInFlight;
		private bool limitedSupportAcknowledged;
		private string statusKey;
		private SessionDiscoveryController sessionDiscoveryController;
		private UIToolkitPanelXRSetup gatePanel;

		private NavView navView;
		private NavPage limitedSupportModal;
		private NavPage passwordPage;
		private Toggle scenePermissionToggle;
		private Toggle cameraPermissionToggle;
		private Label statusLabel;
		private TextField passwordField;
		private Label passwordError;
		private Button unlockButton;
		private Button acknowledgeButton;

		private void Awake()
		{
			if (permissionDocument == null)
				throw new InvalidOperationException(
					"GateMenu requires a permission UIDocument.");

			sessionDiscoveryController =
				GetComponentInParent<SessionDiscoveryController>();
			if (sessionDiscoveryController == null)
				throw new InvalidOperationException(
					"GateMenu requires SessionConnectionController in its parent hierarchy.");
			gatePanel = GetComponent<UIToolkitPanelXRSetup>();
			if (gatePanel == null)
				throw new InvalidOperationException(
					"GateMenu requires UIToolkitPanelXRSetup on the same GameObject.");

			gatePanel.VisibleChanged += OnPanelVisibilityChanged;
		}

		private void OnEnable()
		{
			InitializeUI();
			MenuCopy.Changed += UpdateStatus;
			RefreshGate();
		}

		private void OnDisable()
		{
			MenuCopy.Changed -= UpdateStatus;
			DisposeUI();
		}

		private void OnDestroy()
		{
			if (gatePanel != null) gatePanel.VisibleChanged -= OnPanelVisibilityChanged;
			sessionDiscoveryController?.SetRequiredPermissionsGranted(false);
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
			bool firstCheck = !HasCheckedPermissions;
			requiredPermissionsGranted = permissionsSatisfied && !warnLimitedSupport;
			if (!requiredPermissionsGranted) passwordUnlocked = false;
			HasCheckedPermissions = true;

			UpdatePermissionToggle(scenePermissionToggle, sceneAuthorization, sceneRequestInFlight);
			UpdatePermissionToggle(cameraPermissionToggle, cameraAuthorization, cameraRequestInFlight);
			UpdateStatus();
			navView.SetModalPresented(limitedSupportModal, warnLimitedSupport, 100);
			sessionDiscoveryController.SetRequiredPermissionsGranted(requiredPermissionsGranted);
			RefreshAccess(firstCheck);
		}

		/// <summary>Closing the menu or changing policy ends the password grant.</summary>
		public void ResetPasswordAccess()
		{
			passwordUnlocked = false;
			ClearPassword();
			RefreshAccess();
		}

		private void RefreshAccess(bool notify = false)
		{
			AccessState next = !requiredPermissionsGranted ? AccessState.Permissions
				: HeadsetConfiguration.MenuPasswordRequired && !passwordUnlocked
					? AccessState.Password : AccessState.Granted;
			bool changed = Access != next;
			Access = next;

			if (navView != null)
			{
				navView.SetModalPresented(passwordPage, Access == AccessState.Password, 1000);
				if (changed) ClearPassword();
				UpdatePasswordFocus();
			}
			if (changed || notify) AccessChanged.Invoke();
		}

		private void OnPanelVisibilityChanged(bool visible)
		{
			if (!visible) ClearPassword();
			UpdatePasswordFocus();
		}

		private void ClearPassword()
		{
			if (passwordField == null) return;
			passwordField.SetValueWithoutNotify("");
			passwordError.style.display = DisplayStyle.None;
		}

		private void UpdatePasswordFocus()
		{
			if (passwordField == null) return;
			if (gatePanel.IsVisible && Access == AccessState.Password) passwordField.Focus();
			else passwordField.Blur();
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

		private void InitializeUI()
		{
			if (navView != null)
				return;

			VisualElement root = permissionDocument.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"GateMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = NavView.RequireIn(root);
			limitedSupportModal = navView.GetPage("limited-support-modal");
			passwordPage = navView.GetPage("password-page");

			acknowledgeButton = Require<Button>(root, "acknowledge-limited-support-button");
			acknowledgeButton.clicked += OnLimitedSupportAcknowledged;

			scenePermissionToggle =
				Require<Toggle>(root, "scene-permission-toggle");
			cameraPermissionToggle =
				Require<Toggle>(root, "camera-permission-toggle");
			statusLabel = Require<Label>(root, "permission-status");
			passwordField = Require<TextField>(root, "menu-password-field");
			passwordField.isPasswordField = true;
			passwordError = Require<Label>(root, "menu-password-error");
			unlockButton = Require<Button>(root, "unlock-menu-button");
			unlockButton.clicked += SubmitPassword;

			scenePermissionToggle.RegisterValueChangedCallback(
				OnScenePermissionToggleChanged);
			cameraPermissionToggle.RegisterValueChangedCallback(
				OnCameraPermissionToggleChanged);
		}

		private void DisposeUI()
		{
			if (unlockButton != null) unlockButton.clicked -= SubmitPassword;
			if (acknowledgeButton != null) acknowledgeButton.clicked -= OnLimitedSupportAcknowledged;
			unlockButton = null;
			acknowledgeButton = null;
			if (scenePermissionToggle != null)
				scenePermissionToggle.UnregisterValueChangedCallback(
					OnScenePermissionToggleChanged);
			if (cameraPermissionToggle != null)
				cameraPermissionToggle.UnregisterValueChangedCallback(
					OnCameraPermissionToggleChanged);

			navView = null;
			limitedSupportModal = null;
			passwordPage = null;
			scenePermissionToggle = null;
			cameraPermissionToggle = null;
			statusLabel = null;
			passwordField = null;
			passwordError = null;
		}

		private void SubmitPassword()
		{
			if (Access != AccessState.Password || !gatePanel.IsVisible)
				return;

			if (HeadsetConfiguration.Instance == null ||
			    !HeadsetConfiguration.Instance.CheckMenuPassword(passwordField.value))
			{
				passwordError.style.display = DisplayStyle.Flex;
				passwordField.SetValueWithoutNotify("");
				passwordField.Focus();
				return;
			}

			passwordUnlocked = true;
			RefreshAccess();
		}

		private void OnScenePermissionToggleChanged(ChangeEvent<bool> change)
		{
			if (!change.newValue || sceneRequestInFlight)
				return;

			scenePermissionToggle.SetValueWithoutNotify(false);
			sceneRequestInFlight = true;
			statusKey = "permission.request-room";
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
			statusKey = "permission.request-camera";
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

			statusKey = MessageFor(result);

			if (isActiveAndEnabled)
				RefreshGate();
		}

		private void UpdateStatus()
		{
			if (statusLabel == null)
				return;

			statusLabel.text = MenuCopy.Get("GateMenu", statusKey) ?? "";
			statusLabel.style.display =
				string.IsNullOrEmpty(statusKey)
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
					"permission.denied",
				PermissionRequestOutcome.Unavailable =>
					"permission.unavailable",
				PermissionRequestOutcome.AvailabilityUnknown =>
					"permission.unknown",
				_ => null
			};
		}
	}
}
