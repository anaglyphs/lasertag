using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.XRTemplate
{
	public class MenuPositioner : MonoBehaviour
	{
		[SerializeField] private InputAction showMenuAction;
		[SerializeField] private MonoBehaviour[] componentsDisabledWhileVisible;
		[SerializeField] private GameObject[] objectsInactiveWhileVisible;

		[SerializeField] private Vector3 offset;
		[SerializeField] private float hideDistance = 5;

		private Transform Head => MainXRRig.Camera.transform;
		private Camera HeadCamera => MainXRRig.Camera;

		private void Awake()
		{
			showMenuAction.performed += delegate { ToggleVisible(); };

			showMenuAction.Enable();

			MainXRRig.Recentered += SetPose;
		}

		private void OnDestroy()
		{
			MainXRRig.Recentered -= SetPose;
		}
		
		private async void OnApplicationFocus(bool focus)
		{
			if (!focus) return;
			
			await Awaitable.NextFrameAsync();
			SetPose();
		}

		private async void Start()
		{
			await Awaitable.NextFrameAsync();
			SetPose();
		}

		private void Update()
		{
			float dist = Vector3.Distance(Head.position, transform.position);

			if (dist > hideDistance)
				SetVisible(false);
		}

		public void ToggleVisible()
		{
			Vector3 viewPos = HeadCamera.WorldToViewportPoint(transform.position);

			bool isInView = viewPos.x is > 0f and < 1f && viewPos.y is > 0f and < 1f && viewPos.z > 0;

			if (!isInView && gameObject.activeSelf)
				SetPose();
			else
				SetVisible(!gameObject.activeSelf);
		}

		public void SetVisible(bool visible)
		{
			if (gameObject.activeSelf == visible)
				return;

			gameObject.SetActive(visible);

			if (visible) SetPose();

			foreach (MonoBehaviour mb in componentsDisabledWhileVisible) mb.enabled = !visible;

			foreach (GameObject go in objectsInactiveWhileVisible) go.SetActive(!visible);
		}

		private async void SetPose()
		{
			await Awaitable.EndOfFrameAsync();
			
			Vector3 flatForward = new Vector3(Head.forward.x, 0, Head.forward.z).normalized;

			Matrix4x4 pose = Matrix4x4.LookAt(Head.position, Head.position + flatForward, Vector3.up);

			transform.position = pose.MultiplyPoint(offset);

			Vector3 forward = (transform.position - Head.position).normalized;

			transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
		}
	}
}