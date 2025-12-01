using System;
using Anaglyph.XRTemplate;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Anaglyph.DepthKit
{
	public class Test : MonoBehaviour
	{
		[SerializeField] private EnvironmentMapper mapper;
		[SerializeField] private MeshFilter meshFilter;

		[SerializeField] public int3 start;
		[SerializeField] public int3 end;

		private Mesh mesh;
		
		MesherPrototype mesher = new ();

		private void Start()
		{
			mesher.metersPerVoxel = mapper.MetersPerVoxel;
			mesher.voxelCount = new int3(mapper.vWidth, mapper.vHeight, mapper.vDepth);

			mesh = new Mesh();

			IntegrateLoop();
		}

		private async void IntegrateLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(1);
				
				AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(mapper.Volume);

				sbyte[] full = new sbyte[req.width * req.height * req.depth];
				int sliceSize = req.width * req.height;

				for (int z = 0; z < req.layerCount; z++)
				{
					NativeArray<sbyte> slice = req.GetData<sbyte>(z);

					slice.ToArray().CopyTo(full, z * sliceSize);
				}

				mesher.Data = full;
				mesher.BuildMesh(start, end, mesh);
				meshFilter.mesh = mesh;
			}
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}
	}
}