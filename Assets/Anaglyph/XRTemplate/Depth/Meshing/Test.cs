using Anaglyph.XRTemplate;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class Test : MonoBehaviour
	{
		[SerializeField] private EnvironmentMapper mapper;
		[SerializeField] private MeshFilter meshFilter;

		private Mesh mesh;
		
		private Mesher mesher = new();

		private void Start()
		{
			mesh = new Mesh();
			meshFilter.mesh = mesh;

			IntegrateLoop();
		}

		private async void IntegrateLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(0.1f);
				
				AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(mapper.Volume);

				int sliceSize = req.width * req.height;
				
				NativeArray<sbyte> volume = new(sliceSize * req.depth, Allocator.TempJob);

				for (int z = 0; z < req.layerCount; z++)
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

				await mesher.CreateMesh(volume, new int3(req.width, req.height, req.depth), mapper.MetersPerVoxel, mesh);
				
				volume.Dispose();
			}
		}
		
		[BurstCompile]
		private struct CopySliceJob : IJobFor
		{
			[ReadOnly] public NativeArray<sbyte> Source;
			public int DestOffset;
			
			[NativeDisableParallelForRestriction]
			[WriteOnly] public NativeArray<sbyte> Destination;

			public void Execute(int i)
			{
				Destination[DestOffset + i] = Source[i];
			}
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}
	}
}