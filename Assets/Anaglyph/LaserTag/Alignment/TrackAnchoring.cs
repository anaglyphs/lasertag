using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Where visual keyframes hang: the track this headset walks, cut into short segments.
	/// Keyframes are stored in the shared frame as the alignment showed it when they were
	/// captured, and never move; a segment only records how far along the track it began
	/// and which site correction was in force, so two keyframes' expected drift apart is the
	/// distance walked between them plus any correction stepped in meanwhile. Consecutive
	/// views fall within the trust window and triangulate together; the same place after a
	/// long walk, or across a correction step, does not, and is a revisit to measure.
	/// </summary>
	public sealed class TrackAnchoring : IKeyframeAnchoring
	{
		/// <summary>Head travel per segment, meters.</summary>
		public float SegmentLength = 1f;
		/// <summary>Expected tracking drift per meter walked.</summary>
		public float DriftPerMeter = 0.01f;
		/// <summary>A correction change this large starts a new segment: views either side of it were placed differently.</summary>
		public float StepTranslation = 0.01f;
		public float StepYawDegrees = 0.1f;

		private struct Segment
		{
			public float path;
			public float steps;
			public int generation;
			public Pose4 correction;
		}

		private readonly Dictionary<ulong, Segment> segments = new();
		private ulong current;
		private ulong nextId = 1;
		private float path, steps;

		/// <summary>Head travel so far, meters: <see cref="HeadPoseHistory.Path"/>, the odometer the colocator ages constraints by.</summary>
		public float Path => path;

		/// <summary>The segment keyframes are anchored to now; 0 when not aligned.</summary>
		public ulong Current => current;

		public bool IsTracking => current != 0;

		/// <summary>The site correction keyframes of this segment were placed with.</summary>
		public Pose4 CorrectionOf(ulong segment) => segments.TryGetValue(segment, out Segment s) ? s.correction : Pose4.Identity;

		/// <summary>Head travel at the start of this segment.</summary>
		public float PathOf(ulong segment) => segments.TryGetValue(segment, out Segment s) ? s.path : path;

		public void Clear()
		{
			segments.Clear();
			current = 0;
		}

		/// <summary>Follows the head and the provider's alignment. Call once per frame.</summary>
		public void Update(CanonColocationProvider provider)
		{
			if (MainXRRig.Instance == null || provider == null) return;
			if (HeadPoseHistory.Instance != null) path = HeadPoseHistory.Instance.Path;

			if (provider.CurrentPhase != CanonColocationProvider.Phase.Aligned)
			{
				current = 0;
				return;
			}

			Pose4 correction = provider.SiteCorrection;
			if (current == 0 || !segments.TryGetValue(current, out Segment s) || s.generation != provider.AlignmentGeneration)
			{
				Begin(provider.AlignmentGeneration, correction);
				return;
			}

			// a correction step is drift between the views either side of it, like a walk
			float stepped = StepSize(s.correction, correction);
			if (stepped > 0 || path - s.path >= SegmentLength)
			{
				steps += stepped;
				Begin(s.generation, correction);
			}
		}

		// how far a correction change moves things near the head, meters (yaw at a 1 m lever)
		private float StepSize(Pose4 from, Pose4 to)
		{
			float3 head = MainXRRig.Camera.transform.position;
			float moved = math.distance(from.Apply(head), to.Apply(head));
			float turned = math.abs(Pose4.WrapAngle(to.yaw - from.yaw));
			return moved >= StepTranslation || math.degrees(turned) >= StepYawDegrees ? moved + turned : 0;
		}

		private void Begin(int generation, Pose4 correction)
		{
			current = nextId++;
			segments[current] = new Segment { path = path, steps = steps, generation = generation, correction = correction };
		}

		// ---- IKeyframeAnchoring ------------------------------------------------

		/// <summary>The camera in the shared frame through the current segment's correction; node identity.</summary>
		public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
		{
			node = 0;
			cameraInNode = default;
			CanonColocationProvider provider = CanonColocationProvider.Active;
			if (current == 0 || provider == null || !segments.TryGetValue(current, out Segment s)) return false;

			Pose canon = provider.CanonSpacePose;
			Pose inCanon = new(canon.position + canon.rotation * cameraInTrackingSpace.position, canon.rotation * cameraInTrackingSpace.rotation);
			node = current;
			cameraInNode = s.correction.Apply(inCanon);
			return true;
		}

		// keyframes never move: every segment sits at the origin of the shared frame
		public bool TryGetNodeWorld(ulong node, out Pose world)
		{
			world = Pose.identity;
			return node != 0;
		}

		public float Drift(ulong a, ulong b)
		{
			if (a == b) return 0;
			if (!segments.TryGetValue(a, out Segment sa) || !segments.TryGetValue(b, out Segment sb) || sa.generation != sb.generation)
				return float.PositiveInfinity;
			return math.abs(sa.path - sb.path) * DriftPerMeter + math.abs(sa.steps - sb.steps);
		}

		// a step only says the views either side were placed differently, not that tracking drifted
		public float WalkDrift(ulong a, ulong b)
		{
			if (a == b) return 0;
			if (!segments.TryGetValue(a, out Segment sa) || !segments.TryGetValue(b, out Segment sb) || sa.generation != sb.generation)
				return float.PositiveInfinity;
			return math.abs(sa.path - sb.path) * DriftPerMeter;
		}
	}
}
