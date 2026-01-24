using System;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.XRTemplate;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Anaglyph.DepthKit.Meshing
{
	public class MeshChunk : MonoBehaviour
	{
#if UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static async void StartWithDebugRendering()
		{
			await Awaitable.WaitForSecondsAsync(0.5f);
		}
#endif

		public Vector3 extents = new float3(10, 10, 10);
		private EnvironmentMapper mapper => EnvironmentMapper.Instance;

		private Mesh mesh;
		public bool dirty;

		private bool isPopulated = false;
		public UnityEvent<Mesh> onMeshFirstPopulated = new();

		private void Awake()
		{
			mesh = new Mesh();
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

		private int3 WorldToVoxel(float3 pos)
		{
			int3 volumeSize = mapper.VolDimensions;
			pos /= mapper.VoxSize;
			pos += (float3)volumeSize / 2.0f;

			int3 id = new(pos);
			id = math.clamp(id, 0, volumeSize);
			return id;
		}

		public async Task Mesh(CancellationToken ctkn = default)
		{
			NativeArray<sbyte> volumePiece = default;

			try
			{
				int3 start = WorldToVoxel(transform.position);
				int3 end = start + new int3(extents / mapper.VoxSize) + 1;

				for (int d = 0; d < 3; d++)
				{
					if (start[d] >= mapper.VolDimensions[d])
						return;

					if (end[d] <= 0)
						return;
				}

				start = math.max(start, 0);
				end = math.min(end, mapper.VolDimensions - 1);

				int3 size = end - start;

				AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(
					mapper.Volume, 0,
					start.x, size.x,
					start.y, size.y,
					start.z, size.z);

				if (req.hasError)
					throw new Exception($"GPU readback error");

				ctkn.ThrowIfCancellationRequested();

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

				bool justPopulated = await Mesher.CreateMesh(volumePiece, size, mapper.VoxSize,
					mesh, ctkn);

				if (justPopulated && !isPopulated)
				{
					onMeshFirstPopulated.Invoke(mesh);
					isPopulated = true;
				}
			}
			finally
			{
				if (volumePiece.IsCreated) volumePiece.Dispose();
				dirty = false;
			}
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
		private void OnDrawGizmosSelected()
		{
			Gizmos.color = dirty ? Color.yellow : Color.green;
			Vector3 areaHalf = extents / 2f;
			Gizmos.DrawWireCube(transform.position + areaHalf, extents);
		}
#endif
	}
}