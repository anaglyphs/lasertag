using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class MetaAnchorColocator : SingletonBehavior<MetaAnchorColocator>, IColocator
	{
		[SerializeField] private GameObject sharedAnchorPrefab;
		private NetworkedAnchor networkedAnchor;

		private Transform spawnTarget;

		private bool _isColocated;
		public event Action<bool> IsColocatedChange;
		private void SetIsColocated(bool b) => IsColocated = b;
		public bool IsColocated
		{
			get => _isColocated;
			set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed) 
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		protected override void SingletonAwake()
		{
			WorldLock.ActiveLockChange += OnActiveAnchorChange;
		}

		protected override void OnSingletonDestroy()
		{
			WorldLock.ActiveLockChange -= OnActiveAnchorChange;
		}

		private void OnActiveAnchorChange(WorldLock anchor)
		{
			IsColocated = anchor != null;
		}

		private void Start()
		{
			spawnTarget = Camera.main.transform;

			// NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
		}

		//private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		//{
		//	if (NetcodeHelpers.ThisClientConnected(data))
		//	{
		//		if(manager.IsHost)
		//			SpawnPrefab();
		//		else
		//			xrRig.transform.position = new Vector3(0, 1000, 0);

		//	} else if(NetcodeHelpers.ThisClientDisconnected(data))
		//	{
		//		xrRig.transform.position = new Vector3(0, 0, 0);
		//	}
		//}

		public void Colocate()
		{
			if (NetworkManager.Singleton.IsHost)
				SpawnPrefab();
			else
				MainXROrigin.TrackingSpace.position = new Vector3(0, 1000, 0);
		}

		public void StopColocation()
		{
			IsColocated = false;

			if (networkedAnchor != null && networkedAnchor.IsSpawned)
				networkedAnchor.NetworkObject.Despawn();
		}

		public void SpawnPrefab()
		{
			Vector3 spawnPos = spawnTarget.position;
			spawnPos.y = 0;

			Vector3 flatForward = spawnTarget.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject g = Instantiate(sharedAnchorPrefab, spawnPos, spawnRot);
			g.TryGetComponent(out networkedAnchor);
			networkedAnchor.NetworkObject.Spawn();
		}
	}
}