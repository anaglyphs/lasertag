using Anaglyph.Lasertag.Logistics;
using Anaglyph.Lasertag.Networking;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class Bolt : NetworkBehaviour
	{
		public float metersPerSecond;

		[SerializeField] private TrailRenderer trailRenderer;
		[SerializeField] private int msHitDeactivateDelay = 1000;
		[SerializeField] private float damage = 50f;

		public UnityEvent onHit = new();
		private bool isFlying = true;

		private NetworkVariable<NetworkPose> networkPos = new();

		private void Awake()
		{
			networkPos.OnValueChanged += OnSpawnPosChange;
		}

		public override void OnNetworkSpawn()
		{
			isFlying = true;
			if (IsOwner)
			{
				networkPos.Value = new NetworkPose(transform);
			}
			else
			{
				SetPoseLocally(networkPos.Value);
			}
		}

		private void OnSpawnPosChange(NetworkPose p, NetworkPose v) => SetPoseLocally(v);

		private void SetPoseLocally(NetworkPose pose)
		{
			transform.SetPositionAndRotation(pose.position, pose.rotation);
			previousPosition = transform.position;
			trailRenderer.Clear();
			trailRenderer.AddPosition(transform.position);
		}

		private void OnEnable()
		{
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
			previousPosition = transform.position;
			transform.position += transform.forward * metersPerSecond * Time.deltaTime;
		}

		private void TestHit()
		{
			if (!IsOwner)
				return;

			if (Vector3.Distance(transform.position, MainPlayer.Instance.HeadTransform.position) > 100)
				NetworkObject.Despawn();

			Ray forwardRay = new Ray(previousPosition, transform.forward);
			float distanceCovered = Vector3.Distance(previousPosition, transform.position);

			bool depthDidHit = DepthCast.Raycast(forwardRay, out var depthHit, distanceCovered);
			bool physDidHit = Physics.Linecast(previousPosition, transform.position, out RaycastHit hit,
				Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

			if (depthDidHit && physDidHit)
			{
				bool castIsCloser = hit.distance < Vector3.Distance(depthHit.Position, previousPosition)
					|| Vector3.Distance(depthHit.Position, hit.point) < 0.1f;

				if (castIsCloser)
					depthDidHit = false;
			}

			if (depthDidHit)
			{
				Hit(depthHit.Position, depthHit.Normal);

			}
			else if (physDidHit)
			{
				Hit(hit.point, hit.normal);

				if (hit.collider.CompareTag(Player.Tag))
					hit.collider.GetComponentInParent<Player>().DamageRpc(damage, OwnerClientId);
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
		}

		private async void DespawnWithDelay()
		{
			await Task.Delay(msHitDeactivateDelay);

			if (IsOwner && IsSpawned)
				NetworkObject.Despawn();
		}
	}
}