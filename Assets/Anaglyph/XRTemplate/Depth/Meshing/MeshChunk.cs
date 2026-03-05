using System;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.XRTemplate;
using Meshia.MeshSimplification;
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
	// mesh & chunk origin are at bottom back left origin and extend along positive axiis
	public class MeshChunk : MonoBehaviour, IDisposable
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
		private NativeArray<sbyte> volume;

		private bool isPopulated = false;
		public bool IsPopulated => isPopulated;

		[FormerlySerializedAs("onMeshFirstPopulated")]
		public UnityEvent<Mesh> onMeshPopulated = new();

		private readonly NetMesher mesher = new();

		[Header("Mesh decimation options")] public MeshSimplificationTarget decimationTarget = new()
		{
			Kind = MeshSimplificationTargetKind.ScaledTotalError,
			Value = 0.5f
		};

		public MeshSimplifierOptions decimationOptions = new()
		{
			EnableSmartLink = false,
			MinNormalDot = 0.8f,
			PreserveBorderEdges = true,
			PreserveSurfaceCurvature = false,
			UseBarycentricCoordinateInterpolation = false,
			VertexLinkDistance = 0.0001f,
			VertexLinkMinNormalDot = 0.95f,
			VertexLinkColorDistance = 0.01f,
			VertexLinkUvDistance = 0.001f
		};

		private void Awake()
		{
			mesh = new Mesh();
			mesh.MarkDynamic();
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}

		private float3 WorldToVoxelFloat(float3 pos)
		{
			pos /= mapper.VoxelSize;
			pos += (float3)mapper.VoxelCount / 2.0f;
			return pos;
		}

		private int3 WorldToVoxel(float3 pos)
		{
			pos = WorldToVoxelFloat(pos);

			int3 id = new(math.floor(pos));
			id = math.clamp(id, 0, mapper.VoxelCount);
			return id;
		}

		public async Task Mesh(CancellationToken ctkn = default)
		{
			try
			{
				int3 start = WorldToVoxel(transform.position);
				int3 end = start + new int3(extents / mapper.VoxelSize);

				for (int d = 0; d < 3; d++)
				{
					if (start[d] >= mapper.VoxelCount[d])
						return;

					if (end[d] <= 0)
						return;
				}

				start = math.max(start, 0);
				end = math.min(end, mapper.VoxelCount - 1);

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

				if (!volume.IsCreated || volume.Length < sliceSize * req.depth)
				{
					if (volume.IsCreated)
						volume.Dispose();

					volume = new NativeArray<sbyte>(sliceSize * req.depth, Allocator.Persistent);
				}

				for (int z = 0; z < size.z; z++)
				{
					NativeArray<sbyte> slice = req.GetData<sbyte>(z);
					int dstOffset = z * req.width * req.height;

					CopySliceJob copier = new()
					{
						Source = slice,
						Destination = volume,
						DestOffset = dstOffset
					};
					copier.ScheduleParallelByRef(sliceSize, 16, default).Complete();
				}

				isPopulated = await mesher.CreateMesh(volume, size, mapper.VoxelSize, mesh, ctkn);

				ctkn.ThrowIfCancellationRequested();

				onMeshPopulated.Invoke(mesh);
				mesh.MarkModified();
			}
			finally
			{
				dirty = false;
			}
		}

		public async Task Decimate(CancellationToken ctkn = default)
		{
			if (isPopulated)
				await MeshSimplifier.SimplifyAsync(mesh, decimationTarget, decimationOptions, mesh, ctkn);
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
		public void Dispose()
		{
			volume.Dispose();
			mesher?.Dispose();
		}
	}
}