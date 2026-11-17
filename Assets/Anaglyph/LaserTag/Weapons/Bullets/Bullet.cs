using System;
using System.Runtime.InteropServices;
using System.Threading;
using Anaglyph.Audio;
using Anaglyph.Debugging;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.LaserTag.Weapons.Bullets
{
	public class Bullet : NetworkBehaviour
	{
		private const float MaxTravelDist = 100;
		private static float ServerTime => SharedNetworkTime.TimeAsFloat;

		[SerializeField] private float metersPerSecond;
		[SerializeField, Min(0)] private float collisionRadius;
		[SerializeField] private AnimationCurve damageOverDistance = AnimationCurve.Constant(0, MaxTravelDist, 50f);

		[SerializeField] private int despawnDelay = 1;
		private CancellationTokenSource despawnCancelSrc;

		[SerializeField] private AudioClip collideSFX;
		[SerializeField, Min(0.01f)] private float collideSFXFullVolumeDistance = 1;

		[Serializable, StructLayout(LayoutKind.Sequential)]
		public struct ShotData : INetworkSerializeByMemcpy
		{
			public Ray ray;
			public float serverTimeShot;
			public ushort groupId;
			public ushort groupSize;

			public bool IsGrouped => groupSize > 1;

			public float GetFlightTime()
			{
				return ServerTime - serverTimeShot;
			}
		}

		private NetworkVariable<ShotData> shotSync = new();
		public ShotData Shot => shotSync.Value;
		public float MetersPerSecond => metersPerSecond;

		private bool isAlive;
		private bool hasCollided;
		private float travelDist;
		private readonly RaycastHit[] castHits = new RaycastHit[8];
		private static int frameCollidersWereSynced = -1;

		public event Action OnFire = delegate { };
		public event Action OnCollide = delegate { };

		public event Action OnDespawned = delegate { };

		private IDamageable.Data damageData;

		private ushort assignedGroupId;
		private ushort assignedGroupSize;

		// call before spawning; bullets sharing an id and owner share one light
		public void AssignGroup(ushort groupId, ushort groupSize)
		{
			assignedGroupId = groupId;
			assignedGroupSize = groupSize;
		}

		private void Awake()
		{
			shotSync.OnValueChanged += OnShot;
		}

		public override void OnNetworkSpawn()
		{
			isAlive = true;
			hasCollided = false;

			if (IsOwner)
			{
				ShotData shotData = new()
				{
					ray = new Ray(transform.position, transform.forward),
					serverTimeShot = ServerTime,
					groupId = assignedGroupId,
					groupSize = assignedGroupSize,
				};
				shotSync.Value = shotData;
			}
			else
			{
				// start at the muzzle so the first Update casts the whole path flown before this peer heard of the shot
				UpdatePosition(new Pose(Shot.ray.origin, Quaternion.LookRotation(Shot.ray.direction)));
			}

			OnFire.Invoke();
		}

		private void OnShot(ShotData prev, ShotData curr)
		{
			Quaternion rot = Quaternion.LookRotation(curr.ray.direction);
			Pose p = new(curr.ray.origin, rot);
			
			UpdatePosition(p);
		}

		private void UpdatePosition(ShotData shot)
		{
			Quaternion rot = Quaternion.LookRotation(shot.ray.direction);
			travelDist = metersPerSecond * shot.GetFlightTime();
			Vector3 pos = shot.ray.GetPoint(travelDist);
			
			Pose p = new(pos, rot);
			
			UpdatePosition(p);
		}

		private void UpdatePosition(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
		}

		private void Update()
		{
			if (AnaglyphDebugging.DebugMode) DrawDebug();

			if (!isAlive) return;
			
			Vector3 prevPos = transform.position;
			UpdatePosition(Shot);
			SyncCollidersOncePerFrame();

			if (TryCastAlongFlight(prevPos, transform.position, out RaycastHit physHit))
			{
				if (IsOwner)
					ApplyHit(physHit);
				else
					Collide(physHit.point, physHit.normal); // predicted: visuals only, the owner's HitRpc decides damage
				return;
			}

			if (IsOwner && travelDist > MaxTravelDist)
			{
				isAlive = false;
				DelayedDespawn().RunUntilCancelled();
			}
		}

		// physics otherwise sees moved colliders only after a fixed step
		private static void SyncCollidersOncePerFrame()
		{
			if (frameCollidersWereSynced == Time.frameCount) return;

			frameCollidersWereSynced = Time.frameCount;
			Physics.SyncTransforms();
		}

		private bool TryCastAlongFlight(Vector3 from, Vector3 to, out RaycastHit nearestHit)
		{
			nearestHit = default;
			Vector3 travel = to - from;
			float distance = travel.magnitude;
			if (distance <= 0) return false;
			Vector3 direction = travel / distance;

			int hitCount = collisionRadius > 0
				? Physics.SphereCastNonAlloc(from, collisionRadius, direction, castHits, distance,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
				: Physics.RaycastNonAlloc(from, direction, castHits, distance,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

			Transform shooter = NetworkManager.SpawnManager.GetPlayerNetworkObject(OwnerClientId)?.transform;
			bool found = false;
			for (int i = 0; i < hitCount; i++)
			{
				RaycastHit hit = castHits[i];
				// a remote shooter's avatar lags behind their real body, so their own shot can cross it
				if (shooter != null && hit.collider.transform.IsChildOf(shooter)) continue;
				// sweeps that start overlapping report a zero point; keep the bullet on its path instead
				if (hit.distance <= 0) hit.point = from;
				if (!found || hit.distance < nearestHit.distance)
				{
					nearestHit = hit;
					found = true;
				}
			}
			return found;
		}

		private void ApplyHit(RaycastHit physHit)
		{
			float dist = Vector3.Distance(Shot.ray.origin, physHit.point);

			damageData = new IDamageable.Data
			{
				playerID = OwnerClientId,
				damage = damageOverDistance.Evaluate(dist)
			};

			HitRpc(physHit.point, physHit.normal);
			Collider col = physHit.collider;
			IProjectileDamageReceiver receiver = col.GetComponentInParent<IProjectileDamageReceiver>();
			if (receiver != null)
				receiver.ReceiveProjectileHit(new ProjectileHit
				{
					damage = damageData,
					position = physHit.point
				});
			else
				IDamageable.DamageParentOf(col, physHit.point, damageData);
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
			if (!hasCollided)
				Collide(pos, norm);

			if (IsOwner)
				DelayedDespawn().RunUntilCancelled();
		}

		private void Collide(Vector3 pos, Vector3 norm)
		{
			hasCollided = true;
			isAlive = false;
			transform.position = pos;
			transform.up = norm;

			OnCollide.Invoke();
			AudioPool.Play(collideSFX, transform.position, 1, collideSFXFullVolumeDistance);
		}

		private async Awaitable DelayedDespawn()
		{
			despawnCancelSrc = new CancellationTokenSource();
			CancellationToken ctn = despawnCancelSrc.Token;

			await Awaitable.WaitForSecondsAsync(despawnDelay, ctn);
			ctn.ThrowIfCancellationRequested();
			NetworkObject.Despawn(true);
		}

		public override void OnNetworkDespawn()
		{
			isAlive = false;
			despawnCancelSrc?.Cancel();
			OnDespawned.Invoke();
		}
	}
}
