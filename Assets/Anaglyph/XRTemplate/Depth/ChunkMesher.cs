using UnityEngine;
using UnityEngine.Rendering;
using MarchingCubes;

namespace Anaglyph.XRTemplate.DepthKit {

	public class ChunkMesher : MonoBehaviour
	{
		private Chunk chunk;
		private MeshFilter meshFilter;
		private MeshCollider meshCollider;

		private MeshBuilder builder;

		[SerializeField] private ComputeShader meshCompute;
		public float interval = 1.0f;
		

		private void Awake()
		{
			TryGetComponent(out chunk);
			TryGetComponent(out meshFilter);
			TryGetComponent(out meshCollider);
			int s = chunk.Size;

			builder = new(s, s, s, 1000000, meshCompute);
		}

		private void OnEnable()
		{
			MeshLoop();
		}

		

		private async void MeshLoop()
		{


			while(enabled)
			{
				await Awaitable.WaitForSecondsAsync(interval);

				builder.BuildIsosurface(chunk.Volume, 0, chunk.MetersPerVoxel);
				meshFilter.mesh = builder.Mesh;

				//var vBuff = builder.Mesh.GetVertexBuffer(0);
				//var iBuff = builder.Mesh.GetIndexBuffer();

				//var vReq = await AsyncGPUReadback.RequestAsync(vBuff);
				////while (!vReq.done) await Awaitable.NextFrameAsync();
				//var verts = vReq.GetData<Vector3>();

				//int vertexCount = verts.Length / 6;
				//builder.Mesh.SetVertexBufferData(verts, 0, 0, vertexCount, 0, MeshUpdateFlags.DontRecalculateBounds);

				//var iReq = await AsyncGPUReadback.RequestAsync(iBuff);
				////while (!iReq.done) await Awaitable.NextFrameAsync();
				//var tris = iReq.GetData<uint>();
				
				//builder.Mesh.SetIndexBufferData(tris, 0, 0, tris.Length, MeshUpdateFlags.DontRecalculateBounds);

				//meshCollider.sharedMesh = builder.Mesh;
			}
		}

		private void OnDestroy()
		{
			builder.Dispose();
		}
	}
}