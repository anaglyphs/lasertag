using System;
using Anaglyph.XRTemplate;
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
			marcher.data = req.GetData<float>().ToArray();
			marcher.TriangulateVoxelRange(new uint3(0, 0, 0), new uint3(50, 50, 50), mesh);
			meshFilter.sharedMesh = mesh;
			busy = false;
		}

		private void OnDestroy()
		{
			Destroy(mesh);
		}
	}
}