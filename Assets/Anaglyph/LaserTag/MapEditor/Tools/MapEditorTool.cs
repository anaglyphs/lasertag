using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	public class MapEditorTool : MonoBehaviour
	{
		[SerializeField] private Color placeColor;
		[SerializeField] private Color deleteColor;
		[SerializeField] private Color moveColor;

		[SerializeField] private float rotationSpeed;

		private MapObject currentSpawnObject;
		private GameObject previewObject;
		private float spawnRotation;

		private MapObject grabbedObject;
		private Vector3 grabLocalPosition;
		private float grabDistance;
		private float grabRotation;

		private float rotationDelta;

		[SerializeField] private LineRenderer lineRenderer;

		private void Awake()
		{
			lineRenderer.useWorldSpace = false;
		}

		public void SetSpawnObject(MapObject spawnObject)
		{
			if (Equals(spawnObject, currentSpawnObject))
				return;

			currentSpawnObject = spawnObject;

			if (previewObject)
				Destroy(previewObject);

			if (spawnObject) previewObject = InstantiateObjectAsPreview(spawnObject.gameObject);
		}

		private Quaternion GetSpawnRotation()
		{
			return Quaternion.Euler(0, spawnRotation, 0);
		}

		private void UpdatePreviewObjectVisibility()
		{
			previewObject.SetActive(enabled && grabbedObject == null);
		}

		private void OnEnable()
		{
			UpdatePreviewObjectVisibility();
		}

		private void OnDisable()
		{
			UpdatePreviewObjectVisibility();
		}

		public bool TryDelete()
		{
			if (!Raycast(out GameObject hitObj))
				return false;

			MapObject mapObj = hitObj.GetComponentInParent<MapObject>();
			mapObj?.Delete();

			return mapObj != null;
		}

		public bool TryPlace()
		{
			if (!currentSpawnObject)
				return false;

			if (!Raycast(out Vector3 hitPos))
				return false;

			SpawnMapObject(currentSpawnObject, hitPos, GetSpawnRotation());

			return true;
		}

		public bool TryGrab()
		{
			if (!Raycast(out RaycastHit hit)) return false;

			grabbedObject = hit.collider.GetComponentInParent<MapObject>();
			if (grabbedObject == null) return false;

			grabLocalPosition = grabbedObject.transform.InverseTransformPoint(hit.point);
			grabDistance = hit.distance;
			grabRotation = grabbedObject.transform.eulerAngles.y;

			UpdatePreviewObjectVisibility();

			return true;
		}

		public void TryLetGo()
		{
			grabbedObject = null;
			UpdatePreviewObjectVisibility();
		}

		private void LateUpdate()
		{
			Raycast(out RaycastHit hit);

			float lineDist = hit.distance;

			if (grabbedObject != null)
			{
				Vector3 targetWorldPos = transform.position + transform.forward * grabDistance;
				Vector3 currentGrabWorldPos = grabbedObject.transform.TransformPoint(grabLocalPosition);
				grabbedObject.transform.position += targetWorldPos - currentGrabWorldPos;

				grabRotation += rotationDelta * Time.deltaTime * rotationSpeed;

				grabbedObject.transform.rotation = Quaternion.Euler(0, grabRotation, 0);

				lineDist = grabDistance;
			}
			else
			{
				if (previewObject != null)
					previewObject.transform.position = hit.point;
			}

			lineRenderer.SetPosition(1, Vector3.forward * lineDist);
		}

		#region Input

		private void OnAxis(InputAction.CallbackContext context)
		{
			rotationDelta = -context.ReadValue<Vector2>().x;
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

		public static GameObject SpawnMapObject(MapObject prefab, Vector3 position,
			Quaternion rotation = default)
		{
			NetworkObject netObj = NetworkObject.InstantiateAndSpawn(prefab.gameObject, NetworkManager.Singleton,
				NetworkManager.Singleton.LocalClientId, false, false, false, position,
				rotation);

			return netObj.gameObject;
		}

		private static GameObject InstantiateObjectAsPreview(GameObject obj)
		{
			GameObject preview = Instantiate(obj);
			preview.SetActive(false);

			Component[] components = preview.GetComponentsInChildren<Component>(true);

			for (int i = 0; i < 100; i++)
			{
				// Because you can't force destroy components that others depend on >:(
				bool allBlacklistedDestroyed = true;

				foreach (Component c in components)
				{
					Type componentType = c.GetType();

					if (c == null)
						continue;

					bool whiteListed = false;
					foreach (Type t in whiteListedPreviewComponents)
						if (componentType == t || componentType.IsSubclassOf(t))
						{
							whiteListed = true;
							break;
						}

					if (whiteListed)
						continue;

					foreach (Type t in blacklistedPreviewComponents)
						if (componentType == t || componentType.IsSubclassOf(t))
						{
							DestroyImmediate(c, false);
							if (c != null)
								allBlacklistedDestroyed = false;
							break;
						}
				}

				if (allBlacklistedDestroyed)
					break;
			}

			preview.SetActive(true);

			return preview;
		}

		#endregion
	}
}