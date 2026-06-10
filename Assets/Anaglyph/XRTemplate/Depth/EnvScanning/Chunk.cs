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
		public uint lastMeshingChangeSum;

		public MeshFilter meshFilter;
		public MeshCollider meshCollider;

		private Vector3 worldCenter;

		private void Awake()
		{
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshCollider);
			meshCollider.enabled = false;

			mesh = new Mesh();
			mesh.MarkDynamic();
			meshFilter.sharedMesh = mesh;
			meshCollider.sharedMesh = mesh;
		}

		private void Start()
		{
			worldCenter = transform.position + ChunkManager.ChunkWorldSizeHalf;
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
			Gizmos.DrawWireCube(worldCenter, ChunkManager.ChunkWorldSize);
		}

#endif
	}
}