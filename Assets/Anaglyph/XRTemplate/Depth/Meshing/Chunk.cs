using System;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.XRTemplate;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit.Meshing
{
	public class MeshChunk : MonoBehaviour
	{
		public Vector3 extents = new float3(10, 10, 10);
		private EnvironmentMapper mapper => EnvironmentMapper.Instance;

		private MeshFilter meshFilter;
		private Mesh mesh;

		private CancellationTokenSource ctkn;
		
		private void Awake()
		{
			TryGetComponent(out meshFilter);
			mesh = new Mesh();
			meshFilter.sharedMesh = mesh;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

		private int3 WorldToVoxel(float3 pos)
		{
			int3 volumeSize = new(mapper.vWidth, mapper.vHeight, mapper.vDepth);
			pos /= mapper.MetersPerVoxel;
			pos += (float3)volumeSize / 2.0f;

			int3 id = new(pos);
			id = math.clamp(id, 0, volumeSize);
			return id;
		}

		public async Task Mesh()
		{
			ctkn?.Cancel();
			ctkn = new CancellationTokenSource();

			NativeArray<sbyte> volumePiece = default;

			try
			{
				int3 size = new(extents / mapper.MetersPerVoxel);

				int3 start = WorldToVoxel(transform.position);

				AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(
					mapper.Volume, 0,
					start.x, size.x,
					start.y, size.y,
					start.z, size.z);

				if (req.hasError)
					throw new Exception($"GPU readback error");

				ctkn.Token.ThrowIfCancellationRequested();

				int sliceSize = size.x * size.y;

				volumePiece = new NativeArray<sbyte>(sliceSize * req.depth, Allocator.TempJob);

				for (int z = 0; z < size.z; z++)
				{
					NativeArray<sbyte> slice = req.GetData<sbyte>(z);
					int dstOffset = z * req.width * req.height;

					CopySliceJob copier = new()
					{
						Source = slice,
						Destination = volumePiece,
						DestOffset = dstOffset
					};
					copier.ScheduleParallelByRef(sliceSize, 16, default).Complete();
				}

				await Mesher.CreateMesh(volumePiece, size, mapper.MetersPerVoxel,
					mesh);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			if(volumePiece.IsCreated) volumePiece.Dispose();
		}

		[BurstCompile]
		private struct CopySliceJob : IJobFor
		{
			[ReadOnly] public NativeArray<sbyte> Source;
			public int DestOffset;

			[NativeDisableParallelForRestriction] [WriteOnly]
			public NativeArray<sbyte> Destination;

			public void Execute(int i)
			{
				Destination[DestOffset + i] = Source[i];
			}
		}

#if UNITY_EDITOR
		private void OnDrawGizmos()
		{
			Gizmos.color = Color.green;
			Vector3 areaHalf = extents / 2f;
			Gizmos.DrawWireCube(transform.position + areaHalf, extents);
		}
#endif
	}
}