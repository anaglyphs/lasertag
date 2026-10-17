using Anaglyph.Debugging;
using Unity.Collections;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.XR.DepthKit.EnvScanning
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
		internal uint pendingMeshingChangeSum;
		public bool meshIsPopulated;
		
		public NativeArray<uint> voxelSignBits;

		public MeshFilter meshFilter;
		public MeshCollider meshCollider;

		// drawn in place of mesh while the debug mesh is shown; see WireframeMesh
		private Mesh wireframeMesh;

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
			if (wireframeMesh) Destroy(wireframeMesh);

			if (voxelSignBits.IsCreated)
				voxelSignBits.Dispose();
		}

		public void ApplyMeshData(NativeArray<Vector3> positions, NativeArray<int> indices)
		{
			bool isPopulated = indices.Length >= 3;

			if(mesh == null)
				mesh = new Mesh();
			else
				mesh.Clear();

			if (isPopulated)
			{
				mesh.SetVertices(positions);
				mesh.SetIndices(indices, MeshTopology.Triangles, 0);
				mesh.RecalculateNormals();

				meshCollider.sharedMesh = mesh;
			}
			
			meshIsPopulated = isPopulated;
			meshCollider.enabled = meshIsPopulated;
			RefreshDisplayMesh();
		}

		/// <summary>
		/// Rebuilds the drawn mesh from <see cref="mesh"/>: its wireframe copy while the
		/// debug mesh is shown, the mesh itself (and no copy kept) otherwise
		/// </summary>
		public void RefreshDisplayMesh()
		{
			bool wireframe = EnvMesher.Instance != null && EnvMesher.Instance.ChunksVisible;
			if (wireframe)
			{
				if (!wireframeMesh)
				{
					wireframeMesh = new Mesh { name = "Chunk Wireframe" };
					wireframeMesh.MarkDynamic();
				}
				WireframeMesh.Build(mesh, wireframeMesh);
				meshFilter.sharedMesh = wireframeMesh;
			}
			else
			{
				meshFilter.sharedMesh = mesh;
				if (wireframeMesh) Destroy(wireframeMesh);
				wireframeMesh = null;
			}
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
