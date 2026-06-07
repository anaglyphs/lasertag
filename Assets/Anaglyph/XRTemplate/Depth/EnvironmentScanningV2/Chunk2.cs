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
		public bool undecimated;

		private MeshFilter meshFilter;
		private MeshCollider meshCollider;

		private void Awake()
		{
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshCollider);

			mesh = new Mesh();
			mesh.MarkDynamic();
			meshFilter.sharedMesh = mesh;
			meshCollider.sharedMesh = mesh;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

#if UNITY_EDITOR

		private void OnDrawGizmos()
		{
			if (dirty)
			{
				EnvScanner2 s = EnvScanner2.Instance;
				Gizmos.color = Color.yellow;
				DrawChunkFrame();
			}
		}

		private void OnDrawGizmosSelected()
		{
			if (dirty) return;

			Gizmos.color = Color.aliceBlue;
			DrawChunkFrame();
		}

		private void DrawChunkFrame()
		{
			EnvScanner2 s = EnvScanner2.Instance;
			if (!s) return;
			Gizmos.DrawWireCube(transform.position + s.ChunkSizeHalf, s.ChunkSize);
		}

#endif
	}
}