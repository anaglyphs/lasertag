using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : NetworkBehaviour, IColocator
	{
		public event Action Colocated = delegate { };
		public static MetaAnchorColocator Instance { get; private set; }
		[SerializeField] private ColocationAnchor anchorPrefab;

		private void Awake()
		{
			Instance = this;
		}

		public override void OnNetworkSpawn()
		{
			ColocationAnchor.Aligned += Colocated.Invoke;
		}

		public override void OnNetworkDespawn()
		{
			ColocationAnchor.Aligned -= Colocated.Invoke;
		}

		public void Colocate()
		{
			if (IsOwner)
				InstantiateNewAnchor();
		}

		public void InstantiateNewAnchor()
		{
			var head = MainXRRig.Camera.transform;

			var spawnPos = head.position;
			spawnPos.y -= 1.5f;

			var flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			var spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			var g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
			g.TryGetComponent(out NetworkObject networkObject);
			networkObject.Spawn();
		}
	}
}