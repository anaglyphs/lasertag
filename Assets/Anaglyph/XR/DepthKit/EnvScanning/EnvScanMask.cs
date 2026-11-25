using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Renderer drawn into the scanner's depth mask. Nothing behind it, as seen from the
	/// depth sensor, is integrated into the scan. Moved onto the <see cref="LayerName"/>
	/// layer, which cameras exclude, so it's never drawn to the player. Skinned renderers
	/// work, e.g. the local player's arms.
	/// </summary>
	[RequireComponent(typeof(Renderer))]
	public class EnvScanMask : MonoBehaviour
	{
		public const string LayerName = "EnvScanMask";

		public static List<EnvScanMask> All { get; } = new();

		public Renderer Renderer { get; private set; }

		/// <summary>Cached, since <see cref="Renderer.sharedMaterials"/> allocates</summary>
		public int SubMeshCount { get; private set; }

		private void Awake()
		{
			Renderer = GetComponent<Renderer>();
			SubMeshCount = Renderer.sharedMaterials.Length;

			int layer = LayerMask.NameToLayer(LayerName);
			if (layer < 0)
				Debug.LogWarning($"[{nameof(EnvScanMask)}] no '{LayerName}' layer; mask will be visible", this);
			else
				gameObject.layer = layer;

			// skinning is skipped for renderers no camera sees, and no camera sees this layer
			if (Renderer is SkinnedMeshRenderer skinned)
				skinned.updateWhenOffscreen = true;
		}

		private void OnEnable()
		{
			All.Add(this);
		}

		private void OnDisable()
		{
			All.Remove(this);
		}

		/// <summary>
		/// Places a primitive capsule (radius 0.5, height 2) around segment a-b. Shrinks the radius to keep
		/// the depth eye outside, since a capsule seen from inside draws nothing.
		/// </summary>
		public static void FitCapsule(Transform capsule, Vector3 a, Vector3 b, float radius,
			float minClearanceFromEye, Vector3 eyeFallback)
		{
			float eyeToSegment = DistanceToSegment(DepthEyePosition(eyeFallback), a, b);
			radius = Mathf.Clamp(radius, 0.01f, Mathf.Max(0.01f, eyeToSegment - minClearanceFromEye));

			Vector3 ab = b - a;
			float length = ab.magnitude;
			Vector3 dir = length > 0.001f ? ab / length : Vector3.up;
			capsule.SetPositionAndRotation((a + b) / 2, Quaternion.FromToRotation(Vector3.up, dir));
			capsule.localScale = new Vector3(radius * 2, (length + radius * 2) / 2, radius * 2);
		}

		private static Vector3 DepthEyePosition(Vector3 fallback)
		{
			DepthKitDriver depth = DepthKitDriver.Instance;
			return depth && DepthKitDriver.DepthAvailable ? depth.ViewInv[0].GetColumn(3) : fallback;
		}

		private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
		{
			Vector3 ab = b - a;
			float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
			return Vector3.Distance(point, a + ab * t);
		}
	}
}
