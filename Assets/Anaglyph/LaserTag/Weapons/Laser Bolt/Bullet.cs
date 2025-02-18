using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class Bullet : NetworkBehaviour
	{
		public float metersPerSecond;

		[SerializeField] private TrailRenderer trailRenderer = null;
		[SerializeField] private int msHitDeactivateDelay = 1000;
		[SerializeField] private float damage = 50f;

		[SerializeField] private float laserVisualPadding = 0.1f;

		public UnityEvent onFire = new();
		public UnityEvent onHit = new();
		public UnityEvent onFrameAfterHit = new();
		private bool isFlying = true;

		private NetworkVariable<NetworkPose> spawnPosSync = new();
		//private NetworkVariable<float> spawnTimeSync = new();
		private float spawnTime;

		private void Awake()
		{
			spawnPosSync.OnValueChanged += OnSpawnPosChange;
		}

		public override void OnNetworkSpawn()
		{
			isFlying = true;
			if (IsOwner)
			{
				spawnPosSync.Value = new NetworkPose(transform);

				//float networkTime = NetworkManager.LocalTime.TimeAsFloat;
				// spawnTimeSync.Value = networkTime;
			}
			else
			{
				SetPoseLocally(spawnPosSync.Value);
			}

			spawnTime = Time.time;
		}

		private void OnSpawnPosChange(NetworkPose p, NetworkPose v) => SetPoseLocally(v);

		private void SetPoseLocally(NetworkPose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
			previousPosition = transform.position;
			trailRenderer.Clear();
			trailRenderer.AddPosition(transform.position - transform.forward * laserVisualPadding);
		}

		private void OnEnable()
		{
			onFire.Invoke();
			isFlying = true;
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
			transform.position = spawnPosSync.Value.position + travel;
		}

		private void TestHit()
		{
			if (!IsOwner)
				return;

			if (Vector3.Distance(transform.position, MainPlayer.Instance.HeadTransform.position) > 100)
				NetworkObject.Despawn();

			Ray forwardRay = new Ray(previousPosition, transform.forward);
			float distanceCovered = Vector3.Distance(previousPosition, transform.position);

			//bool depthDidHit = DepthCast.Raycast(forwardRay, out var depthHit, distanceCovered);
			bool depthDidHit = EnvironmentTSDFMapper.Instance.Raycast(forwardRay, distanceCovered, out Vector3 pointHit);

			bool physDidHit = Physics.Linecast(previousPosition, transform.position, out RaycastHit hit,
				Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

			if (depthDidHit && physDidHit)
			{
				bool castIsCloser = hit.distance < Vector3.Distance(pointHit, previousPosition)
					|| Vector3.Distance(pointHit, hit.point) < 0.1f;

				if (castIsCloser)
					depthDidHit = false;
			}

			if (depthDidHit)
			{
				Hit(pointHit, -forwardRay.direction);

			}
			else if (physDidHit)
			{
				Hit(hit.point, hit.normal);

				if (hit.collider.CompareTag(Networking.Avatar.Tag))
					hit.collider.GetComponentInParent<Networking.Avatar>().DamageRpc(damage, OwnerClientId);
			}
		}

		private void Hit(Vector3 pos, Vector3 norm)
		{
			if (IsSpawned)
				HitRpc(pos, norm);

			DespawnWithDelay();
		}

		[Rpc(SendTo.Everyone)]
		private void HitRpc(Vector3 pos, Vector3 norm)
		{
			transform.position = pos;
			transform.up = norm;
			isFlying = false;
			onHit.Invoke();
			Vector3 trailTermination = transform.position + transform.forward * laserVisualPadding;
			if (trailRenderer.positionCount > 2)
			{
				trailRenderer.SetPosition(trailRenderer.positionCount - 1, trailTermination);
			}else
			{
				trailRenderer.AddPosition(trailTermination);
			}
			StartCoroutine(WaitForFrame());
		}

		private IEnumerator WaitForFrame()
		{
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			onFrameAfterHit.Invoke();
		}

		private async void DespawnWithDelay()
		{
			await Task.Delay(msHitDeactivateDelay);

			if (IsOwner && IsSpawned)
				NetworkObject.Despawn();
		}
	}
}