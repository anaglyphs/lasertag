using Anaglyph.Netcode;
using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class AnchorColocator : SingletonBehavior<AnchorColocator>
	{
		[SerializeField] private GameObject sharedAnchorPrefab;

		private Transform spawnTarget;
		public static EventVariable<bool> IsColocated = new(false);

		protected override void SingletonAwake()
		{

		}

		protected override void OnSingletonDestroy()
		{
			if (NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
		}

		private void Start()
		{
			spawnTarget = Camera.main.transform;

			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
		}

		private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
		{
			if (!manager.IsHost)
				return;

			if (NetcodeHelpers.ThisClientConnected(data))
				SpawnPrefab();
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