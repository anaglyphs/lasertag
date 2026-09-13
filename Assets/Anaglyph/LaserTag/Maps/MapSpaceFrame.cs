using System;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Visit-specific mesh identity. Re-entering a frame never accepts its previous scan packets.</summary>
	public struct MapSpaceScanContext
	{
		public Guid space, frame, scan;
		public bool Matches(MapSpaceScanContext other) => space != Guid.Empty && frame != Guid.Empty && scan != Guid.Empty &&
			space == other.space && frame == other.frame && scan == other.scan;
	}

	/// <summary>One conversion boundary for layouts and reference targets; never applied to the rig itself.</summary>
	public readonly struct MapSpaceFrame
	{
		public readonly string StorageId;
		public readonly string CanonicalId;
		public readonly Pose CanonicalFromStorage;
		public readonly Pose StorageFromCanonical;

		public MapSpaceFrame(MapSpace space) : this(space.storageFrameId, space.canonicalFrameId, space.canonicalFromStorage) { }
		public MapSpaceFrame(string storageId, string canonicalId, Pose offset)
		{
			if (!ValidPose(offset)) throw new ArgumentException("Invalid space offset", nameof(offset));
			StorageId = storageId;
			CanonicalId = canonicalId;
			CanonicalFromStorage = offset;
			StorageFromCanonical = Inverse(offset);
		}

		public Pose ToCanonical(Pose stored) => Compose(CanonicalFromStorage, stored);
		public Pose ToStorage(Pose canonical) => Compose(StorageFromCanonical, canonical);
		public static Pose Compose(Pose a, Pose b) => new(a.position + a.rotation * b.position, a.rotation * b.rotation);
		public static Pose Inverse(Pose pose)
		{
			Quaternion rotation = Quaternion.Inverse(pose.rotation);
			return new Pose(rotation * -pose.position, rotation);
		}
		public static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
		public static bool ValidPose(Pose p) => Finite(p.position.x) && Finite(p.position.y) && Finite(p.position.z) &&
			Finite(p.rotation.x) && Finite(p.rotation.y) && Finite(p.rotation.z) && Finite(p.rotation.w) &&
			Mathf.Abs(Quaternion.Dot(p.rotation, p.rotation) - 1f) < .01f;
		public static bool ValidOffset(Pose p) => ValidPose(p) && Vector3.Angle(p.rotation * Vector3.up, Vector3.up) < .1f;
		public static bool Near(Pose a, Pose b, float meters = .02f, float degrees = 1f) =>
			Vector3.Distance(a.position, b.position) <= meters && Quaternion.Angle(a.rotation, b.rotation) <= degrees;
	}
}
