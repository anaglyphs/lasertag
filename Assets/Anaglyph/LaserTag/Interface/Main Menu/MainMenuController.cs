using System;
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
		[SerializeField] private float sessionPanelTransitionLength = 0.2f;
		[SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
		[SerializeField] private float sessionPanelScaleWhenAbsent = 0.8f;
		
		[SerializeField] private UIToolkitPanelXRSetup statusBar;
		[SerializeField] private UIToolkitPanelXRSetup closeBar;
		[SerializeField] private float barGapInMeters = 0.02f;

		private Transform camTransform => MainXRRig.Camera.transform;

		private bool hasPose;
		private bool isWaitingForOpeningPose;
		private bool openingRequestsRecovery;
		private float transitionProgress;
		// the game panel: shown only in a session, in the center, the other panels closing the gap without it
		private int sessionPanelIndex = -1;
		private Vector3 sessionPanelScale = Vector3.one;
		private float sessionPanelPresence;
		private bool inSession;
		private GateMenu gateMenu;
		private UIToolkitPanelXRSetup gatePanel;
		private UIToolkitPanelXRSetup[] menuPanels;
		private SessionDiscoveryController sessionDiscoveryController;
		private GateMenu.AccessState lastAccess;
		private GateMenu.AccessState displayedAccess;
		private bool initializedVisibility;
		private int protectedMenuPressCount;
		private float firstProtectedMenuPressTime = float.NegativeInfinity;

		public bool IsVisible { get; private set; }
		public bool IsClosing => !IsVisible && transitionProgress > 0;

		public GateMenu Gate => gateMenu;
		public Transform[] Panels => panels;
		public event Action VisibilityChanged = delegate { };

		/// <summary>
		/// On a phone the panels are flat on the screen, one at a time: none are placed in the room, none
		/// close because the player walked off, and only <see cref="ScreenPanel"/> shows.
		/// </summary>
		public bool OnScreen { get; private set; }

		private int screenPanel;

		/// <summary>Which of <see cref="Panels"/> shows while <see cref="OnScreen"/>.</summary>
		public int ScreenPanel
		{
			get => screenPanel;
			set
			{
				int panel = Mathf.Clamp(value, 0, panels.Length - 1);
				if (!IsPanelAvailable(panel)) return;
				screenPanel = panel;
				ApplyPanelVisibility();
			}
		}

		/// <summary>The game panel is available only in a session.</summary>
		public bool IsPanelAvailable(int panel) => panel != sessionPanelIndex || inSession;

		public void ShowOnScreen(int initialPanel)
		{
			OnScreen = true;
			ScreenPanel = initialPanel;
		}

		private void Awake()
		{
			gateMenu = GetComponentInChildren<GateMenu>(true);
			if (gateMenu == null)
				throw new InvalidOperationException(
					"MainMenuController requires a GateMenu in its child hierarchy.");

			gatePanel = gateMenu.GetComponent<UIToolkitPanelXRSetup>();
			sessionDiscoveryController = GetComponent<SessionDiscoveryController>();
			if (gatePanel == null || sessionDiscoveryController == null)
				throw new InvalidOperationException(
					"MainMenuController requires a gate panel and SessionConnectionController.");
			menuPanels = new UIToolkitPanelXRSetup[panels.Length];
			for (int i = 0; i < panels.Length; i++)
			{
				menuPanels[i] = panels[i].GetComponent<UIToolkitPanelXRSetup>();
				if (menuPanels[i] == null)
					throw new InvalidOperationException("Every menu panel requires UIToolkitPanelXRSetup.");
			}
			if (statusBar == null || closeBar == null)
				throw new InvalidOperationException("MainMenuController requires a status bar and a close bar.");

			FindSessionPanel();
			inSession = NetcodeManagement.State == NetcodeState.Connected;
			sessionPanelPresence = inSession ? 1 : 0;

			showMenuAction.performed += OnShowMenuPerformed;

			showMenuAction.Enable();

			MainXRRig.Recentered += SetPose;
			gateMenu.AccessChanged += OnAccessChanged;
			HeadsetConfiguration.MenuAccessChanged += OnMenuAccessChanged;
			NetcodeManagement.StateChanged += OnNetworkStateChanged;
			ApplyPanelVisibility();
			ApplyPanelLayout();
		}

		private void FindSessionPanel()
		{
			for (int i = 0; i < panels.Length; i++)
				if (panels[i].GetComponent<GameMenu>() != null)
				{
					sessionPanelIndex = i;
					sessionPanelScale = panels[i].localScale;
				}
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
			inSession = state == NetcodeState.Connected;
			if (!IsPanelAvailable(screenPanel)) ScreenPanel = FirstAvailablePanel();
			if (state == NetcodeState.Connected && !NetcodeManagement.IsHosting &&
				gateMenu.Access != GateMenu.AccessState.Permissions)
				SetVisibility(false, false);
			else if (state == NetcodeState.Disconnected && !IsVisible && !HeadsetConfiguration.MenuPasswordRequired)
				SetVisibility(true, false);
			else ApplyPanelVisibility();
		}

		private int FirstAvailablePanel()
		{
			for (int i = 0; i < panels.Length; i++)
				if (IsPanelAvailable(i)) return i;
			return 0;
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

		private void Start() => SetPoseAfterStartupDelay().RunUntilCancelled();

		private async Awaitable SetPoseAfterStartupDelay()
		{
			await Awaitable.WaitForSecondsAsync(0.5f, destroyCancellationToken);
			if (IsVisible) SetPose();
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
			if (!OnScreen && IsVisible && hasPose && NetcodeManagement.State == NetcodeState.Connected &&
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

			float presenceTarget = inSession ? 1 : 0;
			if (sessionPanelPresence != presenceTarget)
			{
				// nothing to watch while the menu is closed or flat on a screen
				bool animated = !OnScreen && transitionProgress > 0 && sessionPanelTransitionLength > 0;
				sessionPanelPresence = !animated ? presenceTarget
					: Mathf.MoveTowards(sessionPanelPresence, presenceTarget, Time.unscaledDeltaTime / sessionPanelTransitionLength);
				ApplyPanelLayout();
				if (sessionPanelPresence == presenceTarget) ApplyPanelVisibility();
			}
		}

		/// <summary>Where a panel sits around the player, in panel widths from the center.</summary>
		private float SlotOf(int panel, float sessionPanelLerp)
		{
			float slotInSession = panel - centerIndex;
			if (sessionPanelIndex < 0 || panel == sessionPanelIndex) return slotInSession;
			int indexAmongTheOthers = panel > sessionPanelIndex ? panel - 1 : panel;
			float slotOutOfSession = indexAmongTheOthers - (panels.Length - 2) * 0.5f;
			return Mathf.Lerp(slotOutOfSession, slotInSession, sessionPanelLerp);
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
			if (OnScreen) return;
			float lerp = transitionCurve.Evaluate(transitionProgress);
			float sessionPanelLerp = transitionCurve.Evaluate(sessionPanelPresence);
			for (int i = 0; i < panels.Length; i++)
			{
				float slot = SlotOf(i, sessionPanelLerp);
				float radiusOffs = radius + Mathf.Abs(slot) * 0.2f + 0.2f;
				float r = Mathf.Lerp(radiusOffs, radius, lerp);
				if (i == sessionPanelIndex)
				{
					r += (1 - sessionPanelLerp) * 0.2f;
					panels[i].localScale = sessionPanelScale * Mathf.Lerp(sessionPanelScaleWhenAbsent, 1, sessionPanelLerp);
				}
				float angle = Mathf.Lerp(0, slot * panelWidth / radius, lerp);
				Vector3 pos = new(Mathf.Sin(angle) * r, 0, Mathf.Cos(angle) * r);
				panels[i].localPosition = pos;
				panels[i].forward = transform.TransformDirection(pos);
			}
			// The permission/password panel occupies the center slot independently.
			float centerDistance = Mathf.Lerp(radius + 0.2f, radius, lerp);
			gateMenu.transform.localPosition = new Vector3(0, 0, centerDistance);

			float statusBarBottom = gatePanel.EdgeHeightAbovePivot(1) + barGapInMeters;
			float closeBarTop = gatePanel.EdgeHeightAbovePivot(-1) - barGapInMeters;
			PlaceInCenterSlot(statusBar, statusBarBottom - statusBar.EdgeHeightAbovePivot(-1), centerDistance);
			PlaceInCenterSlot(closeBar, closeBarTop - closeBar.EdgeHeightAbovePivot(1), centerDistance);
		}

		private static void PlaceInCenterSlot(UIToolkitPanelXRSetup panel, float height, float distance) =>
			panel.transform.SetLocalPositionAndRotation(new Vector3(0, height, distance), Quaternion.identity);

		private bool CheckIsInView()
		{
			if (OnScreen) return true;
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

		/// <summary>Opening from fully closed waits for the end-of-frame pose; nothing changes until then.</summary>
		private void SetVisibility(bool visible, bool recoveryRequested)
		{
			ResetProtectedMenuClicks();
			bool opensFromClosed = visible && !IsVisible && transitionProgress == 0 && !OnScreen;
			if (opensFromClosed)
			{
				openingRequestsRecovery = recoveryRequested;
				if (!isWaitingForOpeningPose) SetPose();
				isWaitingForOpeningPose = true;
				return;
			}
			isWaitingForOpeningPose = false;
			ApplyVisibility(visible, recoveryRequested);
		}

		private void ApplyVisibility(bool visible, bool recoveryRequested)
		{
			bool changed = IsVisible != visible;
			IsVisible = visible;
			if (!isActiveAndEnabled || transitionLength <= 0) transitionProgress = visible ? 1 : 0;

			// Apply the close before resetting access: the animation retains the
			// panels that were displayed, rather than switching to a password prompt.
			ApplyPanelLayout();
			ApplyPanelVisibility();
			if (!visible) gateMenu.ResetPasswordAccess();
			sessionDiscoveryController.SetRecoveryMenuOpen(visible && recoveryRequested);
			if (changed) VisibilityChanged.Invoke();
		}

		private void ApplyPanelVisibility()
		{
			if (IsVisible) displayedAccess = gateMenu.Access;
			bool showMenu = displayedAccess == GateMenu.AccessState.Granted;
			for (int i = 0; i < menuPanels.Length; i++)
			{
				bool shown = showMenu && (!OnScreen || i == screenPanel);
				bool usable = shown && IsPanelAvailable(i);
				bool leaving = shown && !usable && sessionPanelPresence > 0;
				menuPanels[i].SetVisible(IsVisible && usable, IsClosing && usable || leaving && (IsVisible || IsClosing));
			}
			gatePanel.SetVisible(IsVisible && !showMenu, IsClosing && !showMenu);
			statusBar.SetVisible(IsVisible && !OnScreen, IsClosing && !OnScreen);
			closeBar.SetVisible(IsVisible && !OnScreen, IsClosing && !OnScreen);
		}

		private void ResetProtectedMenuClicks()
		{
			protectedMenuPressCount = 0;
			firstProtectedMenuPressTime = float.NegativeInfinity;
		}

		private void SetPose() => SetPoseAtEndOfFrame().RunUntilCancelled();

		private async Awaitable SetPoseAtEndOfFrame()
		{
			if (!OnScreen)
			{
				// Wait for placement before checking distance when opening or recentering.
				hasPose = false;

				await Awaitable.EndOfFrameAsync(destroyCancellationToken);
			}

			if (!OnScreen && MainXRRig.Camera != null)
			{
				Vector3 camPos = camTransform.position;
				transform.position = camPos + Vector3.up * verticalOffset;

				Vector3 f = camTransform.forward;
				f = new Vector3(f.x, 0, f.z).normalized;

				transform.forward = f;
				hasPose = true;
			}

			if (isWaitingForOpeningPose)
			{
				isWaitingForOpeningPose = false;
				ApplyVisibility(true, openingRequestsRecovery);
			}
		}
	}
}
