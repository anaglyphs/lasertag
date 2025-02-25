using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using System.Collections;
using System.Drawing;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class Bullet : NetworkBehaviour
	{
		private const float MaxTravelDist = 50;
		private const float IgnoreConsensusWithin = 10f;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private float damage = 50f;

		[SerializeField] private int msHitDeactivateDelay = 1000;

		[SerializeField] private AudioClip fireSFX;
		[SerializeField] private AudioClip collideSFX;

		private bool isFlying = true;

		private NetworkVariable<NetworkPose> spawnPoseSync = new();
		public Pose SpawnPose => spawnPoseSync.Value;

		private float spawnTime;
		private float envHitDistance;

		public UnityEvent OnFire = new();
		public UnityEvent OnCollide = new();

		private Ray fireRay;

		private void Awake()
		{
			spawnPoseSync.OnValueChanged += OnSpawnPosChange;
		}

		public override void OnNetworkSpawn()
		{
			envHitDistance = MaxTravelDist;
			isFlying = true;
			spawnTime = Time.time;

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
			var hit = Environment.Raycast(fireRay, MaxTravelDist);
			if (hit.didHit && (IsOwner || hit.distance > EnvironmentMapper.Instance.MaxEyeDist))
				ReportWorldHitToOwnerRPC(hit.distance);
		}

		private void OnSpawnPosChange(NetworkPose p, NetworkPose v) => SetPoseLocally(v);

		private void SetPoseLocally(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
			previousPosition = transform.position;
		}

		private void Update()
		{
			if (isFlying)
			{
				Fly();
				
				if(IsOwner)
					TestHit();
			}
		}

		Vector3 previousPosition = Vector3.zero;
		private void Fly()
		{
			//float networkTime = NetworkManager.LocalTime.TimeAsFloat;
			//float lifeTime = networkTime - spawnTimeSync.Value;
			float lifeTime = Time.time - spawnTime;
			previousPosition = transform.position;
			Vector3 travel = transform.forward * metersPerSecond * lifeTime;
			transform.position = spawnPoseSync.Value.position + travel;
		}

		private void TestHit()
		{
			if (!IsOwner)
				return;

			bool didHitEnv = Vector3.Distance(transform.position, SpawnPose.position) > envHitDistance;

			if (didHitEnv)
				transform.position = fireRay.GetPoint(envHitDistance);

			bool didHitPhys = Physics.Linecast(previousPosition, transform.position, out RaycastHit physHit,
				Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

			if(didHitPhys)
			{
				Hit(physHit.point, physHit.normal);

				if (physHit.collider.CompareTag(Networking.Avatar.Tag))
					physHit.collider.GetComponentInParent<Networking.Avatar>().DamageRpc(damage, OwnerClientId);

			} else if(didHitEnv)
			{
				Vector3 envHitPoint = fireRay.GetPoint(envHitDistance);
				Hit(envHitPoint, -transform.forward);
			}
		}

		private void Hit(Vector3 pos, Vector3 norm)
		{
			if (IsSpawned)
				HitRpc(pos, norm);

			DespawnWithDelay();
		}

		[Rpc(SendTo.Owner)]
		private void ReportWorldHitToOwnerRPC(float dist)
		{
			envHitDistance = Mathf.Min(envHitDistance, dist);
		}

		[Rpc(SendTo.Everyone)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isFlying = false;

			OnCollide.Invoke();
			AudioSource.PlayClipAtPoint(collideSFX, transform.position);
		}

		private async void DespawnWithDelay()
		{
			await Task.Delay(msHitDeactivateDelay);

			if (IsOwner && IsSpawned)
				NetworkObject.Despawn();
		}
	}
}