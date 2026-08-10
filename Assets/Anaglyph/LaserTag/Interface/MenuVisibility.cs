using System;
using System.Threading;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag.Interface
{
	public class MenuVisibility : MonoBehaviour
	{
		[SerializeField] private InputAction showMenuAction;
		[SerializeField] private MonoBehaviour[] componentsDisabledWhileVisible;
		[SerializeField] private GameObject[] objectsInactiveWhileVisible;

		[SerializeField] private float verticalOffset;

		private Camera mainCamera;
		private Transform camTransform => mainCamera.transform;
		private bool IsActive => gameObject.activeSelf;

		private void Awake()
		{
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

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused) return;
			
			if (gameObject.activeInHierarchy)
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
		/// Toggles visibility *on screen*, not active state. If the menu is enabled but *off-screen*
		/// the menu is repositioned on-screen rather than disabled. 
		/// </summary>
		public void SmartToggleVisible()
		{
			bool isInView = CheckIsInView();
			
			if (!isInView && gameObject.activeSelf)
				SetPose();
			else
				SetVisible(!gameObject.activeSelf);
		}

		public void SetVisible(bool shouldBeVisible)
		{
			bool isInView = CheckIsInView();
			
			if (shouldBeVisible && (!IsActive || !isInView)) SetPose();
			
			if (IsActive == shouldBeVisible)
				return;
			
			gameObject.SetActive(shouldBeVisible);

			foreach (MonoBehaviour mb in componentsDisabledWhileVisible) mb.enabled = !shouldBeVisible;

			foreach (GameObject go in objectsInactiveWhileVisible) go.SetActive(!shouldBeVisible);
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