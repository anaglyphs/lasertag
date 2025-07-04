using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Menu
{
	public class MenuPositioner : MonoBehaviour
	{
		[SerializeField] private InputAction showMenuAction;
		[SerializeField] private MonoBehaviour[] componentsDisabledWhileVisible;
		[SerializeField] private GameObject[] objectsInactiveWhileVisible;

		private Camera mainCamera;
		private Transform camTransform => mainCamera.transform;

		[SerializeField] private Vector3 offset;

		private void Awake()
		{
			showMenuAction.performed += delegate
			{
				ToggleVisible();
			};

			showMenuAction.Enable();
		}

		private void OnEnable()
		{
			mainCamera = Camera.main;
		}

		private void Start()
		{
			StartCoroutine(LateSetPoseOnStart());
		}

		private IEnumerator LateSetPoseOnStart()
		{
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			SetPose();
		}

		public void ToggleVisible()
		{
			Vector3 viewPos = mainCamera.WorldToViewportPoint(transform.position);

			bool isInView = viewPos.x > 0f && viewPos.x < 1f && viewPos.y > 0f && viewPos.y < 1f && viewPos.z > 0;

			if (!isInView && gameObject.activeSelf)
				SetPose();
			else
				SetVisible(!gameObject.activeSelf);
		}

		public void SetVisible(bool b)
		{
			if (gameObject.activeSelf == b)
				return;

			gameObject.SetActive(b);

			if (b)
			{
				SetPose();
			}

			foreach (MonoBehaviour mb in componentsDisabledWhileVisible)
			{
				mb.enabled = !b;
			}

			foreach (GameObject go in objectsInactiveWhileVisible)
			{
				go.SetActive(!b);
			}
		}

		private void SetPose()
		{
			Vector3 flatForward = new Vector3(camTransform.forward.x, 0, camTransform.forward.z).normalized;

			Matrix4x4 pose = Matrix4x4.LookAt(camTransform.position, camTransform.position + flatForward, Vector3.up);

			transform.position = pose.MultiplyPoint(offset);

			Vector3 forward = (transform.position - camTransform.position).normalized;

			transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
		}

		private void OnApplicationFocus(bool focus)
		{
			if (focus)
				SetPose();
		}

		//#if UNITY_EDITOR
		//		private void OnDrawGizmosSelected()
		//		{
		//			if (mainCamera != null)
		//			{
		//				Gizmos.color = Color.green;
		//				Gizmos.DrawSphere(camTransform.position, 0.1f);
		//			}
		//		}

		//		private void OnValidate()
		//		{
		//			mainCamera = Camera.main;

		//			if (mainCamera != null)
		//			{
		//				SetPose();
		//			}
		//		}
		//#endif
	}
}