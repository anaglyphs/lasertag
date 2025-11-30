using System;
using Anaglyph.XRTemplate;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.DepthKit
{
	public class Test : MonoBehaviour
	{
		[SerializeField] private EnvironmentMapper mapper;
		[SerializeField] private Marcher marcher;
		[SerializeField] private MeshFilter meshFilter;

		private Mesh mesh;

		private void Start()
		{
			marcher.metersPerVoxel = mapper.MetersPerVoxel;
			marcher.voxelCount = (uint3)new int3(mapper.vWidth, mapper.vHeight, mapper.vDepth);

			mapper.Integrated += OnIntegrate;

			mesh = new Mesh();
		}

		private bool busy = false;

		private async void OnIntegrate()
		{
			if (busy) return;
			busy = true;
			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(mapper.Volume);

			sbyte[] full = new sbyte[req.width * req.height * req.depth];
			int sliceSize = req.width * req.height;

			for (int z = 0; z < req.depth; z++)
			{
				NativeArray<sbyte> slice = req.GetData<sbyte>(z);

				slice.ToArray().CopyTo(full, z * sliceSize);
			}

			marcher.data = full;
			marcher.TriangulateVoxelRange(new uint3(0, 0, 0), new uint3(64, 64, 64), mesh);
			meshFilter.mesh = mesh;
			busy = false;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}
	}
}