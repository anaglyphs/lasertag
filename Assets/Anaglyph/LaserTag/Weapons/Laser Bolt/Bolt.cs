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

		private bool justAwoke = true;
		private bool didHit = false;
		//private float damage = Role.Default.GunDamage;

		private float distancePerFrame => metersPerSecond * Time.deltaTime;

		public override void OnNetworkSpawn()
		{
			didHit = false;

			if(IsOwner)
			{
				SetPoseRpc(new NetworkPose(transform.position, transform.rotation));
				// SetDamageRpc(PlayerLocal.Instance.currentRole.GunDamage);
            }
		}

		private void OnEnable()
		{
			justAwoke = true;
		}

		private void OnDisable()
		{
			trailRenderer.Clear();
			
		}

		private void Update()
		{
			if(justAwoke)
			{
				trailRenderer.AddPosition(transform.position);
				previousPosition = transform.position;
				justAwoke = false;
			}

			if (!didHit)
			{
				Fly();
				
				if(IsOwner)
				{
					TestHit();
				}
			}
        }

        [Rpc(SendTo.Everyone)]
        private void SetPoseRpc(NetworkPose pose)
        {
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        [Rpc(SendTo.Everyone)]
        private void SetDamageRpc(float newDamage)
        {
			this.damage = newDamage;
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
				Hit(depthHit.Position, depthHit.Normal);
			}

			//Vector3 diff = transform.position - previousPosition;
			if (Physics.Linecast(previousPosition, transform.position, out RaycastHit hit))
			{
				Hit(hit.point, hit.normal);

				if (hit.collider.CompareTag("Player"))
				{
					hit.collider.GetComponentInParent<Player>().HitRpc(damage);
				}
			}
		}

		private void Hit(Vector3 pos, Vector3 norm)
		{
			if(IsSpawned)
				HitRpc(pos, norm);
			DespawnWithDelay();
		}

		[Rpc(SendTo.Everyone)]
		private void HitRpc(Vector3 hitPos, Vector3 hitNorm)
		{
			transform.position = hitPos;
			transform.up = Vector3.Reflect(transform.forward, hitNorm);
			didHit = true;
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