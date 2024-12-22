using SharedSpaces;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class AnchorSpawner : MonoBehaviour
	{
		[SerializeField] private GameObject sharedAnchorPrefab;

		Transform spawnTarget;

		private void Start()
		{
			spawnTarget = Camera.main.transform;

			NetworkManager.Singleton.OnConnectionEvent += OnConnectionEvent;
		}

		private void OnDestroy()
		{
			if(NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= OnConnectionEvent;
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

		public void DespawnAll()
		{
			NetworkedSpatialAnchor.DespawnAll();
		}
	}
}