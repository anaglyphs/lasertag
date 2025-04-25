using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
    public class ChunkInstantiator : MonoBehaviour
    {
        [SerializeField] private Chunk chunkPrefab;
        [SerializeField] private Vector3Int num;

		private void Awake()
		{
			for(int x = 0; x < num.x; x++) 
				for(int y = 0; y < num.y; y++)
					for(int z = 0; z < num.z; z++)
					{
						Vector3 pos = new(x, y, z);
						pos += 0.5f * Vector3.one;
						pos -= new Vector3(num.x, num.y, num.z) / 2f;

						pos *= chunkPrefab.MetersPerVoxel * (chunkPrefab.Size - 1);

						Instantiate(chunkPrefab.gameObject, pos, Quaternion.identity);
					}
						
		}
	}
}
