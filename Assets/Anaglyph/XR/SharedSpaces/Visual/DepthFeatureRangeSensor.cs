using System;
using System.Collections.Generic;
using Anaglyph.XR.DepthKit;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>How far off a depth sensor's reading is likely to be, by how far away it reads.</summary>
	[Serializable]
	public struct DepthSensorError
	{
		[Tooltip("Meters off at any distance")]
		public float floorMeters;
		[Tooltip("Meters off per meter of distance: a time-of-flight sensor's error grows about in step with range")]
		public float perMeter;
		[Tooltip("Meters off per meter of distance, squared: stereo matching's error grows with the square of range")]
		public float perMeterSquared;
		[Tooltip("Readings farther than this are not used")]
		public float maxMeters;

		public readonly float SigmaAt(float meters) => floorMeters + perMeter * meters + perMeterSquared * meters * meters;
	}

	/// <summary>
	/// Ranges a view's features against the device's depth frame, with the doubt that device's kind of
	/// depth has at each distance: a headset's stereo depth falls off quickly past a few meters, a
	/// phone's LiDAR holds much farther, and depth estimated from camera motion is the loosest.
	/// </summary>
	public sealed class DepthFeatureRangeSensor : IFeatureRangeSensor, IDisposable
	{
		[Serializable]
		public sealed class Settings
		{
			[Tooltip("A headset's stereo depth (Quest), and the editor's simulated depth standing in for it")]
			public DepthSensorError stereoHeadset = new() { floorMeters = 0.01f, perMeterSquared = 0.012f, maxMeters = 5f };
			[Tooltip("ARKit scene depth: LiDAR, filled in from the camera image")]
			public DepthSensorError lidar = new() { floorMeters = 0.01f, perMeter = 0.01f, perMeterSquared = 0.003f, maxMeters = 7f };
			[Tooltip("ARCore depth: estimated from camera motion on most phones")]
			public DepthSensorError motionDepth = new() { floorMeters = 0.02f, perMeter = 0.02f, perMeterSquared = 0.01f, maxMeters = 5f };
			[Tooltip("Meters, nearest a reading is used from")]
			public float minMeters = 0.2f;
		}

		private readonly Settings settings;
		private readonly DepthRayCaster caster;
		private readonly List<Ray> rays = new();

		public DepthFeatureRangeSensor(ComputeShader depthRayCast, int capacity, Settings settings)
		{
			this.settings = settings;
			caster = new DepthRayCaster(depthRayCast, capacity);
		}

		/// <summary>The error of the depth this device has.</summary>
		public DepthSensorError Error => HandheldAR.Runtime switch
		{
			HandheldAR.Provider.ARKit => settings.lidar,
			HandheldAR.Provider.ARCore => settings.motionDepth,
			_ => settings.stereoHeadset,
		};

		public async Awaitable<SensedRange[]> SenseAsync(Pose cameraInTrackingSpace, IReadOnlyList<float3> raysInCamera)
		{
			Transform trackingSpace = MainXRRig.TrackingSpace;
			if (!DepthRayCaster.CanCast || trackingSpace == null) return null;

			DepthSensorError error = Error;
			Vector3 origin = trackingSpace.TransformPoint(cameraInTrackingSpace.position);
			Quaternion rotation = trackingSpace.rotation * cameraInTrackingSpace.rotation;
			rays.Clear();
			foreach (float3 ray in raysInCamera) rays.Add(new Ray(origin, rotation * ray));

			caster.RayRange = new Vector2(settings.minMeters, error.maxMeters);
			Vector4[] hits = await caster.CastAsync(rays);
			if (hits == null) return null;

			SensedRange[] ranges = new SensedRange[raysInCamera.Count];
			for (int i = 0; i < ranges.Length && i < hits.Length; i++)
			{
				if (hits[i].w <= 0) continue;
				float meters = Vector3.Distance(origin, hits[i]);
				ranges[i] = new SensedRange { meters = meters, sigma = error.SigmaAt(meters) };
			}

			return ranges;
		}

		public void Dispose() => caster.Dispose();
	}
}
