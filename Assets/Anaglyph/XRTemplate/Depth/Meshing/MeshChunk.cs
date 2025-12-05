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
		private static bool debugRendering = false;
		private static event Action<bool> DebugRenderingChanged = delegate { };

		public static void SetDebugRenderingEnabled(bool b)
		{
			debugRendering = b;
			DebugRenderingChanged.Invoke(b);
		}

#if UNITY_EDITOR
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static async void StartWithDebugRendering()
		{
			await Awaitable.WaitForSecondsAsync(0.5f);
			SetDebugRenderingEnabled(true);
		}
#endif

		public Vector3 extents = new float3(10, 10, 10);
		private EnvironmentMapper mapper => EnvironmentMapper.Instance;

		[SerializeField] private MeshRenderer meshRenderer;
		[SerializeField] private MeshFilter meshFilter;
		[SerializeField] private MeshCollider meshCollider;

		[SerializeField] private Material occlusionMaterial;
		[SerializeField] private Material debugMaterial;

		private Mesh mesh;

		private CancellationTokenSource ctkn;

		public bool dirty;

		private void Awake()
		{
			mesh = new Mesh();

			DebugRenderingChanged += OnDebugRenderingStateChanged;
			OnDebugRenderingStateChanged(debugRendering);
		}

		private void OnDestroy()
		{
			Destroy(mesh);
			DebugRenderingChanged -= OnDebugRenderingStateChanged;
		}

		private void OnDebugRenderingStateChanged(bool b)
		{
			meshRenderer.material = debugRendering ? debugMaterial : occlusionMaterial;
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
				int3 size = new int3(extents / mapper.MetersPerVoxel) + 1;

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

			if (volumePiece.IsCreated) volumePiece.Dispose();

			bool meshExists = mesh.GetIndexCount(0) > 0;

			if (meshExists)
			{
				meshFilter.sharedMesh = mesh;
				meshCollider.sharedMesh = mesh;
			}

			meshCollider.enabled = meshExists;
			dirty = false;
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