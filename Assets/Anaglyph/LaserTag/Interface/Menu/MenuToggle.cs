using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Menu
{
	public class MenuToggle : MonoBehaviour
	{
		[SerializeField] private InputAction showMenuAction;
		[SerializeField] private MonoBehaviour[] componentsDisabledWhileVisible;
		[SerializeField] private GameObject[] objectsInactiveWhileVisible;

		[SerializeField] private float verticalOffset;

		private Camera mainCamera;
		private Transform camTransform => mainCamera.transform;

		private void Awake()
		{
			showMenuAction.performed += delegate { ToggleVisible(); };

			showMenuAction.Enable();
		}

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private async void Start()
		{
			await Awaitable.WaitForSecondsAsync(0.5f);
			SetPose();
		}

		public void ToggleVisible()
		{
			SetVisible(!gameObject.activeSelf);

			if (gameObject.activeSelf)
				SetPose();
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

		private void SetPose()
		{
			Vector3 camPos = camTransform.position;
			transform.position = camPos + Vector3.up * verticalOffset;

			Vector3 f = camTransform.forward;
			f = new Vector3(f.x, 0, f.z).normalized;

			transform.forward = f;
		}
	}
}