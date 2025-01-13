using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.DepthKit;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Spawner : SuperAwakeBehavior
	{
		[SerializeField] private HandedControllerInput input = null;
		[SerializeField] private ObjectBoundsVisual objectBoundsVisual = null;
		[SerializeField] private LineRendererLaser pointerLaser = null;
		[SerializeField] private float rotateSpeed;
		[SerializeField] private float floorOffset = 0.05f;
		[SerializeField] private Vector3 rayOriginOffset = new(0, 0, 0.1f);
		[SerializeField] private float objectVerticalOffset = 0.01f;

		private GameObject objectToSpawn;
		private GameObject previewObject;
		private float spawnAngle;
		private HandSide handSide;

		protected override void SuperAwake()
		{
			handSide = GetComponentInParent<HandSide>(true);
			pointerLaser.startOffset = rayOriginOffset;
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

			bool hoveringOverUI = handSide.rayInteractor.IsOverUIGameObject();

			previewObject.SetActive(!hoveringOverUI);

			if (hoveringOverUI) return;

			bool hit = Raycast(out Vector3 pos);
			if (!hit) return;

			pointerLaser.SetEndPositionForFrame(pos);

			previewObject.transform.position = pos;
			previewObject.transform.rotation = Orient();

			Rotate(input.JoystickVector.x * rotateSpeed * Time.deltaTime);

			if (!input.TriggerWasDown && input.TriggerIsDown)
			{
				Spawn();
			}
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

		private void Spawn()
		{
			bool hit = Raycast(out Vector3 position);
			if (!hit) return;

			Quaternion rotation = Orient();

			NetworkObject.InstantiateAndSpawn(objectToSpawn, NetworkManager.Singleton,
				position: position, rotation: rotation,
				ownerClientId: NetworkManager.Singleton.LocalClientId);
		}

		private void Rotate(float angle)
		{
			spawnAngle += angle;
		}

		private bool Raycast(out Vector3 pos)
		{
			Ray ray = new Ray(transform.TransformPoint(rayOriginOffset), transform.forward);
			//bool hit = DepthCast.Raycast(ray, out var result, handRejection: true);
			bool didHit = EnvironmentMap.Raycast(ray, out Vector3 hitPoint);
			pos = hitPoint + Vector3.up * objectVerticalOffset;
			return didHit;
		}

		//private Vector3 FloorCast(float floorY)
		//{
		//	Vector3 pos = transform.position - new Vector3(0, floorY, 0);
		//	Vector3 forw = transform.forward;

		//	if (forw.y == 0)
		//	{
		//		return new Vector3(pos.x, floorY, pos.z);
		//	}

		//	Vector2 slope = new Vector2(forw.x, forw.z) / forw.y;

		//	return new Vector3(slope.x * -pos.y + pos.x, floorY, slope.y * -pos.y + pos.z);
		//}

		private Quaternion Orient()
		{
			//Vector3 forw = transform.forward;

			//if (forw.y == 1)
			//	return Quaternion.identity;

			//forw.y = 0;
			//Quaternion rot = Quaternion.LookRotation(forw, Vector3.up);
			//rot *= Quaternion.Euler(0, spawnAngle, 0);
			//return rot;

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