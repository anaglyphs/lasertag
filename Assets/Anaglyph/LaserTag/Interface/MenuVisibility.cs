using System;
using System.Threading;
using Anaglyph.Netcode;
using Anaglyph.XR;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.Interface
{
	public class MenuVisibility : MonoBehaviour
	{
		[SerializeField] private InputAction showMenuAction;

		[SerializeField] private float verticalOffset;

		private Transform camTransform => MainXRRig.Camera.transform;

		private UIToolkitPanelXRSetup[] panels;
		private PanelArranger panelArranger;

		public bool IsVisible { get; private set; } = true;

		private void Awake()
		{
			panels = GetComponentsInChildren<UIToolkitPanelXRSetup>(true);
			panelArranger = GetComponent<PanelArranger>();

			showMenuAction.performed += delegate { SmartToggleVisible(); };

			showMenuAction.Enable();

			MainXRRig.Recentered += SetPose;
			NetcodeManagement.StateChanged += NetcodeStateChanged;
		}

		private void NetcodeStateChanged(NetcodeState state)
		{
			switch (state)
			{
				case NetcodeState.Connected:
					SetVisible(false);
					break;
				case NetcodeState.Disconnected:
					SetVisible(true);
					break;
			}
		}

		private void OnDestroy()
		{
			MainXRRig.Recentered -= SetPose;
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused) return;

			if (IsVisible)
				SetPose();
		}

		private async void Start()
		{
			await Awaitable.WaitForSecondsAsync(0.5f);
			SetPose();
		}

		private bool CheckIsInView()
		{
			Vector3 viewPos = MainXRRig.Camera.WorldToViewportPoint(transform.position + transform.forward);
			
			return viewPos.x is > 0f and < 1f && viewPos.y is > 0f and < 1f && viewPos.z > 0;
		}

		/// <summary>
		/// Toggles visibility *on screen*. If the menu is visible but *off-screen*
		/// the menu is repositioned on-screen rather than hidden.
		/// </summary>
		public void SmartToggleVisible()
		{
			bool isInView = CheckIsInView();

			if (!isInView && IsVisible)
				SetPose();
			else
				SetVisible(!IsVisible);
		}

		/// <summary>
		/// Hides the panels rather than deactivating them, so each menu keeps its
		/// visual tree, its navigation history and its typed-in state while closed.
		/// </summary>
		public void SetVisible(bool shouldBeVisible)
		{
			bool isInView = CheckIsInView();

			if (shouldBeVisible && (!IsVisible || !isInView)) SetPose();

			if (IsVisible == shouldBeVisible)
				return;

			IsVisible = shouldBeVisible;

			foreach (UIToolkitPanelXRSetup panel in panels)
				panel.SetVisible(shouldBeVisible);

			// re-enabling replays its fly-in transition
			panelArranger.enabled = shouldBeVisible;
		}

		private async void SetPose()
		{
			CancellationToken ctkn = destroyCancellationToken;

			try
			{
				await Awaitable.EndOfFrameAsync(ctkn);
				
				Vector3 camPos = camTransform.position;
				transform.position = camPos + Vector3.up * verticalOffset;

				Vector3 f = camTransform.forward;
				f = new Vector3(f.x, 0, f.z).normalized;

				transform.forward = f;

			}
			catch (OperationCanceledException)
			{
				
			}
		}
	}
}