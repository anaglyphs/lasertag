using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>How far along a ray a depth sensor found the scene, and how sure the sensor is at that distance. Zero meters: nothing found.</summary>
	public struct SensedRange
	{
		public float meters;
		/// <summary>One sigma of <see cref="meters"/>.</summary>
		public float sigma;

		public readonly bool IsKnown => meters > 0 && sigma > 0;
	}

	/// <summary>
	/// A depth sensor, asked how far the scene is along a view's feature rays. A landmark ranged this
	/// way is placed from its first two views, however little the camera moved between them: turning
	/// on the spot ranges nothing by triangulation.
	/// </summary>
	public interface IFeatureRangeSensor
	{
		/// <summary>One range per ray, in order, or null with no depth to read. Rays are unit, in the camera's Unity axes.</summary>
		Awaitable<SensedRange[]> SenseAsync(Pose cameraInTrackingSpace, IReadOnlyList<float3> raysInCamera);
	}
}
