using System;
using System.Threading;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using Utilities.XR;

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
		private float travelDist;

		public event Action OnFire = delegate { };
		public event Action OnCollide = delegate { };

		private IDamageable.Data damageData;

		private void Awake()
		{
			spawnPoseSync.OnValueChanged += OnSpawnPosChange;
		}

		public override void OnNetworkSpawn()
		{
			isAlive = true;
			spawnedTime = Time.time;

			if (IsOwner)
				spawnPoseSync.Value = transform.GetWorldPose();
			else
				SetPose(SpawnPose);

			OnFire.Invoke();
			AudioPool.Play(fireSFX, transform.position);

			fireRay = new Ray(transform.position, transform.forward);
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
			if (AnaglyphDebugging.DebugMode) DrawDebug();

			if (!isAlive) return;

			float lifeTime = Time.time - spawnedTime;
			Vector3 prevPos = transform.position;
			travelDist = metersPerSecond * lifeTime;

			transform.position = fireRay.GetPoint(travelDist);

			if (IsOwner)
			{
				bool didHit = Physics.Linecast(prevPos, transform.position, out RaycastHit physHit,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

				if (didHit)
				{
					HitRpc(physHit.point, physHit.normal);

					float damage = damageOverDistance.Evaluate(travelDist);

					Collider col = physHit.collider;

					damageData = new IDamageable.Data
					{
						playerID = OwnerClientId,
						damage = damage
					};

					IDamageable.DamageHierarchy(col.transform.root, damageData);
				}

				if (travelDist > MaxTravelDist)
					NetworkObject.Despawn(true);
			}
		}

		private void DrawDebug()
		{
			Color color = isAlive ? Color.white : Color.yellow;

			XRGizmos.DrawWireSphere(transform.position, transform.rotation, 0.1f, color);
			XRGizmos.DrawArrow(transform.position, transform.rotation, color);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isAlive = false;

			OnCollide.Invoke();
			AudioPool.Play(collideSFX, transform.position);

			if (IsOwner)
				DelayedDespawn();
		}

		private async void DelayedDespawn()
		{
			despawnCancelSrc = new CancellationTokenSource();
			CancellationToken ctn = despawnCancelSrc.Token;

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