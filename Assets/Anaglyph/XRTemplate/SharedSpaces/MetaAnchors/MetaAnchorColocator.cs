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

		private readonly NetworkVariable<ulong> currentAnchorId = new(ulong.MaxValue);
		private ColocationAnchor _currentAnchor;
		public ColocationAnchor CurrentAnchor => _currentAnchor;

		private void Awake()
		{
			Instance = this;

			currentAnchorId.OnValueChanged += delegate
			{
				// ensure there is only one anchor
				if (_currentAnchor && _currentAnchor.IsOwner)
					_currentAnchor.NetworkObject.Despawn(true);

				GetCurrentAnchor();
			};
		}

		private void GetCurrentAnchor()
		{
			var objs = NetworkManager.SpawnManager.SpawnedObjects;
			if (!objs.TryGetValue(currentAnchorId.Value, out var anchorNetObj)) return;

			var anchor = anchorNetObj.GetComponent<ColocationAnchor>();
			if (anchor != _currentAnchor)
			{
				_currentAnchor = anchor;
				_currentAnchor.WorldLocker.Aligned += OnAligned;
			}
		}

		public void StartColocation()
		{
			GetCurrentAnchor();
			if (!CurrentAnchor)
				RealignEveryone();
		}

		public void RealignEveryone()
		{
			var localId = NetworkManager.LocalClientId;
			if (localId != OwnerClientId)
				NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);

			SpawnNewCurrentAnchor();
		}

		private void SpawnNewCurrentAnchor()
		{
			if (!IsOwner)
				throw new Exception("Only the owner can spawn a new anchor!");

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

			currentAnchorId.Value = netObj.NetworkObjectId;
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