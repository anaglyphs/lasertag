using System;
using System.Runtime.InteropServices;
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
		private static float ServerTime => NetworkManager.Singleton.ServerTime.TimeAsFloat;

		[SerializeField] private float metersPerSecond;
		[SerializeField] private AnimationCurve damageOverDistance = AnimationCurve.Constant(0, MaxTravelDist, 50f);

		[SerializeField] private int despawnDelay = 1;
		private CancellationTokenSource despawnCancelSrc;

		[SerializeField] private AudioClip fireSFX;
		[SerializeField] private AudioClip collideSFX;

		[Serializable, StructLayout(LayoutKind.Sequential)]
		public struct ShotData
		{
			public Ray ray;
			public float serverTimeShot;

			public float GetFlightTime()
			{
				return ServerTime - serverTimeShot;
			}

			public Vector3 GetFlightPosition(float metersPerSecond)
			{
				return ray.GetPoint(GetFlightTime() * metersPerSecond);
			}
		}

		private NetworkVariable<ShotData> shotSync = new();
		public ShotData Shot => shotSync.Value;
		
		private bool isAlive;
		private float travelDist;

		public event Action OnFire = delegate { };
		public event Action OnCollide = delegate { };

		private IDamageable.Data damageData;

		private void Awake()
		{
			shotSync.OnValueChanged += OnShot;
		}

		public override void OnNetworkSpawn()
		{
			isAlive = true;

			if (IsOwner)
			{
				ShotData shotData = new()
				{
					ray = new Ray(transform.position, transform.forward),
					serverTimeShot = ServerTime,
				};
				shotSync.Value = shotData;
			}
			else
			{
				UpdatePosition(Shot);
			}

			OnFire.Invoke();
			AudioPool.Play(fireSFX, transform.position);
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
			Vector3 pos = shot.GetFlightPosition(metersPerSecond);
			
			Pose p = new(pos, rot);
			
			UpdatePosition(p);
		}

		private void UpdatePosition(Pose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
		}

		private void Update()
		{
			if (!isAlive) return;
			
			Vector3 prevPos = transform.position;
			UpdatePosition(Shot);

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
			
			if (AnaglyphDebugging.DebugMode) DrawDebug();
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