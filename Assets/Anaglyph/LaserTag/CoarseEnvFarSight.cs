using System;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Extends the <see cref="CoarseEnvMap"/> past the depth sensor's reach. The network
	/// behind <see cref="SkyView"/> estimates distance only up to a factor, so each look is
	/// scaled to meters by the cells the sensor measured too, and what the look puts beyond
	/// the sensor's reach is added to the map as rays
	/// </summary>
	public class CoarseEnvFarSight : MonoBehaviour
	{
		[Tooltip("The network's distances count as surfaces out to this. A farther one, or sky, only says nothing is nearer")]
		[SerializeField] private float farthestTrustedMeters = 20f;
		[Tooltip("Fewest cells of a look the depth sensor must have measured for the look to be scaled")]
		[SerializeField] private int fewestMeasuredCells = 35;
		[Tooltip("A look is dropped when network and sensor differ by more than this factor over half the cells both measured: a look that is wrong up close is worse far away")]
		[SerializeField] private float largestTypicalDisagreement = 1.2f;
		[Tooltip("Rays through each cell of the network's map, per side: enough that one look hits a far wall a few times per map cell")]
		[SerializeField] private int raysPerCellSide = 2;
		[Tooltip("A cell with this much sky in it is open")]
		[SerializeField, Range(0f, 1f)] private float skyShareForOpen = 0.5f;

		private float[] measuredLogDepthSum, sorted;
		private int[] measuredPointCount;
		private NativeArray<float4> rayEnds;
		private SkyView subscribedTo;

		public int LooksAdded { get; private set; }
		/// <summary>Looks the sensor measured too little of to scale.</summary>
		public int LooksUnscaled { get; private set; }
		/// <summary>Looks dropped for disagreeing with the sensor.</summary>
		public int LooksDisagreeing { get; private set; }
		/// <summary>The factor network and sensor typically differed by in the latest scaled look.</summary>
		public float LatestTypicalDisagreement { get; private set; }

		private void Start()
		{
			subscribedTo = SkyView.Instance;
			if (subscribedTo != null) subscribedTo.LookedWithDistance += OnLookedWithDistance;
		}

		private void OnDestroy()
		{
			if (subscribedTo != null) subscribedTo.LookedWithDistance -= OnLookedWithDistance;
			if (rayEnds.IsCreated) rayEnds.Dispose();
		}

		private void OnLookedWithDistance(ReadOnlySpan<float> skyShare, ReadOnlySpan<float> logNearness, int cellsPerSide,
			Pose camera, PinholeCamera intrinsics, float4 content)
		{
			CoarseEnvScanner scanner = CoarseEnvScanner.Instance;
			if (!isActiveAndEnabled || scanner == null || scanner.Map == null || !scanner.PoseIsInMapFrame) return;

			AddLook(scanner.Map, scanner.LatestRayEnds, scanner.TrustedMeters, skyShare, logNearness, cellsPerSide,
				camera, intrinsics, content);
		}

		/// <summary>
		/// Scales one look to meters and adds what it shows past the sensor's reach to
		/// <paramref name="map"/>. Nothing is added when the sensor measured too little of
		/// the look, or disagrees with it
		/// </summary>
		/// <param name="measured">World points the depth sensor last saw, w = 0 where it had no reading.</param>
		/// <param name="sensorTrustedMeters">Nearest and farthest of those points that count.</param>
		public void AddLook(CoarseEnvMap map, ReadOnlySpan<float4> measured, Vector2 sensorTrustedMeters,
			ReadOnlySpan<float> skyShare, ReadOnlySpan<float> logNearness, int cellsPerSide,
			Pose camera, PinholeCamera intrinsics, float4 content)
		{
			int cellCount = cellsPerSide * cellsPerSide;
			if (sorted == null || sorted.Length != cellCount)
			{
				measuredLogDepthSum = new float[cellCount];
				measuredPointCount = new int[cellCount];
				sorted = new float[cellCount];
			}

			Array.Clear(measuredLogDepthSum, 0, cellCount);
			Array.Clear(measuredPointCount, 0, cellCount);

			Quaternion worldToCamera = Quaternion.Inverse(camera.rotation);
			float2 contentSize = content.zw - content.xy;
			foreach (float4 point in measured)
			{
				if (point.w == 0) continue;
				float3 inCamera = worldToCamera * ((Vector3)point.xyz - camera.position);
				float meters = math.length(inCamera);
				// written so NaN fails the test
				if (!(meters >= sensorTrustedMeters.x && meters <= sensorTrustedMeters.y)) continue;
				if (!intrinsics.Project(inCamera, out float2 pixel)) continue;

				float2 acrossFrame = (pixel + 0.5f - content.xy) / contentSize;
				if (math.any(acrossFrame < 0) || math.any(acrossFrame >= 1)) continue;
				int2 cell = (int2)(acrossFrame * cellsPerSide);
				int index = cell.y * cellsPerSide + cell.x;
				measuredLogDepthSum[index] += math.log(inCamera.z);
				measuredPointCount[index]++;
			}

			// log depth = logMetersPerUnit - logNearness in every cell; each measured cell says what logMetersPerUnit is
			int measuredCells = 0;
			for (int cell = 0; cell < cellCount; cell++)
				if (measuredPointCount[cell] > 0)
					sorted[measuredCells++] = measuredLogDepthSum[cell] / measuredPointCount[cell] + logNearness[cell];

			if (measuredCells < fewestMeasuredCells)
			{
				LooksUnscaled++;
				return;
			}

			float logMetersPerUnit = MiddleOf(sorted, measuredCells);
			for (int i = 0; i < measuredCells; i++) sorted[i] = math.abs(sorted[i] - logMetersPerUnit);
			LatestTypicalDisagreement = math.exp(MiddleOf(sorted, measuredCells));
			if (LatestTypicalDisagreement > largestTypicalDisagreement)
			{
				LooksDisagreeing++;
				return;
			}

			int raysPerSide = cellsPerSide * raysPerCellSide;
			if (!rayEnds.IsCreated || rayEnds.Length != raysPerSide * raysPerSide)
			{
				if (rayEnds.IsCreated) rayEnds.Dispose();
				rayEnds = new NativeArray<float4>(raysPerSide * raysPerSide, Allocator.Persistent);
			}

			// longer than the map trusts, so the ray only passes
			float openMeters = farthestTrustedMeters * 2f;
			for (int rayY = 0; rayY < raysPerSide; rayY++)
			for (int rayX = 0; rayX < raysPerSide; rayX++)
			{
				int ray = rayY * raysPerSide + rayX;
				int cell = rayY / raysPerCellSide * cellsPerSide + rayX / raysPerCellSide;
				// the sensor knows a cell it measured better
				if (measuredPointCount[cell] > 0)
				{
					rayEnds[ray] = default;
					continue;
				}

				float2 acrossFrame = (new float2(rayX, rayY) + 0.5f) / raysPerSide;
				float3 direction = intrinsics.Ray(content.xy + acrossFrame * contentSize - 0.5f);
				bool open = skyShare[cell] >= skyShareForOpen;
				float meters = open ? openMeters : math.exp(logMetersPerUnit - logNearness[cell]) / direction.z;
				Vector3 end = camera.position + camera.rotation * (Vector3)(direction * meters);
				rayEnds[ray] = new float4(end, 1);
			}

			map.AddRays(camera.position, rayEnds, sensorTrustedMeters.y, farthestTrustedMeters);
			LooksAdded++;
		}

		private static float MiddleOf(float[] values, int count)
		{
			Array.Sort(values, 0, count);
			return values[count / 2];
		}
	}
}
