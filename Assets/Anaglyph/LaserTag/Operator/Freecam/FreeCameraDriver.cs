using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace EnvisionCenter.Freecam
{
	[RequireComponent (typeof (Camera))]
	public class FreeCameraDriver : MonoBehaviour
	{
		[SerializeField] private new Camera camera;
		public float MaxFocusDistance = 20f;

		public float maxSafeDistanceFromOrigin = 100;

		public float orbitSpeed = 5;
		public float zoomStep = 0.1f;
		public float moveSpeed = 1;

		private Pose initialPose;

		private Vector3 focusPoint;
		private float focusZDist;

		[SerializeField] private InputActionAsset inputActions;
		private InputActionMap actionMap;
		private InputAction pan;
		private InputAction orbit;
		private InputAction zoom;

		private InputSystemUIInputModule inputModule;

		public bool IsMovingCamera { get; private set; }

		private void OnEnable()
		{
			actionMap.Enable();
		}

		private void OnDisable()
		{
			actionMap.Disable();
		}

		private void Awake()
		{
			actionMap = inputActions.FindActionMap("Camera");

			pan = actionMap.FindAction("Pan");
			orbit = actionMap.FindAction("Orbit");
			zoom = actionMap.FindAction("Zoom");

			initialPose = new(transform.position, transform.rotation);
			focusPoint = transform.position + transform.forward * 2;
			focusZDist = 2;

			inputModule = FindFirstObjectByType<InputSystemUIInputModule>();
		}

		private bool CheckPointerIsOverUI()
		{
			var lastInputModuleRaycast = inputModule.GetLastRaycastResult(0);
			return lastInputModuleRaycast.module && lastInputModuleRaycast.module.GetType() == typeof(GraphicRaycaster);
		}

		private Vector3 prevMousePos;
		private void Update()
		{
			if (CheckPointerIsOverUI()) return;

			Vector3 mousePos = Mouse.current.position.ReadValue();

			if (!camera.pixelRect.Contains(mousePos))
				return;

            Ray mouseRay = camera.ScreenPointToRay(mousePos);

			IsMovingCamera = pan.IsPressed() || orbit.IsPressed() || zoom.IsPressed();

			if (pan.WasPressedThisFrame() || orbit.WasPressedThisFrame() || zoom.WasPerformedThisFrame())
			{
				Ray ray = mouseRay;

				//if (Physics.Raycast(ray, out RaycastHit hit, MaxFocusDistance))
				//if (Physics.SphereCast(ray, sphereCastSize, out RaycastHit hit) && hit.distance > sphereCastSize)
				//	focusPoint = hit.point;
				if(Physics.Raycast(ray, out RaycastHit hit))
					focusPoint = hit.point;
				else
					focusPoint = transform.position + transform.forward * focusZDist;

				focusZDist = camera.WorldToScreenPoint(focusPoint).z;
			}

			if (orbit.IsPressed())
			{
				Vector2 dir = orbit.ReadValue<Vector2>() * orbitSpeed;

				Vector3 orbitPoint = focusPoint;

				transform.RotateAround(orbitPoint, Vector3.up, dir.x);
				transform.RotateAround(orbitPoint, transform.right, -dir.y);
			}
			
			if (pan.IsPressed())
			{
				Vector3 prevWorldPoint = camera.ScreenToWorldPoint(new Vector3(prevMousePos.x, prevMousePos.y, focusZDist));
				Vector3 newWorldPoint = camera.ScreenToWorldPoint(new Vector3(mousePos.x, mousePos.y, focusZDist));
				Vector3 disp = newWorldPoint - prevWorldPoint;

				camera.transform.position -= disp;
			}

			prevMousePos = mousePos;

			if (zoom.WasPerformedThisFrame())
			{
				float scroll = this.zoom.ReadValue<float>();

#if !UNITY_EDITOR && UNITY_WEBGL
				scroll /= 120f;
#endif

				Vector3 zoom = mouseRay.direction * scroll * zoomStep * Vector3.Distance(focusPoint, transform.position);
				camera.transform.Translate(zoom, Space.World);
			}

			if (transform.position.magnitude > maxSafeDistanceFromOrigin)
				transform.SetPositionAndRotation(initialPose.position, initialPose.rotation);
		}
	}
}
