using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : NetworkBehaviour, IColocator
	{
		public static MetaAnchorColocator Instance { get; private set; }

		[SerializeField] private ColocationAnchor anchorPrefab;

		public event Action Colocated = delegate { };

		ulong anchorIdSync = ulong.MaxValue;
		private ColocationAnchor _currentAnchor;
		public ColocationAnchor CurrentAnchor => _currentAnchor;

		private void Awake()
		{
			Instance = this;
		}

		protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
		{
			serializer.SerializeValue(ref anchorIdSync);
		}

		public void StartColocation()
		{
			UpdateCurrentAnchor();
			if (!CurrentAnchor)
				RealignEveryone();
		}
		
		private void UpdateCurrentAnchor()
		{
			var objs = NetworkManager.SpawnManager.SpawnedObjects;
			if (!objs.TryGetValue(anchorIdSync, out var anchorNetObj)) return;

			var anchor = anchorNetObj.GetComponent<ColocationAnchor>();
			if (anchor != _currentAnchor)
			{
				_currentAnchor = anchor;
				_currentAnchor.WorldLocker.Aligned += OnAligned;
			}
		}

		public void RealignEveryone()
		{
			var localId = NetworkManager.LocalClientId;
			if (localId != OwnerClientId)
				NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);

			if (!IsOwner)
				throw new Exception("Not owner! Can't spawn new anchor");

			// spawn anchor
			var head = MainXRRig.Camera.transform;

			var spawnPos = head.position;
			spawnPos.y -= 1.5f;

			var flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			var spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			var g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
			g.TryGetComponent(out NetworkObject netObj);
			netObj.Spawn();
			
			anchorIdSync = netObj.NetworkObjectId;
			SpawnedNewAnchorRpc(anchorIdSync);
		}

		[Rpc(SendTo.Everyone)]
		private void SpawnedNewAnchorRpc(ulong id)
		{
			if (_currentAnchor && _currentAnchor.IsOwner)
				_currentAnchor.NetworkObject.Despawn(true);
			
			anchorIdSync = id;

			UpdateCurrentAnchor();
		}

		private void OnAligned()
		{
			Colocated.Invoke();
		}

		public void StopColocation()
		{
			// do nothing. world lock anchor naturally despawns
		}
	}
}