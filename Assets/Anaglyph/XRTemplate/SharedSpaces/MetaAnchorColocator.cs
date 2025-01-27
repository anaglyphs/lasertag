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
			ColocationAnchor.ActiveAnchorChange += OnActiveAnchorChange;
		}

		protected override void OnSingletonDestroy()
		{
			ColocationAnchor.ActiveAnchorChange -= OnActiveAnchorChange;
		}

		private void OnActiveAnchorChange(ColocationAnchor anchor)
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

		public void SpawnPrefab()
		{
			Vector3 spawnPos = spawnTarget.position;
			spawnPos.y = 0;

			Vector3 flatForward = spawnTarget.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject newAnchorObject = Instantiate(sharedAnchorPrefab, spawnPos, spawnRot);

			NetworkedSpatialAnchor newAnchor = newAnchorObject.GetComponent<NetworkedSpatialAnchor>();

			newAnchor.NetworkObject.Spawn();
		}
	}
}