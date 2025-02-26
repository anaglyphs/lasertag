using System;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class Raycaster : MonoBehaviour
	{
		public bool triggers;
		public bool testOrigin = true;
		public LayerMask layers = Physics.DefaultRaycastLayers;

		private Collider[] overlapBuffer = new Collider[10];

		private GameObject hitObject;
		public GameObject HitObject => hitObject;

		private bool didHit;
		public bool DidHit => didHit;

		private RaycastHit result;
		public RaycastHit Result => result;


		private HandedHierarchy handed;
		

		/// new hit, prev hit
		public event Action<GameObject, GameObject> OnHitObjectChange = delegate { };

		private void Awake()
		{
			handed = GetComponentInParent<HandedHierarchy>();
		}

		public bool Raycast()
		{
			if (handed.RayInteractor.IsOverUIGameObject())
			{
				SetHitObject(null);
				return false;
			}

			QueryTriggerInteraction interaction =
				triggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

			Ray ray = new(transform.position, transform.forward);

			// test origin overlap

			if (testOrigin)
			{
				int hits = Physics.OverlapSphereNonAlloc(ray.origin, 0.01f, overlapBuffer, layers, interaction);
				didHit = hits > 0;

				if (didHit)
				{
					// choose closest

					float maxDist = Mathf.Infinity;
					Collider closest = overlapBuffer[0];
					for (int i = 1; i < hits; i++)
					{
						Vector3 colPos = overlapBuffer[i].transform.position;
						float sqrDist = (colPos - ray.origin).sqrMagnitude;
						if (sqrDist < maxDist)
						{
							maxDist = sqrDist;
							closest = overlapBuffer[i];
						}
					}

					SetHitObject(hitObject);

					result = new();
					result.point = ray.origin;
					result.normal = Vector3.zero;
					result.distance = 0;

					return didHit;
				}
			}

			// raycast

			didHit = Physics.Raycast(ray, out result, Mathf.Infinity, layers, interaction);

			if (didHit)
			{
				SetHitObject(result.collider.gameObject);
				return didHit;
			}

			// didn't hit anything 

			SetHitObject(null);
			return didHit;
		}

		private void SetHitObject(GameObject newObj)
		{
			if(newObj != hitObject)
			{
				GameObject prev = hitObject;
				hitObject = newObj;
				OnHitObjectChange?.Invoke(hitObject, prev);
			}
		}
	}
}
