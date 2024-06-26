using Anaglyph.LaserTag.Logistics;
using Anaglyph.LaserTag.Networking;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag
{
	public class Bolt : NetworkBehaviour
	{
		public float metersPerSecond;

		[SerializeField] private TrailRenderer trailRenderer;
		[SerializeField] private int msHitDeactivateDelay = 1000;
		[SerializeField] private float damage = 50f;

		public UnityEvent onHit = new();
		private bool isFlying = true;

		private float distancePerFrame => metersPerSecond * Time.deltaTime;

        private NetworkVariable<NetworkPose> networkPose
		= new(default, NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Owner);

        private void Awake()
		{
			networkPose.OnValueChanged += OnNetworkPoseChange;
		}

		public override void OnNetworkSpawn()
		{
            isFlying = true;
            if (IsOwner)
			{
				networkPose.Value = new NetworkPose(transform);
			}
			else
			{
				SetPoseLocally(networkPose.Value);
			}
		}

		private void OnNetworkPoseChange(NetworkPose p, NetworkPose newValue) => 
			SetPoseLocally(newValue);

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
			transform.position += transform.forward * distancePerFrame;
		}

		private void TestHit()
		{
			Vector3 camPos = DepthCast.Camera.transform.position;

			Vector3 boltFromCam = transform.position - camPos;

			if (Vector3.Magnitude(boltFromCam) > 100)
			{
				NetworkObject.Despawn();
			}

			// Despawn if out of camera view
			//Vector3 inViewSpace = DepthCast.Camera.WorldToViewportPoint(transform.position,
			//		Camera.MonoOrStereoscopicEye.Left);

			//if (inViewSpace.x < 0 || inViewSpace.x > 1 ||
			//	inViewSpace.y < 0 || inViewSpace.y > 1 || inViewSpace.z < 0)
			//{
			//	NetworkObject.Despawn();
			//}

			Ray forwardRay = new Ray(previousPosition, transform.forward);
			float distanceCovered = Vector3.Distance(previousPosition, transform.position);

			bool didHit = DepthCast.Raycast(forwardRay, out var depthHit, distanceCovered);

			if(didHit)
			{
				HandleHitLocally(depthHit.Position, depthHit.Normal);
			}

			//Vector3 diff = transform.position - previousPosition;
			if (Physics.Linecast(previousPosition, transform.position, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
			{
				HandleHitLocally(hit.point, hit.normal);

				if (hit.collider.CompareTag("Player"))
				{
					hit.collider.GetComponentInParent<Player>().HitRpc(damage);
				}
			}
		}

		private void HandleHitLocally(Vector3 pos, Vector3 norm)
		{
			if (IsSpawned) HitRpc(pos, norm);
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