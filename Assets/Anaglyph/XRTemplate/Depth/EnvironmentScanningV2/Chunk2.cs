using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanningV2
{
	/// <summary>
	/// GameObject for environment scan mesh chunk
	/// </summary>
	public class Chunk2 : MonoBehaviour
	{
		public int chunkIndex;
		public Mesh mesh;
		public bool dirty;

		private MeshFilter meshFilter;

		private void Awake()
		{
			TryGetComponent(out meshFilter);

			mesh = new Mesh();
			mesh.MarkDynamic();
			meshFilter.sharedMesh = mesh;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

#if UNITY_EDITOR

		private void OnDrawGizmos()
		{
			EnvScanner2 s = EnvScanner2.Instance;
			Gizmos.color = Color.grey;
			Gizmos.DrawWireCube(transform.position + s.ChunkSizeHalf, s.ChunkSize);
		}

#endif
	}
}