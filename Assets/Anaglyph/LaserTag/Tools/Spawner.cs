using Anaglyph.XRTemplate;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.Lasertag
{
	public class Spawner : MonoBehaviour
	{
		[SerializeField] private float rotateSpeed;

		private GameObject objectToSpawn;
		private GameObject previewObject;
		private float spawnAngle;

		[SerializeField] private ObjectBoundsVisual objectBoundsVisual;

		private LineRenderer lineRenderer;
		private HandedHierarchy handedness;

		public static Spawner Left { get; private set; }
		public static Spawner Right { get; private set; }

		private void Awake()
		{
			handedness = GetComponentInParent<HandedHierarchy>(true);

			TryGetComponent(out lineRenderer);
			lineRenderer.useWorldSpace = false;
			lineRenderer.SetPositions(new[] { Vector3.zero, Vector3.zero });

			if (handedness.Handedness == InteractorHandedness.Left)
				Left = this;
			else if (handedness.Handedness == InteractorHandedness.Right)
				Right = this;
		}

		public void SetObjectToSpawn(GameObject objectToSpawn)
		{
			this.objectToSpawn = objectToSpawn;

			if(previewObject != null)
			{
				Destroy(previewObject);
			}

			previewObject = InstantiateObjectAsPreview(objectToSpawn);

			objectBoundsVisual.SetTrackedObject(previewObject);
			previewObject.SetActive(false);
		}

		private void Update()
		{
			if (previewObject == null) return;

			bool hoveringOverUI = handedness.RayInteractor.IsOverUIGameObject();
			previewObject.SetActive(!hoveringOverUI);
			if (hoveringOverUI) return;

			bool raycastDidHit = Raycast(out Vector3 pos);
			previewObject.SetActive(raycastDidHit);
			if (!raycastDidHit) return;

			lineRenderer.SetPosition(1, transform.InverseTransformPoint(pos));

			previewObject.transform.position = pos;
			previewObject.transform.rotation = Orient();
		}

		private void OnEnable()
		{
			Vector3 forw = transform.forward;
			forw.y = 0;
			spawnAngle = Vector3.SignedAngle(Vector3.forward, forw, Vector3.up);

			//spawnAngle = 0;
		}
		private void OnDisable()
		{
			if (previewObject != null)
				previewObject.SetActive(false);
		}

		private void OnFire(InputAction.CallbackContext context)
		{
			if (context.performed && context.ReadValueAsButton())
				Spawn();
		}

		private void OnAxis(InputAction.CallbackContext context)
		{
			Rotate(context.ReadValue<Vector2>().x);
		}

		public void Spawn()
		{
			if (handedness.RayInteractor.IsOverUIGameObject()) return;
			
			if (!Raycast(out Vector3 position)) return;

			Quaternion rotation = Orient();

			NetworkObject.InstantiateAndSpawn(objectToSpawn, NetworkManager.Singleton,
				position: position, rotation: rotation,
				ownerClientId: NetworkManager.Singleton.LocalClientId);
		}

		private void Rotate(float angle)
		{
			if (handedness.RayInteractor.IsOverUIGameObject()) return;

			spawnAngle += angle;
		}

		private bool Raycast(out Vector3 pos)
		{
			Ray ray = new Ray(transform.position, transform.forward);
			bool didHit = EnvironmentMapper.Raycast(ray, 50, out var result);
			pos = result.point;
			return didHit;
		}

		private Quaternion Orient()
		{
			return Quaternion.Euler(0, spawnAngle, 0);
		}

		private static readonly Type[] blacklistedPreviewComponents = {
			typeof(MonoBehaviour), typeof(Animator), typeof(Collider), typeof(Rigidbody)
		};

		private static readonly Type[] whiteListedPreviewComponents = {
			typeof(TeamColorer),
		};

		private static GameObject InstantiateObjectAsPreview(GameObject obj)
		{
			GameObject preview = Instantiate(obj);
			preview.SetActive(false);

			Component[] components = preview.GetComponentsInChildren<Component>(true);

			for (int i = 0; i < 100; i++)
			{
				// Because you can't force destroy components that others depend on >:(
				bool allBlacklistedDestroyed = true;

				foreach (var c in components)
				{
					Type componentType = c.GetType();

					if (c == null)
						continue;

					bool whiteListed = false;
					foreach (Type t in whiteListedPreviewComponents)
					{
						if (componentType == t || componentType.IsSubclassOf(t))
						{
							whiteListed = true;
							break;
						}
					}

					if (whiteListed)
						continue;

					foreach (Type t in blacklistedPreviewComponents)
					{
						if (componentType == t || componentType.IsSubclassOf(t))
						{
							DestroyImmediate(c, false);
							if (c != null)
								allBlacklistedDestroyed = false;
							break;
						}
					}
				}

				if (allBlacklistedDestroyed)
					break;
			}

			preview.SetActive(true);

			return preview;
		}
	}
}