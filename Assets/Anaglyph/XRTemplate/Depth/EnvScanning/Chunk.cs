using Unity.Collections;
using UnityEngine;
using Utilities.XR;

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
			worldCenter = transform.position + EnvMesher.ChunkWorldSizeHalf;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

		public void ApplyMeshData(NativeArray<Vector3> positions, NativeArray<int> indices)
		{
			bool isPopulated = indices.Length >= 3;

			mesh.Clear();

			if (isPopulated)
			{
				mesh.SetVertices(positions);
				mesh.SetIndices(indices, MeshTopology.Triangles, 0);
				mesh.RecalculateNormals();

				meshCollider.sharedMesh = mesh;
			}

			meshCollider.enabled = isPopulated;
		}

		private void Update()
		{
			if (AnaglyphDebugging.DebugMode)
				DrawDebug();
		}

		private void DrawDebug()
		{
			if (dirty)
				DrawChunkFrame(Color.yellow);
		}

		private void DrawChunkFrame(Color color)
		{
			XRGizmos.DrawWireCube(worldCenter, Quaternion.identity, EnvMesher.ChunkWorldSize,
				color);
		}

#if UNITY_EDITOR

		private void OnDrawGizmosSelected()
		{
			DrawChunkFrame(Color.aliceBlue);
		}

#endif
	}
}