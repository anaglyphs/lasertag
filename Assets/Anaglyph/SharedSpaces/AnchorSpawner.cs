using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	public class AnchorSpawner : MonoBehaviour
	{
		[SerializeField] private GameObject sharedAnchorPrefab;

		private Transform spawnTarget;

		private void Awake()
		{
			spawnTarget = Camera.main.transform;
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