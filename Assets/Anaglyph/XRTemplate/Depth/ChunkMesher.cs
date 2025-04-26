using UnityEngine;
using MarchingCubes;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate.DepthKit {

	public class ChunkMesher : MonoBehaviour
	{
		private Chunk chunk;
		private MeshFilter meshFilter;
		private MeshCollider meshCollider;

		private MeshBuilder builder;

		[SerializeField] private bool updateColliders;
		[SerializeField] private ComputeShader meshCompute;

		bool shouldUpdate = false;
		public bool ShouldUpdate => shouldUpdate;
		public int triBudget = 1000;

		private void Awake()
		{
			TryGetComponent(out chunk);
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshCollider);

			chunk.OnIntegrate += delegate
			{
				shouldUpdate = true;
			};

			Mapper.meshers.Add(this);
		}

		private void OnDestroy()
		{
			Mapper.meshers.Remove(this);
		}


#if UNITY_EDITOR

		private static readonly Color g = new Color(0, 1, 0, 0.1f);

		private void OnDrawGizmos()
		{
			if (!shouldUpdate)
				return;

			// Gizmos.color = shouldUpdate ? Color.green : Color.black;
			
			Gizmos.color = g;
			Gizmos.DrawWireCube(chunk.Bounds.center, chunk.Bounds.size);
		}
#endif

		public async void BuildMesh()
		{
			if (!shouldUpdate)
				return;

			shouldUpdate = false;

			if (builder == null)
			{
				int s = chunk.Size;
				builder = new(s, s, s, triBudget, meshCompute);
			}

			builder.BuildIsosurface(chunk.Volume, 0.1f, chunk.MetersPerVoxel);
			meshFilter.sharedMesh = builder.Mesh;


			var vBuff = builder.Mesh.GetVertexBuffer(0);
			var iBuff = builder.Mesh.GetIndexBuffer();

			int vertexCount = 3 * triBudget;

			var vReq = await AsyncGPUReadback.RequestAsync(vBuff);
			var verts = vReq.GetData<Vector3>();

			if (this == null)
				return;

			builder.Mesh.SetVertexBufferData(verts, 0, 0, verts.Length, 0, MeshUpdateFlags.DontRecalculateBounds);

			var iReq = await AsyncGPUReadback.RequestAsync(iBuff);
			var tris = iReq.GetData<uint>();

			if (this == null)
				return;

			builder.Mesh.SetIndexBufferData(tris, 0, 0, tris.Length, MeshUpdateFlags.DontRecalculateBounds);

			// builder.Mesh.RecalculateBounds();
			builder.Mesh.UploadMeshData(false);

			if (updateColliders)
				meshCollider.sharedMesh = builder.Mesh;
		}
	}
}