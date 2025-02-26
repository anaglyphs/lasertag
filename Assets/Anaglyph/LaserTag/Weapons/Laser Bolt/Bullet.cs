using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class Bullet : NetworkBehaviour
	{
		private const float MaxTravelDist = 50;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private float damage = 50f;

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

		public UnityEvent OnFire = new();
		public UnityEvent OnCollide = new();

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
			{
				spawnPoseSync.Value = new NetworkPose(transform);
			}
			else
			{
				SetPoseLocally(SpawnPose);
			}

			OnFire.Invoke();
			AudioSource.PlayClipAtPoint(fireSFX, transform.position);

			fireRay = new(transform.position, transform.forward);
			if (EnvironmentMapper.Raycast(fireRay, MaxTravelDist, out var envCast))
				if (IsOwner)
					envHitDist = envCast.distance;
				else
					SyncLocalWorldHitRPC(envCast.distance);
		}

		private void OnSpawnPosChange(NetworkPose p, NetworkPose v) => SetPoseLocally(v);

		private void SetPoseLocally(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
			prevPos = transform.position;
		}

		private void Update()
		{
			if (isAlive)
			{
				Fly();
				
				if(IsOwner)
					TestHit();
			}
		}

		Vector3 prevPos = Vector3.zero;
		private void Fly()
		{
			float lifeTime = Time.time - spawnedTime;
			prevPos = transform.position;
			travelDist = metersPerSecond * lifeTime;

			transform.position = fireRay.GetPoint(travelDist);
		}

		private void TestHit()
		{
			if (!IsOwner)
				return;

			bool didHitEnv = travelDist > envHitDist;

			if (didHitEnv)
				transform.position = fireRay.GetPoint(envHitDist);

			bool didHitPhys = Physics.Linecast(prevPos, transform.position, out var physHit,
				Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

			if(didHitPhys)
			{
				Hit(physHit.point, physHit.normal);

				var col = physHit.collider;

				if (col.CompareTag(Networking.Avatar.Tag))
				{
					var av = col.GetComponentInParent<Networking.Avatar>();
					av.DamageRpc(damage, OwnerClientId);
				}

			} else if(didHitEnv)
			{
				Vector3 envHitPoint = fireRay.GetPoint(envHitDist);
				Hit(envHitPoint, -transform.forward);
			}
		}

		private async void Hit(Vector3 pos, Vector3 norm)
		{
			if (IsSpawned)
				HitRpc(pos, norm);

			await Awaitable.WaitForSecondsAsync(despawnDelay);

			if (IsOwner && IsSpawned)
				NetworkObject.Despawn();
		}

		[Rpc(SendTo.Owner)]
		private void SyncLocalWorldHitRPC(float dist)
		{
			if(dist > EnvironmentMapper.Instance.MaxEyeDist)
				envHitDist = Mathf.Min(envHitDist, dist);
		}

		[Rpc(SendTo.Everyone)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isAlive = false;

			OnCollide.Invoke();
			AudioSource.PlayClipAtPoint(collideSFX, transform.position);
		}
	}
}