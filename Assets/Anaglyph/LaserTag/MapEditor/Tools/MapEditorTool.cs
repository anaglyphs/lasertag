using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.MapEditor.Tools
{
	
	public class MapEditorTool : MonoBehaviour
	{
		public static MapEditorTool DominantHand;

		[SerializeField] private Color placeColor = Color.green;
		[SerializeField] private Color deleteColor = Color.red;
		[SerializeField] private Color moveColor = Color.white;

		[SerializeField] private string rotateInputBinding = "OnAxis";
		[SerializeField] private string moveInputBinding = "OnGrip";
		[SerializeField] private string deleteInputBinding = "OnBack";
		[SerializeField] private string placeInputBinding = "OnFire";

		[SerializeField] private float rotationSpeed;
		[SerializeField] private float distanceSpeed;

		private MapObject currentSpawnObject;
		private GameObject previewObject;
		private float spawnRotation;

		private MapObject grabbedObject;
		private Vector3 grabOffset;
		private float grabDistance;
		private float grabRotation;

		private float rotationDelta;
		private float distanceDelta;

		[SerializeField] private HandSubject handSubject;

		[SerializeField] private LineRenderer lineRenderer;

		private void Awake()
		{
			MapEditor.ActiveChanged += gameObject.SetActive;
			gameObject.SetActive(MapEditor.IsActive);

			lineRenderer.useWorldSpace = false;
		}

		private void Start()
		{
			if (!handSubject)
				TryGetComponent(out handSubject);

			if (handSubject.Current.Handedness == Handedness.Right)
				DominantHand = this;

			handSubject.Bind(rotateInputBinding, OnRotateInput);
			handSubject.Bind(placeInputBinding, OnPlaceInput);
			handSubject.Bind(deleteInputBinding, OnDeleteInput);
			handSubject.Bind(moveInputBinding, OnMoveInput);
		}

		private void OnDestroy()
		{
			MapEditor.ActiveChanged -= gameObject.SetActive;
		}

		public void SetSpawnObject(MapObject spawnObject)
		{
			if (Equals(spawnObject, currentSpawnObject))
				return;

			currentSpawnObject = spawnObject;

			if (previewObject)
				Destroy(previewObject);

			if (spawnObject != null) previewObject = InstantiateObjectAsPreview(spawnObject);
		}

		private Quaternion GetSpawnRotation()
		{
			return Quaternion.Euler(0, spawnRotation, 0);
		}

		private void OnDisable()
		{
			if (previewObject != null)
				previewObject.SetActive(false);
		}

		public bool TryDelete()
		{
			if (!Raycast(out GameObject hitObj))
				return false;

			MapObject mapObj = hitObj.GetComponentInParent<MapObject>();
			if (mapObj == null || !mapObj.TryDelete())
				return false;

			MapObject.NotifyLocalEdit();
			return true;
		}

		public bool TryPlace()
		{
			if (!CheckCanPlace())
				return false;

			if (!Raycast(out Vector3 hitPos))
				return false;

			if (!SpawnMapObject(currentSpawnObject, hitPos, GetSpawnRotation()))
				return false;

			MapObject.NotifyLocalEdit();

			return true;
		}

		public bool TryGrab()
		{
			// Moving an object records a new world pose, so it is held to the same rule as
			// placing one.
			if (MapManager.Instance != null && !MapManager.Instance.CheckCanEditMap())
				return false;

			if (!Raycast(out RaycastHit hit)) return false;

			grabbedObject = hit.collider.GetComponentInParent<MapObject>();
			if (grabbedObject == null || !grabbedObject.Movable) return false;

			grabbedObject.TryTakeOwnership();

			grabOffset = hit.point - grabbedObject.transform.position;
			grabDistance = hit.distance;
			grabRotation = grabbedObject.transform.eulerAngles.y;

			return true;
		}

		public void TryLetGo()
		{
			if (grabbedObject != null)
			{
				grabbedObject.ReleaseOwnership();
				MapObject.NotifyLocalEdit();
			}

			grabbedObject = null;
		}

		// Also drives the placement preview, so an edit the map manager would refuse shows as
		// nothing to place rather than as a press that does nothing.
		private bool CheckCanPlace()
		{
			return currentSpawnObject != null && !handSubject.Current.InputBlocked &&
				this == DominantHand &&
				(MapManager.Instance == null || MapManager.Instance.CheckCanEditMap());
		}

		private void LateUpdate()
		{
			bool didHit = Raycast(out RaycastHit hit);

			float lineDist = 1;
			if (didHit) lineDist = hit.distance;

			if (grabbedObject != null)
			{
				if (previewObject != null)
					previewObject.SetActive(false);

				if (grabbedObject.CanManage())
				{
					Vector3 targetWorldPos = transform.position + transform.forward * grabDistance;
					Vector3 currentGrabWorldPos = grabbedObject.transform.position + grabOffset;
					grabbedObject.transform.position += targetWorldPos - currentGrabWorldPos;

					grabDistance += distanceDelta * Time.deltaTime * distanceSpeed;
					grabRotation += rotationDelta * Time.deltaTime * rotationSpeed;

					grabbedObject.transform.rotation = Quaternion.Euler(0, grabRotation, 0);

					lineDist = grabDistance;
				}
			}
			else
			{
				if (previewObject != null)
				{
					spawnRotation += rotationDelta * Time.deltaTime * rotationSpeed;

					previewObject.transform.position = hit.point;
					previewObject.transform.rotation = GetSpawnRotation();
					previewObject.SetActive(didHit && CheckCanPlace());
				}
			}

			lineRenderer.enabled = !handSubject.Current.InputBlocked;
			lineRenderer.SetPosition(1, Vector3.forward * lineDist);
			lineRenderer.SetPosition(0, Vector3.zero);
		}

		#region Input

		public void OnPlaceInput(InputAction.CallbackContext context)
		{
			if (!context.performed) return;

			if (this == DominantHand)
			{
				if (grabbedObject != null) return;

				TryPlace();
			}
			else
			{
				Handedness otherHand =
					handSubject.Current.Handedness == Handedness.Left ? Handedness.Right : Handedness.Left;

				DominantHand = this;
				Palette.Instance?.SetHandSide(otherHand);
			}
		}

		public void OnMoveInput(InputAction.CallbackContext context)
		{
			if (context.performed)
				TryGrab();
			else if (context.canceled && grabbedObject != null)
				TryLetGo();
		}

		public void OnDeleteInput(InputAction.CallbackContext context)
		{
			if (context.performed)
				TryDelete();
		}

		private void OnRotateInput(InputAction.CallbackContext context)
		{
			Vector2 axis = context.ReadValue<Vector2>();

			rotationDelta = -axis.x;
			distanceDelta = axis.y;

			if (Mathf.Abs(axis.x) > Mathf.Abs(axis.y))
				distanceDelta = 0;
			else
				rotationDelta = 0;
		}

		#endregion


		#region Helpers

		private bool Raycast(out RaycastHit hit)
		{
			Ray ray = new(transform.position, transform.forward);

			return Physics.Raycast(ray, out hit);
		}

		private bool Raycast(out GameObject hitObject)
		{
			bool didHit = Raycast(out RaycastHit hit);

			hitObject = hit.collider?.gameObject;

			return didHit;
		}

		private bool Raycast(out Vector3 hitPos)
		{
			bool didHit = Raycast(out RaycastHit hit);

			hitPos = hit.point;

			return didHit;
		}

		private static readonly Type[] blacklistedPreviewComponents =
		{
			typeof(MonoBehaviour), typeof(Animator), typeof(Collider), typeof(Rigidbody)
		};

		private static readonly Type[] whiteListedPreviewComponents =
		{
			typeof(TeamColorer), typeof(TeamOwner)
		};

		public static bool SpawnMapObject(MapObject prefab, Vector3 position,
			Quaternion rotation = default)
		{
			return MapManager.Instance != null &&
				MapManager.Instance.RequestPlaceObject(prefab, position, rotation);
		}

		private static GameObject InstantiateObjectAsPreview(MapObject obj)
		{
			GameObject preview = Instantiate(obj.Visuals);
			return preview;
		}

		#endregion
	}
}