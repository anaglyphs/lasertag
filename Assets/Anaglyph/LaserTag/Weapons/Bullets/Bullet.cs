using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.Lasertag
{
	public class Bullet : NetworkBehaviour
	{
		private const float MaxTravelDist = 50;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private AnimationCurve damageOverDistance = AnimationCurve.Constant(0, MaxTravelDist, 50f);

		[SerializeField] private int despawnDelay = 1;

		[SerializeField] private AudioClip fireSFX;
		[SerializeField] private AudioClip collideSFX;

		private NetworkVariable<NetworkPose> spawnPoseSync = new();
		public Pose SpawnPose => spawnPoseSync.Value;

		private Ray fireRay;
		private bool isAlive;
		private float spawnedTime;
		private float envHitDist;
		private float travelDist;

		public event Action OnFire = delegate { };
		public event Action OnCollide = delegate { };

		private void Awake()
		{
			spawnPoseSync.OnValueChanged += OnSpawnPosChange;
		}

		public override void OnNetworkSpawn()
		{
			envHitDist = MaxTravelDist;
			isAlive = true;
			spawnedTime = Time.time;

			if (IsOwner)
				spawnPoseSync.Value = new NetworkPose(transform);
			else
				SetPose(SpawnPose);

			OnFire.Invoke();
			AudioSource.PlayClipAtPoint(fireSFX, transform.position);

			EnvRaymarch();
		}

		private async void EnvRaymarch()
		{
			if (!XRSettings.enabled || !Player.Instance)
				return;

			fireRay = new(transform.position, transform.forward);
			var result = await EnvironmentMapper.Instance.RaymarchAsync(fireRay, MaxTravelDist);
			if (result.didHit)
			{
				if (IsOwner)
				{
					envHitDist = result.distance;
				}
				else
				{
					var headPos = Player.Instance.HeadTransform.position;
					var hitDistFromHead = Vector3.Distance(headPos, result.point);

					if (hitDistFromHead < EnvironmentMapper.Instance.MaxEyeDist)
						EnvironmentRaycastRpc(result.distance);
				}
			}
		}

		[Rpc(SendTo.Owner)]
		private void EnvironmentRaycastRpc(float dist)
		{
			if (dist > EnvironmentMapper.Instance.MaxEyeDist)
				envHitDist = Mathf.Min(envHitDist, dist);
		}

		private void OnSpawnPosChange(NetworkPose p, NetworkPose v) => SetPose(v);

		private void SetPose(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
		}

		private void Update()
		{
			if (isAlive)
			{
				float lifeTime = Time.time - spawnedTime;
				Vector3 prevPos = transform.position;
				travelDist = metersPerSecond * lifeTime;

				transform.position = fireRay.GetPoint(travelDist);

				if (IsOwner)
				{
					bool didHitEnv = travelDist > envHitDist;

					if (didHitEnv)
						transform.position = fireRay.GetPoint(envHitDist);

					bool didHitPhys = Physics.Linecast(prevPos, transform.position, out var physHit,
						Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

					if (didHitPhys)
					{
						HitRpc(physHit.point, physHit.normal);

						var col = physHit.collider;

						if (col.CompareTag(Networking.PlayerAvatar.Tag))
						{
							var av = col.GetComponentInParent<Networking.PlayerAvatar>();
							float damage = damageOverDistance.Evaluate(travelDist);
							av.DamageRpc(damage, OwnerClientId);
						}

					}
					else if (didHitEnv)
					{
						Vector3 envHitPoint = fireRay.GetPoint(envHitDist);
						HitRpc(envHitPoint, -transform.forward);
					}
				}
			}
		}

		[Rpc(SendTo.Everyone)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isAlive = false;

			OnCollide.Invoke();
			AudioSource.PlayClipAtPoint(collideSFX, transform.position);

			if (IsOwner)
			{
				StartCoroutine(D());
				IEnumerator D() {
					yield return new WaitForSeconds(despawnDelay);
					NetworkObject.Despawn();
				}
			}
		}
	}
}