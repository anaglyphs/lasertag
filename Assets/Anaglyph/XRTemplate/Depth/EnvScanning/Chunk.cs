using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanning
{
	/// <summary>
	/// GameObject for environment scan mesh chunk
	/// </summary>
	public class Chunk : MonoBehaviour
	{
		public int chunkIndex;
		public Mesh mesh;
		public bool dirty;
		public bool undecimated;

		public MeshFilter meshFilter;
		public MeshCollider meshCollider;

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
				EnvScanner s = EnvScanner.Instance;
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
			EnvScanner s = EnvScanner.Instance;
			if (!s) return;
			Gizmos.DrawWireCube(transform.position + s.ChunkSizeHalf, s.ChunkSize);
		}

#endif
	}
}