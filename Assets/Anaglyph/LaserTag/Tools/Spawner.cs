using Anaglyph.XRTemplate;
using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.Lasertag
{
	public class Spawner : MonoBehaviour
	{
		[SerializeField] private float rotateSpeed;

		private GameObject objectToSpawn;
		private GameObject previewObject;
		private float rotating;
		private float angle;

		[SerializeField] private BoundsMesh boundsVisual;
		[SerializeField] private LineRenderer lineRenderer;

		private HandedHierarchy hand;

		public static Spawner Left { get; private set; }
		public static Spawner Right { get; private set; }

		private void Awake()
		{
			hand = GetComponentInParent<HandedHierarchy>(true);

			if (hand.Handedness == InteractorHandedness.Left)
				Left = this;
			else if (hand.Handedness == InteractorHandedness.Right)
				Right = this;

			lineRenderer.useWorldSpace = false;
			lineRenderer.SetPositions(new[] { Vector3.zero, Vector3.zero });
		}

		public void SetObjectToSpawn(GameObject objectToSpawn)
		{
			this.objectToSpawn = objectToSpawn;

			if (previewObject != null)
				Destroy(previewObject);

			previewObject = InstantiateObjectAsPreview(objectToSpawn);

			boundsVisual.SetTrackedObject(previewObject);
		}

		private async void LateUpdate()
		{
			angle += rotating * Time.deltaTime * rotateSpeed;

			if (previewObject == null)
			{
				lineRenderer.enabled = false;
				boundsVisual.enabled = false;
				lineRenderer.SetPosition(1, Vector3.forward);
				return;
			}

			Ray ray = new(transform.position, transform.forward);
			var result = await EnvironmentMapper.Instance.RaymarchAsync(ray, 50);

			if (!enabled)
				return;

			previewObject.SetActive(result.DidHit);
			boundsVisual.enabled = result.DidHit;

			if (result.DidHit)
			{
				lineRenderer.SetPosition(1, Vector3.forward * result.Distance);

				previewObject.transform.position = result.Point;
				previewObject.transform.eulerAngles = new Vector3(0, angle, 0);
			}
			else
			{
				lineRenderer.SetPosition(1, Vector3.forward);
			}
		}

		private void OnEnable()
		{
			lineRenderer.enabled = true;
			var forw = transform.forward;
			forw.y = 0;
			angle = Vector3.SignedAngle(Vector3.forward, forw, Vector3.up);
		}

		private void OnDisable()
		{
			if (previewObject != null)
				previewObject.SetActive(false);

			lineRenderer.enabled = false;
			boundsVisual.enabled = false;
		}

		private void OnFire(InputAction.CallbackContext context)
		{
			if (!enabled)
				return;

			if (context.performed && context.ReadValueAsButton())
			{
				var position = previewObject.transform.position;
				var rotation = previewObject.transform.rotation;

				NetworkObject.InstantiateAndSpawn(objectToSpawn, NetworkManager.Singleton,
					position: position, rotation: rotation,
					ownerClientId: NetworkManager.Singleton.LocalClientId);
			}
		}

		private void OnAxis(InputAction.CallbackContext context)
		{
			rotating = -context.ReadValue<Vector2>().x;
		}


		private static readonly Type[] blacklistedPreviewComponents =
		{
			typeof(MonoBehaviour), typeof(Animator), typeof(Collider), typeof(Rigidbody), typeof(NavMeshAgent)
		};

		private static readonly Type[] whiteListedPreviewComponents =
		{
			typeof(TeamColorer), typeof(TeamOwner)
		};

		private static GameObject InstantiateObjectAsPreview(GameObject obj)
		{
			var preview = Instantiate(obj);
			preview.SetActive(false);

			var components = preview.GetComponentsInChildren<Component>(true);

			for (var i = 0; i < 100; i++)
			{
				// Because you can't force destroy components that others depend on >:(
				var allBlacklistedDestroyed = true;

				foreach (var c in components)
				{
					var componentType = c.GetType();

					if (c == null)
						continue;

					var whiteListed = false;
					foreach (var t in whiteListedPreviewComponents)
						if (componentType == t || componentType.IsSubclassOf(t))
						{
							whiteListed = true;
							break;
						}

					if (whiteListed)
						continue;

					foreach (var t in blacklistedPreviewComponents)
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
	}
}