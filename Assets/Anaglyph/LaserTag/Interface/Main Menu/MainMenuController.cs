using System;
using System.Threading;
using Anaglyph.Netcode;
using Anaglyph.XR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Interface
{
	public class MainMenuController : MonoBehaviour
	{
		private const float AutoCloseDistance = 5f;

		[SerializeField] private InputAction showMenuAction;

		[SerializeField] private float verticalOffset;
		[SerializeField] private float protectedMenuClickWindow = 0.8f;

		[SerializeField] private float radius = 1;
		[SerializeField] private Transform[] panels;
		[SerializeField] private float panelWidth = 0.6f;
		[SerializeField] private int centerIndex;
		[SerializeField] private float transitionLength = 0.2f;
		[SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

		private Transform camTransform => MainXRRig.Camera.transform;

		private bool hasPose;
		private float transitionProgress;
		private GateMenu gateMenu;
		private UIToolkitPanelXRSetup gatePanel;
		private UIToolkitPanelXRSetup[] menuPanels;
		private SessionConnectionController sessionConnectionController;
		private GateMenu.AccessState lastAccess;
		private GateMenu.AccessState displayedAccess;
		private bool initializedVisibility;
		private int protectedMenuPressCount;
		private float firstProtectedMenuPressTime = float.NegativeInfinity;

		public bool IsVisible { get; private set; }
		public bool IsClosing => !IsVisible && transitionProgress > 0;

		private void Awake()
		{
			gateMenu = GetComponentInChildren<GateMenu>(true);
			if (gateMenu == null)
				throw new InvalidOperationException(
					"MainMenuController requires a GateMenu in its child hierarchy.");

			gatePanel = gateMenu.GetComponent<UIToolkitPanelXRSetup>();
			sessionConnectionController = GetComponent<SessionConnectionController>();
			if (gatePanel == null || sessionConnectionController == null)
				throw new InvalidOperationException(
					"MainMenuController requires a gate panel and SessionConnectionController.");
			menuPanels = new UIToolkitPanelXRSetup[panels.Length];
			for (int i = 0; i < panels.Length; i++)
			{
				menuPanels[i] = panels[i].GetComponent<UIToolkitPanelXRSetup>();
				if (menuPanels[i] == null)
					throw new InvalidOperationException("Every menu panel requires UIToolkitPanelXRSetup.");
			}

			showMenuAction.performed += OnShowMenuPerformed;

			showMenuAction.Enable();

			MainXRRig.Recentered += SetPose;
			gateMenu.AccessChanged += OnAccessChanged;
			HeadsetConfiguration.MenuAccessChanged += OnMenuAccessChanged;
			NetcodeManagement.StateChanged += OnNetworkStateChanged;
			ApplyPanelVisibility();
			ApplyPanelLayout();
		}

		private void OnShowMenuPerformed(InputAction.CallbackContext _)
		{
			if (IsVisible || !HeadsetConfiguration.MenuPasswordRequired)
			{
				SmartToggleVisible();
				return;
			}

			float now = Time.unscaledTime;
			if (now - firstProtectedMenuPressTime > protectedMenuClickWindow)
			{
				protectedMenuPressCount = 0;
				firstProtectedMenuPressTime = now;
			}
			protectedMenuPressCount++;
			if (protectedMenuPressCount < 3)
				return;

			ResetProtectedMenuClicks();
			SetVisible(true);
		}

		private void OnAccessChanged()
		{
			GateMenu.AccessState previous = lastAccess;
			lastAccess = gateMenu.Access;
			if (!initializedVisibility)
			{
				if (!gateMenu.HasCheckedPermissions) return;
				initializedVisibility = true;
				SetVisibility(lastAccess == GateMenu.AccessState.Permissions ||
					!HeadsetConfiguration.MenuPasswordRequired, false);
			}
			else if (lastAccess == GateMenu.AccessState.Permissions && previous != lastAccess)
			{
				// Lost permissions need attention, even if the menu was closed.
				SetVisibility(true, false);
			}
			else ApplyPanelVisibility();
		}

		private void OnMenuAccessChanged()
		{
			if (HeadsetConfiguration.MenuPasswordRequired &&
				gateMenu.Access != GateMenu.AccessState.Permissions)
				SetVisibility(false, false);
			else gateMenu.ResetPasswordAccess();
		}

		private void OnNetworkStateChanged(NetcodeState state)
		{
			if (state == NetcodeState.Connected && gateMenu.Access != GateMenu.AccessState.Permissions)
				SetVisibility(false, false);
			else if (state == NetcodeState.Disconnected && !IsVisible &&
				!HeadsetConfiguration.MenuPasswordRequired)
				SetVisibility(true, false);
		}

		private void OnDestroy()
		{
			MainXRRig.Recentered -= SetPose;
			if (gateMenu != null) gateMenu.AccessChanged -= OnAccessChanged;
			HeadsetConfiguration.MenuAccessChanged -= OnMenuAccessChanged;
			NetcodeManagement.StateChanged -= OnNetworkStateChanged;
			showMenuAction.performed -= OnShowMenuPerformed;
			showMenuAction.Disable();
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused) return;

			if (IsVisible)
				SetPose();
		}

		private async void Start()
		{
			try
			{
				await Awaitable.WaitForSecondsAsync(0.5f, destroyCancellationToken);
				if (IsVisible) SetPose();
			}
			catch (OperationCanceledException) { }
		}

		private void OnEnable()
		{
			if (gateMenu.HasCheckedPermissions) OnAccessChanged();
			ApplyPanelLayout();
			ApplyPanelVisibility();
		}

		private void OnDisable()
		{
			if (gateMenu == null) return;
			transitionProgress = IsVisible ? 1 : 0;
			ApplyPanelVisibility();
		}

		private void Update()
		{
			if (IsVisible && hasPose && NetcodeManagement.State == NetcodeState.Connected &&
				MainXRRig.Camera != null)
				CheckAutoCloseDistance(camTransform.position);

			float target = IsVisible ? 1 : 0;
			if (transitionProgress != target)
			{
				transitionProgress = transitionLength <= 0 ? target
					: Mathf.MoveTowards(transitionProgress, target, Time.unscaledDeltaTime / transitionLength);
				ApplyPanelLayout();
				if (!IsVisible && transitionProgress == 0) ApplyPanelVisibility();
			}
		}

		private void CheckAutoCloseDistance(Vector3 userPosition)
		{
			// Measure walking distance from the menu's placement origin, ignoring height.
			Vector3 offset = userPosition - transform.position;
			offset.y = 0;
			if (IsVisible && offset.sqrMagnitude > AutoCloseDistance * AutoCloseDistance)
				SetVisibility(false, false);
		}

		private void ApplyPanelLayout()
		{
			float lerp = transitionCurve.Evaluate(transitionProgress);
			for (int i = 0; i < panels.Length; i++)
			{
				float radiusOffs = radius + Mathf.Abs(i - centerIndex) * 0.2f + 0.2f;
				float r = Mathf.Lerp(radiusOffs, radius, lerp);
				float angle = Mathf.Lerp(0, (i - centerIndex) * panelWidth / radius, lerp);
				Vector3 pos = new(Mathf.Sin(angle) * r, 0, Mathf.Cos(angle) * r);
				panels[i].localPosition = pos;
				panels[i].forward = transform.TransformDirection(pos);
			}
			// The permission/password panel occupies the center slot independently.
			gateMenu.transform.localPosition = new Vector3(0, 0, Mathf.Lerp(radius + 0.2f, radius, lerp));
		}

		private bool CheckIsInView()
		{
			Vector3 viewPos = MainXRRig.Camera.WorldToViewportPoint(transform.position + transform.forward);

			return viewPos.x is > 0f and < 1f && viewPos.y is > 0f and < 1f && viewPos.z > 0;
		}

		/// <summary>
		/// Toggles visibility *on screen*. If the menu is visible but *off-screen*
		/// the menu is repositioned on-screen rather than toggled off.
		/// </summary>
		public void SmartToggleVisible()
		{
			bool isInView = CheckIsInView();

			if (!isInView && IsVisible)
				SetPose();
			else
				SetVisible(!IsVisible);
		}

		/// <summary>A deliberate menu request pauses pinned-host recovery attempts.</summary>
		public void SetVisible(bool shouldBeVisible)
		{
			if (shouldBeVisible && IsVisible && !CheckIsInView()) SetPose();
			SetVisibility(shouldBeVisible, shouldBeVisible);
		}

		private void SetVisibility(bool visible, bool recoveryRequested)
		{
			bool changed = IsVisible != visible;
			IsVisible = visible;
			ResetProtectedMenuClicks();
			if (changed && visible && transitionProgress == 0) SetPose();
			if (!isActiveAndEnabled || transitionLength <= 0) transitionProgress = visible ? 1 : 0;

			// Apply the close before resetting access: the animation retains the
			// panels that were displayed, rather than switching to a password prompt.
			ApplyPanelLayout();
			ApplyPanelVisibility();
			if (!visible) gateMenu.ResetPasswordAccess();
			sessionConnectionController.SetRecoveryMenuOpen(visible && recoveryRequested);
		}

		private void ApplyPanelVisibility()
		{
			if (IsVisible) displayedAccess = gateMenu.Access;
			bool showMenu = displayedAccess == GateMenu.AccessState.Granted;
			foreach (UIToolkitPanelXRSetup panel in menuPanels)
				panel.SetVisible(IsVisible && showMenu, IsClosing && showMenu);
			gatePanel.SetVisible(IsVisible && !showMenu, IsClosing && !showMenu);
		}

		private void ResetProtectedMenuClicks()
		{
			protectedMenuPressCount = 0;
			firstProtectedMenuPressTime = float.NegativeInfinity;
		}

		private async void SetPose()
		{
			// Wait for placement before checking distance when opening or recentering.
			hasPose = false;
			CancellationToken ctkn = destroyCancellationToken;

			try
			{
				await Awaitable.EndOfFrameAsync(ctkn);

				Vector3 camPos = camTransform.position;
				transform.position = camPos + Vector3.up * verticalOffset;

				Vector3 f = camTransform.forward;
				f = new Vector3(f.x, 0, f.z).normalized;

				transform.forward = f;
				hasPose = true;

			}
			catch (OperationCanceledException)
			{

			}
		}
	}
}
