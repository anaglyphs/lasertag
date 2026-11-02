using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Where visual keyframes hang: the track this headset walks, cut into short segments.
	/// Keyframes are stored in the shared frame as the alignment showed it when they were
	/// captured; a segment records how far along the track it began, which site correction was
	/// in force, and where the <see cref="KeyframeGraph"/> has since moved that frame, so two
	/// keyframes' expected drift apart is the distance walked between them plus any correction
	/// stepped in meanwhile. Consecutive
	/// views fall within the trust window and triangulate together; the same place after a
	/// long walk, or across a correction step, does not, and is a revisit to measure.
	/// </summary>
	public sealed class TrackAnchoring : IKeyframeAnchoring
	{
		/// <summary>Head travel per segment, meters.</summary>
		public float SegmentLength = 1f;
		/// <summary>How fast tracking drifts as the head walks.</summary>
		public DriftModel Model = DriftModel.Default;
		/// <summary>A correction change this large starts a new segment: views either side of it were placed differently.</summary>
		public float StepTranslation = 0.01f;
		public float StepYawDegrees = 0.1f;

		private struct Segment
		{
			public float path;
			public float steps;
			public int generation;
			public Pose4 correction;
			// the shared frame as it was placed in, to where the graph has it now
			public Pose4 world;
			public Pose worldPose;
			public int knot;
		}

		// by id less firstId: looked up for every landmark placed
		private readonly List<Segment> segments = new();
		// other headsets' knots, each a node of the views and landmarks hung off it
		private readonly Dictionary<ulong, (Pose4 world, Pose pose)> others = new();
		private ulong firstId = 1;
		// the alignment the current segment's keyframes are placed through
		private CanonAlignment alignment;
		private ulong current;
		private ulong nextId = 1;
		private float path, steps;

		/// <summary>Head travel so far, meters: <see cref="HeadPoseHistory.Path"/>, the odometer the colocator ages constraints by.</summary>
		public float Path => path;

		/// <summary>The segment keyframes are anchored to now; 0 when not aligned.</summary>
		public ulong Current => current;

		public bool IsTracking => current != 0;

		/// <summary>The site correction keyframes of this segment were placed with.</summary>
		public Pose4 CorrectionOf(ulong segment) => TryGet(segment, out Segment s) ? s.correction : Pose4.Identity;

		/// <summary>A pose relative to a node as its own tracking had it: whatever the graph does next.</summary>
		public Pose Raw(ulong node, Pose inNode) => CorrectionOf(node).Inverse.Apply(inNode);

		/// <summary>Where the graph has moved this segment's frame.</summary>
		public Pose4 WorldOf(ulong segment) =>
			TryGet(segment, out Segment s) ? s.world : others.TryGetValue(segment, out (Pose4 world, Pose pose) other) ? other.world : Pose4.Identity;

		/// <summary>Whether this is a segment of this headset's own track.</summary>
		public bool IsOwn(ulong node) => TryGet(node, out _);

		/// <summary>Where the graph has another headset's node.</summary>
		public void SetOtherWorld(ulong node, Pose4 world) => others[node] = (world, world.AsPose);

		public bool HasOtherWorld(ulong node) => others.ContainsKey(node);

		/// <summary>Raw tracking to where keyframes of this segment are in the world now.</summary>
		public Pose4 PlacementOf(ulong segment) => TryGet(segment, out Segment s) ? s.world * s.correction : Pose4.Identity;

		/// <summary>The graph's knot this segment belongs to, or -1.</summary>
		public int KnotOf(ulong segment) => TryGet(segment, out Segment s) ? s.knot : -1;

		/// <summary>Gives a new segment its knot: its generation, path, the head in raw tracking and its placement.</summary>
		public System.Func<int, float, float3, Pose4, int> KnotFor;

		/// <summary>Head travel at the start of this segment.</summary>
		public float PathOf(ulong segment) => TryGet(segment, out Segment s) ? s.path : path;

		private bool TryGet(ulong id, out Segment segment)
		{
			ulong index = id - firstId;
			if (id >= firstId && index < (ulong)segments.Count)
			{
				segment = segments[(int)index];
				return true;
			}

			segment = default;
			return false;
		}

		/// <summary>Every segment so far, for diagnosis: with a keyframe's node, how far along it was made and what placed it.</summary>
		public IEnumerable<(ulong node, float path, int generation, Pose4 correction)> Segments
		{
			get
			{
				for (int i = 0; i < segments.Count; i++)
					yield return (firstId + (ulong)i, segments[i].path, segments[i].generation, segments[i].correction);
			}
		}

		public void Clear()
		{
			segments.Clear();
			others.Clear();
			firstId = nextId;
			current = 0;
		}

		/// <summary>The graph is gone: every segment back where it was placed.</summary>
		public void ForgetKnots()
		{
			others.Clear();
			for (int i = 0; i < segments.Count; i++)
			{
				Segment s = segments[i];
				s.knot = -1;
				s.world = Pose4.Identity;
				s.worldPose = Pose.identity;
				segments[i] = s;
			}

			current = 0;
		}

		/// <summary>Moves every segment to where a solve has it.</summary>
		public void ApplyGraph(KeyframeGraph graph)
		{
			for (int i = 0; i < segments.Count; i++)
			{
				Segment s = segments[i];
				if (s.knot < 0) continue;
				s.world = graph.Transform(s.knot, s.path) * s.correction.Inverse;
				s.worldPose = s.world.AsPose;
				segments[i] = s;
			}
		}

		/// <summary>Follows the head and the alignment keyframes are placed through. Call once per frame.</summary>
		public void Update(CanonAlignment alignment)
		{
			this.alignment = alignment;
			if (MainXRRig.Instance == null || alignment == null) return;
			if (HeadPoseHistory.Instance != null) path = HeadPoseHistory.Instance.Path;

			if (alignment.CurrentPhase != CanonAlignment.Phase.Aligned)
			{
				current = 0;
				return;
			}

			Pose4 correction = alignment.SiteCorrection;
			if (current == 0 || !TryGet(current, out Segment s) || s.generation != alignment.Generation)
			{
				Begin(alignment.Generation, correction, Pose4.Identity);
				return;
			}

			// a correction step is drift between the views either side of it, like a walk
			float stepped = StepSize(s.correction, correction);
			if (stepped > 0 || path - s.path >= SegmentLength)
			{
				steps += stepped;
				// the graph's move carries on: a solve never breaks a stretch of tracking
				Begin(s.generation, correction, s.world);
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

		private void Begin(int generation, Pose4 correction, Pose4 world)
		{
			Pose canon = alignment.CanonSpacePose;
			Vector3 head = canon.position + canon.rotation * MainXRRig.TrackingSpace.InverseTransformPoint(MainXRRig.Camera.transform.position);
			current = nextId++;
			segments.Add(new Segment
			{
				path = path, steps = steps, generation = generation, correction = correction,
				world = world, worldPose = world.AsPose,
				knot = KnotFor?.Invoke(generation, path, head, world * correction) ?? -1,
			});
		}

		// ---- IKeyframeAnchoring ------------------------------------------------

		/// <summary>The camera in the shared frame through the current segment's correction; node identity.</summary>
		public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
		{
			node = 0;
			cameraInNode = default;
			if (current == 0 || alignment == null || !TryGet(current, out Segment s)) return false;

			Pose canon = alignment.CanonSpacePose;
			Pose inCanon = new(canon.position + canon.rotation * cameraInTrackingSpace.position, canon.rotation * cameraInTrackingSpace.rotation);
			node = current;
			cameraInNode = s.correction.Apply(inCanon);
			return true;
		}

		// every segment starts at the origin of the shared frame, and goes where the graph puts it;
		// a saved map's node is no segment, and stays
		public bool TryGetNodeWorld(ulong node, out Pose world)
		{
			if (TryGet(node, out Segment s)) world = s.worldPose;
			else world = others.TryGetValue(node, out (Pose4 world, Pose pose) other) ? other.pose : Pose.identity;
			return node != 0;
		}

		public float Drift(ulong a, ulong b)
		{
			if (a == b) return 0;
			bool ownA = TryGet(a, out Segment sa), ownB = TryGet(b, out Segment sb);
			// other headsets' views are all the graph's, as one saved map is one piece
			if (!ownA && !ownB) return 0;
			if (!ownA || !ownB || sa.generation != sb.generation) return float.PositiveInfinity;
			return Model.PositionSigma(math.abs(sa.path - sb.path)) + math.abs(sa.steps - sb.steps);
		}

		// a step only says the views either side were placed differently, not that tracking drifted
		public float WalkDrift(ulong a, ulong b) => Model.PositionSigma(Walked(a, b));

		/// <summary>Meters walked between two segments; +∞ when unconnected.</summary>
		public float Walked(ulong a, ulong b)
		{
			if (a == b) return 0;
			if (!TryGet(a, out Segment sa) || !TryGet(b, out Segment sb) || sa.generation != sb.generation)
				return float.PositiveInfinity;
			return math.abs(sa.path - sb.path);
		}
	}
}
