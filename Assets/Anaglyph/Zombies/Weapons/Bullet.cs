using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Zombies
{
	public class Bullet : MonoBehaviour
	{
		private const float MaxTravelDist = 50;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private AnimationCurve damageOverDistance = AnimationCurve.Constant(0, MaxTravelDist, 50f);

		[SerializeField] private int despawnDelay = 1;

		[SerializeField] private AudioClip fireSFX;
		[SerializeField] private AudioClip collideSFX;

		private Ray fireRay;
		private bool isAlive;
		private float spawnedTime;
		private float envHitDist;
		private float travelDist;

		public UnityEvent OnFire = new();
		public UnityEvent OnCollide = new();

		private void Start()
		{
			envHitDist = MaxTravelDist;
			isAlive = true;
			spawnedTime = Time.time;

			OnFire.Invoke();
			AudioSource.PlayClipAtPoint(fireSFX, transform.position);

			fireRay = new(transform.position, transform.forward);
		}

		private void Update()
		{
			if (isAlive)
			{
				float lifeTime = Time.time - spawnedTime;
				Vector3 prevPos = transform.position;
				travelDist = metersPerSecond * lifeTime;

				transform.position = fireRay.GetPoint(travelDist);

				bool didHitEnv = travelDist > envHitDist;

				if (didHitEnv)
					transform.position = fireRay.GetPoint(envHitDist);

				bool didHitPhys = Physics.Linecast(prevPos, transform.position, out var physHit,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

				if (didHitPhys)
				{
					Hit(physHit.point, physHit.normal);

					var col = physHit.collider;

					float damage = damageOverDistance.Evaluate(travelDist);
					physHit.collider.gameObject.BroadcastMessage("BulletHit", damage, SendMessageOptions.DontRequireReceiver);
				}
			}
		}

		private void Hit(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isAlive = false;

			OnCollide.Invoke();
			AudioSource.PlayClipAtPoint(collideSFX, transform.position);

			StartCoroutine(D());
			IEnumerator D()
			{
				yield return new WaitForSeconds(despawnDelay);
				Destroy(gameObject);
			}
		}
	}
}