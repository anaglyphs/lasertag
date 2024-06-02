using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.XRTemplate
{
	public class Raycaster : MonoBehaviour
	{
		public float forwardRayOffset = 0.05f;
		public bool shouldHitTriggers;
		public bool shouldTestIfStartsInsideCollider = true;
		public LayerMask shouldHitLayers = Physics.DefaultRaycastLayers;

		private Collider[] overlapSphereBuffer = new Collider[10];

		private bool didHit;
		private GameObject hitObject;
		private Vector3 hitPoint;

		private HandSide handSide;

		public bool DidHit { get; private set; }
		public bool DidHitPrevious { get; private set; }

		public bool raycastOnLateUpdate = false;

		public GameObject HitObject { get; private set; }
		public GameObject PreviousHitObject { get; private set; }

		public Vector3 HitPoint { get; private set; }

		public UnityEvent<GameObject> onHitChange;
		public UnityEvent<GameObject> onEnterObject;
		public UnityEvent<GameObject> onLeaveObject;
		public UnityEvent onCast;
		public UnityEvent<Vector3> onHitPoint;
		public UnityEvent<bool> onDidHitChange;

		private void Awake()
		{
			onHitChange.Invoke(null);
			onDidHitChange.Invoke(false);

			handSide = GetComponentInParent<HandSide>();

		}

		public bool Raycast()
		{
			if (handSide != null && handSide.rayInteractor.IsOverUIGameObject())
			{
				ForceDidNotHit();
				return false;
			}

			onCast.Invoke();

			QueryTriggerInteraction interaction =
				shouldHitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore;

			Vector3 origin = transform.position + transform.forward * forwardRayOffset;

			// test origin overlap

			if (shouldTestIfStartsInsideCollider)
			{

				int hits = Physics.OverlapSphereNonAlloc(origin, 0.01f, overlapSphereBuffer, shouldHitLayers, interaction);
				didHit = hits > 0;

				if (didHit)
				{
					// choose closest

					float maxDist = Mathf.Infinity;
					Collider closest = overlapSphereBuffer[0];
					for (int i = 1; i < hits; i++)
					{
						float sqrDist = (overlapSphereBuffer[i].transform.position - origin).sqrMagnitude;
						if (sqrDist < maxDist)
						{
							maxDist = sqrDist;
							closest = overlapSphereBuffer[i];
						}
					}

					hitObject = closest.gameObject;
					hitPoint = origin;

					UpdatePropertiesAndEvents();
					return didHit;
				}
			}

			// raycast

			Ray ray = new(origin, transform.forward);

			didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, shouldHitLayers, interaction);

			if (didHit)
			{
				hitObject = hit.collider.gameObject;
				hitPoint = hit.point;

				UpdatePropertiesAndEvents();
				return didHit;
			}

			// didn't hit anything 

			hitObject = null;
			hitPoint = origin;

			UpdatePropertiesAndEvents();
			return didHit;
		}

		private void OnEnable()
		{
			onDidHitChange.Invoke(false);

		}

		private void OnDisable()
		{
			ForceDidNotHit();
		}

		private void ForceDidNotHit()
		{
			didHit = false;
			hitObject = null;
			hitPoint = transform.position;

			UpdatePropertiesAndEvents();
		}

		private void UpdatePropertiesAndEvents()
		{
			DidHitPrevious = DidHit;
			DidHit = didHit;

			PreviousHitObject = HitObject;
			HitObject = hitObject;

			HitPoint = hitPoint;

			if (DidHit)
				onHitPoint.Invoke(hitPoint);

			if (HitObject != PreviousHitObject)
			{
				onHitChange.Invoke(HitObject);

				if (HitObject != null)
					onEnterObject.Invoke(HitObject);

				if (PreviousHitObject != null)
					onLeaveObject.Invoke(PreviousHitObject);
			}

			if (DidHit != DidHitPrevious)
				onDidHitChange.Invoke(DidHit);
		}

		private void LateUpdate()
		{
			if (raycastOnLateUpdate)
				Raycast();
		}
	}
}
