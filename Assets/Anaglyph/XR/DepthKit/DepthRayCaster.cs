using System;
using System.Collections.Generic;
using Anaglyph.GPU;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit
{
	/// <summary>
	/// Finds where world-space rays first meet the surface in the current depth frame. Rays the
	/// depth sensor does not see, or that only slip behind an object's edge, find nothing.
	/// </summary>
	public sealed class DepthRayCaster : IDisposable
	{
		private static readonly int rayCountID = Shader.PropertyToID("rayCount");
		private static readonly int rayRangeID = Shader.PropertyToID("rayRange");
		private static readonly int surfaceToleranceID = Shader.PropertyToID("surfaceTolerance");
		private static readonly int rayOriginsID = Shader.PropertyToID("rayOrigins");
		private static readonly int rayDirectionsID = Shader.PropertyToID("rayDirections");
		private static readonly int rayHitsID = Shader.PropertyToID("rayHits");

		private readonly ComputeShader compute;
		private readonly ComputeKernel castKernel;
		private readonly int capacity;
		private readonly ComputeBuffer origins, directions, hits;
		private readonly Vector4[] originData, directionData;

		/// <summary>Meters along each ray searched: nearest, farthest.</summary>
		public Vector2 RayRange { get; set; } = new(0.2f, 8f);

		/// <summary>Meters a hit may sit off the surface: constant, and per meter of range.</summary>
		public Vector2 SurfaceTolerance { get; set; } = new(0.03f, 0.03f);

		public DepthRayCaster(ComputeShader compute, int capacity)
		{
			this.compute = compute;
			this.capacity = capacity;
			castKernel = new ComputeKernel(compute, "CastRays");
			origins = new ComputeBuffer(capacity, sizeof(float) * 4);
			directions = new ComputeBuffer(capacity, sizeof(float) * 4);
			hits = new ComputeBuffer(capacity, sizeof(float) * 4);
			originData = new Vector4[capacity];
			directionData = new Vector4[capacity];
		}

		public static bool CanCast => DepthKitDriver.DepthAvailable && DepthKitDriver.Instance != null && DepthKitDriver.Instance.DepthTex != null;

		/// <summary>
		/// One world-space hit per ray, at most <c>capacity</c> rays, w = 1 where it met a surface.
		/// Null when there is no depth frame to cast against.
		/// </summary>
		public async Awaitable<Vector4[]> CastAsync(IReadOnlyList<Ray> rays)
		{
			if (!CanCast || origins == null || !origins.IsValid()) return null;

			int count = Mathf.Min(rays.Count, capacity);
			if (count == 0) return Array.Empty<Vector4>();
			for (int i = 0; i < count; i++)
			{
				originData[i] = rays[i].origin;
				directionData[i] = rays[i].direction;
			}

			DepthKitDriver depth = DepthKitDriver.Instance;
			origins.SetData(originData, 0, 0, count);
			directions.SetData(directionData, 0, 0, count);
			compute.SetInt(rayCountID, count);
			compute.SetVector(rayRangeID, RayRange);
			compute.SetVector(surfaceToleranceID, SurfaceTolerance);
			compute.SetMatrixArray(DepthKitDriver.projID, depth.Proj);
			compute.SetMatrixArray(DepthKitDriver.viewID, depth.View);
			castKernel.Bind(DepthKitDriver.depthTexID, depth.DepthTex);
			castKernel.Bind(rayOriginsID, origins);
			castKernel.Bind(rayDirectionsID, directions);
			castKernel.Bind(rayHitsID, hits);
			castKernel.DispatchFit(count, 1);

			AsyncGPUReadbackRequest readback = await AsyncGPUReadback.RequestAsync(hits, count * sizeof(float) * 4, 0);
			if (readback.hasError || !hits.IsValid()) return null;
			return readback.GetData<Vector4>().ToArray();
		}

		public void Dispose()
		{
			origins?.Release();
			directions?.Release();
			hits?.Release();
		}
	}
}
