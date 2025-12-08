using Anaglyph.XRTemplate;
using System;
using System.Threading;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Bullet : NetworkBehaviour
	{
		private const float MaxTravelDist = 50;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private AnimationCurve damageOverDistance = AnimationCurve.Constant(0, MaxTravelDist, 50f);

		[SerializeField] private int despawnDelay = 1;
		private CancellationTokenSource despawnCancelSrc;

		[SerializeField] private AudioClip fireSFX;
		[SerializeField] private AudioClip collideSFX;

		private NetworkVariable<Pose> spawnPoseSync = new();
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
				spawnPoseSync.Value = transform.GetWorldPose();
			else
				SetPose(SpawnPose);

			OnFire.Invoke();
			AudioPool.Play(fireSFX, transform.position);

			fireRay = new Ray(transform.position, transform.forward);

			EnvRaymarch();
		}

		private async void EnvRaymarch()
		{
			if (!MainPlayer.Instance)
				return;

			var result = await EnvironmentMapper.Instance.RaymarchAsync(fireRay, MaxTravelDist);
			if (NetworkObject.IsSpawned && result.DidHit)
			{
				if (IsOwner)
				{
					envHitDist = result.Distance;
				}
				else
				{
					var headPos = MainPlayer.Instance.HeadTransform.position;
					var hitDistFromHead = Vector3.Distance(headPos, result.Point);

					if (hitDistFromHead < EnvironmentMapper.Instance.MaxEyeDist)
						EnvironmentRaycastRpc(result.Distance);
				}
			}
		}

		[Rpc(SendTo.Owner)]
		private void EnvironmentRaycastRpc(float dist)
		{
			if (dist > EnvironmentMapper.Instance.MaxEyeDist)
				envHitDist = Mathf.Min(envHitDist, dist);
		}

		private void OnSpawnPosChange(Pose p, Pose v)
		{
			SetPose(v);
		}

		private void SetPose(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
		}

		private void Update()
		{
			if (!isAlive) return;
			
			var lifeTime = Time.time - spawnedTime;
			var prevPos = transform.position;
			travelDist = metersPerSecond * lifeTime;

			transform.position = fireRay.GetPoint(travelDist);

			if (IsOwner)
			{
				var didHitEnv = travelDist > envHitDist;

				if (didHitEnv)
					transform.position = fireRay.GetPoint(envHitDist);

				var didHitPhys = Physics.Linecast(prevPos, transform.position, out var physHit,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

				if (didHitPhys)
				{
					HitRpc(physHit.point, physHit.normal);

					var col = physHit.collider;

					if (col.CompareTag(Networking.PlayerAvatar.Tag))
					{
						var av = col.GetComponentInParent<Networking.PlayerAvatar>();
						var damage = damageOverDistance.Evaluate(travelDist);
						av.DamageRpc(damage, OwnerClientId);
					}
				}
				else if (didHitEnv)
				{
					var envHitPoint = fireRay.GetPoint(envHitDist);
					HitRpc(envHitPoint, -transform.forward);
				}
			}
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isAlive = false;

			OnCollide.Invoke();
			AudioPool.Play(collideSFX, transform.position);

			// if(IsOwner)
			// 	NetworkObject.Despawn();
			
			// commented out to make sure this wasn't the problem

			if (IsOwner)
				DelayedDespawn();
		}

		private async void DelayedDespawn()
		{
			despawnCancelSrc = new CancellationTokenSource();
			var ctn = despawnCancelSrc.Token;

			try
			{
				await Awaitable.WaitForSecondsAsync(despawnDelay, ctn);
				ctn.ThrowIfCancellationRequested();
				NetworkObject.Despawn(true);
			}
			catch (OperationCanceledException)
			{
			}
		}

		public override void OnNetworkDespawn()
		{
			despawnCancelSrc?.Cancel();
		}
	}
}