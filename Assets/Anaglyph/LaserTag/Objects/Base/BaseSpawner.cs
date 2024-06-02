using Anaglyph.SharedSpaces;
using UnityEngine;

public class BaseSpawner : MonoBehaviour
{
    [SerializeField] private GameObject basePrefab;

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

        GameObject newBase = Instantiate(basePrefab, spawnPos, spawnRot);

        newBase.GetComponent<NetworkedSpatialAnchor>().NetworkObject.Spawn();
    }
}
