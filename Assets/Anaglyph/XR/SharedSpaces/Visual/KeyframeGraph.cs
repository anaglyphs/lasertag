using System;
using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Names for a knot on every headset: its owner, a token a headset picks for each map it
	/// starts, and its place along that owner's walk. As a node, a knot of another headset's, or
	/// of a saved map's walks, hangs the views and landmarks that came from it. Key 0 is no knot.
	/// </summary>
	public static class KnotKeys
	{
		private const ulong OtherBit = 1UL << 63;

		public static ulong Make(uint owner, uint index) => ((ulong)owner << 32) | index;
		public static uint Owner(ulong key) => (uint)(key >> 32);
		public static ulong Node(ulong key) => key | OtherBit;
		public static ulong Key(ulong node) => node & ~OtherBit;
	}

	/// <summary>
	/// Corrects the map itself, across every headset. Raw tracking walks a place a little
	/// differently each lap, and revisits alone hold every lap to the first, error and all. Each
	/// new view is matched against older views of its place, this headset's from every lap and
	/// everyone's it holds (<see cref="KeyframeEdgeJob"/>), and a graph over everyone's tracks
	/// (<see cref="KeyframeGraphSolveJob"/>) averages the walks: every segment's node, and with it
	/// its keyframes, landmarks and any site anchored there, moves to where the laps agree. The
	/// scan doesn't follow: it stays where it was integrated.
	///
	/// There is one graph, and everyone holds all of it: a saved map carries its knots and edges
	/// and goes on moving, and every headset contributes knots and edges through the authority, the
	/// only one to solve (<see cref="KeyframeGraphSync"/>), so anyone can take over from it. A track
	/// only starts once it has been aligned against the graph, and that alignment is what ties it
	/// on (<see cref="LinkNextTrack"/>): no walk is ever left in a piece of its own. Offline, this
	/// headset is the authority.
	///
	/// All of it runs at a walking pace on one worker: one pair of views at a time, a few a
	/// second, and one Gauss-Newton step a frame every few seconds, none of it while nothing new
	/// has come.
	/// </summary>
	public sealed class KeyframeGraph : IDisposable
	{
		[Serializable]
		public sealed class Settings
		{
			public bool enabled = true;
			[Tooltip("Meters of walk one transform of raw tracking covers: drift in less is lost in the matches' noise")]
			public float knotSpacing = 2f;

			[Header("Matching")]
			[Tooltip("Of an older view's landmarks, how many must fall in the new view's image for the pair to be tried")]
			public int minOverlap = 50;
			[Tooltip("Older views tried per new keyframe")]
			public int pairsPerKeyframe = 6;
			[Tooltip("Edges that end a keyframe's tries: the next view has newer news")]
			public int edgesPerKeyframe = 3;
			[Tooltip("Keyframes kept waiting for their pairs, newest first: at a run, more come than can be matched")]
			public int maxWaiting = 16;
			[Tooltip("Of those, the most from one pass of a place: a graph learns from other laps, not from more of one")]
			public int pairsPerPass = 2;
			[Tooltip("Seconds of capture time counted as one pass")]
			public float passSeconds = 15f;
			[Tooltip("Pairs of views matched a second, at most: each is a few milliseconds on a worker")]
			public float pairsPerSecond = 12f;
			[Tooltip("Views farther apart than this (m) aren't tried: on device this holds 90% of what matches, half of it past 2.5 m")]
			public float pairDistance = 6f;
			[Tooltip("Views turned further apart than this (°) aren't tried: on device this holds 99% of what matches, half of it past 15°")]
			public float pairAngle = 45f;
			[Tooltip("Meters walked, at least, between two views of a pair: nearer than this, they are one stretch of tracking")]
			public float pairMinWalk = 10f;
			public int minInliers = 20;
			public float maxYawSigmaDegrees = 1f;
			[Tooltip("Lowest inverse depth a landmark can have and count as ranged, 1/m")]
			public float minInverseDepth = 0.005f;
			[Tooltip("Largest share of its inverse depth a landmark's sigma can be and count as ranged")]
			public float maxDepthSigmaShare = 0.35f;
			[Tooltip("Features' localization noise, pixels")]
			public float pixelSigma = 1.5f;

			[Header("Solving")]
			[Tooltip("Seconds between solves, at least")]
			public float solveInterval = 2f;
			[Tooltip("Edges to wait for before solving again")]
			public int minNewEdges = 2;
			[Tooltip("Knots the solve holds apart at most: past this the older half is solved in groups. A step's cost grows with the cube of this")]
			public int maxSolveKnots = 500;
			[Tooltip("Gauss-Newton steps a solve takes at most, one a frame")]
			public int iterations = 3;
			[Tooltip("How far one pair of views' edge is from the truth across its knots, meters: the rays' own fit is far surer than the landmarks it rests on allow")]
			public float edgeSigma = 0.04f;
			[Tooltip("Inliers below which an edge counts for a quarter, and tracking isn't corrected from it directly")]
			public int confidentInliers = 30;
			[Tooltip("Likewise in yaw, degrees: an edge's yaw is much the worse half of it")]
			public float edgeYawSigmaDegrees = 1.5f;
			[Tooltip("How fast the solve lets tracking wander between knots, meters per √meter along each axis: what long walks measured on device, more than the short stretches landmarks are trusted over")]
			public float driftRate = 0.028f;
			[Tooltip("Sigmas past which an edge's pull stops growing")]
			public float huber = 2f;
			public int maxEdges = 8192;
			[Tooltip("How well a solve is taken to place this headset's tracking, meters: as a constraint beside the sites")]
			public float tipSigma = 0.05f;
			[Tooltip("Newest matches of this headset's own views kept as constraints beside the solve, which lags them by seconds: at a run, seconds are meters of drift")]
			public int freshFixes = 12;
			[Tooltip("How well one match places this headset's tracking, meters")]
			public float fixSigma = 0.05f;
			[Tooltip("Likewise in yaw, degrees")]
			public float fixYawSigmaDegrees = 0.75f;
			[Tooltip("Two matches agree enough to act on when they place tracking this close, meters")]
			public float fixAgreement = 0.2f;
			[Tooltip("Likewise in yaw, degrees")]
			public float tipYawSigmaDegrees = 0.15f;
		}

		// what a view offers as the older of a pair
		private sealed class OlderView
		{
			public int[] features;
			public float3[] points;
		}

		private readonly Settings settings;
		private readonly VisualLocalizer localizer;
		private readonly TrackAnchoring track;

		private NativeList<KeyframeGraphKnot> knots = new(256, Allocator.Persistent);
		// parallel to knots
		private NativeList<double4> state = new(256, Allocator.Persistent);
		private NativeList<KeyframeEdge> edges = new(1024, Allocator.Persistent);
		// parallel to knots
		private readonly List<ulong> keys = new();
		private readonly Dictionary<ulong, int> knotByKey = new();
		private readonly Dictionary<long, int> knotByPlace = new();
		private int lastKnotGeneration = -1, lastOwnKnot = -1, tipGeneration = -1;
		private uint nextOwnIndex;
		// the latest of this headset's knots a solve has placed, and whether it measured anything of this headset's to do so
		private int tipKnot = -1;
		private struct Fix
		{
			public ulong keyA;
			public float3 pointA, pointB;
			public float yaw, path;
		}

		// the newest first-hand matches, and one waiting for another to agree with it
		private readonly List<Fix> fixes = new();
		private Fix waitingFix;
		private bool hasWaitingFix;
		private bool ownEdgesSolved;
		// the alignment the next track starts from, to tie it to the graph with
		private ulong linkKey;
		private KeyframeEdge linkEdge;

		// for the authority: what it has yet to tell everyone, and edges naming knots not come yet
		private readonly List<float4> sentPoses = new();
		private readonly List<PoseMessage> solvedPoses = new();
		private readonly List<EdgeMessage> waitingEdges = new();
		// what it has yet to pass on: to everyone from the authority, to the authority from anyone else
		private readonly List<KnotMessage> knotsToSend = new();
		private readonly List<EdgeMessage> edgesToSend = new();

		private readonly List<(VisualKeyframe keyframe, float at)> waiting = new();
		private readonly Dictionary<VisualKeyframe, OlderView> olderViews = new();
		private readonly List<(float likeness, VisualKeyframe keyframe)> candidates = new();
		private readonly Dictionary<long, int> pairsOfPass = new();
		private readonly List<VisualKeyframe> pairs = new();

		// one pair at a time: the newer view's side stays for all of its pairs
		private NativeArray<half> descriptorsA, descriptorsB;
		private NativeArray<float3> pointsA, raysB;
		private NativeArray<KeyframeEdge> edgeResult = new(1, Allocator.Persistent);
		private NativeArray<int> inliersOfA;
		private VisualKeyframe olderOfPair;
		private int[] featuresOfPair;
		private JobHandle edgeHandle;
		private bool edgeInFlight;
		private int countB, edgesOfNewer;
		private float pathB, newerAt;
		private ulong keyA, keyB;
		private float3 centerB;
		private quaternion rotationB;
		private float angularSigma;
		private uint seed = 1;
		private float nextPair;

		private NativeArray<KeyframeGraphKnot> solveKnots;
		private NativeArray<KeyframeEdge> solveEdges;
		private NativeArray<double4> solveState;
		private NativeArray<double> largestStep = new(1, Allocator.Persistent);
		private JobHandle solveHandle;
		private bool solving;
		private int solveIteration, solveEdgeCount, solveKnotCount, solveGroupCount;
		private int[] groupOf = Array.Empty<int>(), groupFirst = Array.Empty<int>(), groupSize = Array.Empty<int>();
		private double4[] groupWas = Array.Empty<double4>();
		private bool discardSolve;
		private int edgesSinceSolve;
		private const int NewerFeatures = 512;
		// by KeyframeEdgeJob's reason, negated
		private readonly int[] pairsLost = new int[6];
		private float edgeLagSum, pairSeconds, pairBegan;
		private int edgeLagCount;
		private float nextSolve, solveBegan;

		/// <summary>This headset's name for the knots and views of its current map.</summary>
		public uint Owner { get; private set; }

		public int KnotCount => knots.Length;
		public int EdgeCount => edges.Length;
		public int PairsTried { get; private set; }
		public int Solves { get; private set; }
		/// <summary>Seconds the last solve took, from its first step to its last, frames included.</summary>
		public float LastSolveSeconds { get; private set; }
		/// <summary>How far the last solve moved the map at most, meters at its knots.</summary>
		public float LastSolveMoved { get; private set; }

		public string Summary => $"{KnotCount} knots, {EdgeCount} edges from {PairsTried} pairs ({pairsLost[1]} few matches, {pairsLost[2]} no consensus, {pairsLost[3]} lost, {pairsLost[4]} loose yaw, {pairsLost[5]} bounds; {(edgeLagCount > 0 ? edgeLagSum / edgeLagCount : 0):F1} s to an edge, {(PairsTried > 0 ? pairSeconds / PairsTried * 1000 : 0):F1} ms a pair), {Solves} solves (last {LastSolveSeconds * 1000:F0} ms, moved {LastSolveMoved * 100:F1} cm)";

		/// <summary>In debug mode: a landmark one of this headset's views just matched in an older view.</summary>
		public event Action<int> LandmarkMatched = delegate { };

		public KeyframeGraph(Settings settings, VisualLocalizer localizer, TrackAnchoring track)
		{
			this.settings = settings;
			this.localizer = localizer;
			this.track = track;
			Owner = NewOwner();
			const int d = FeatureSet.DescriptorSize, rows = XFeatExtractor.MaxKeypoints;
			descriptorsA = new NativeArray<half>(rows * d, Allocator.Persistent);
			descriptorsB = new NativeArray<half>(rows * d, Allocator.Persistent);
			pointsA = new NativeArray<float3>(rows, Allocator.Persistent);
			raysB = new NativeArray<float3>(rows, Allocator.Persistent);
			inliersOfA = new NativeArray<int>(rows, Allocator.Persistent);
		}

		public void Dispose()
		{
			edgeHandle.Complete();
			solveHandle.Complete();
			knots.Dispose();
			state.Dispose();
			edges.Dispose();
			descriptorsA.Dispose();
			descriptorsB.Dispose();
			pointsA.Dispose();
			raysB.Dispose();
			inliersOfA.Dispose();
			edgeResult.Dispose();
			largestStep.Dispose();
			DisposeSolve();
		}

		private static uint NewOwner() => (uint)new System.Random().Next(1, int.MaxValue);

		private void DisposeSolve()
		{
			if (solveKnots.IsCreated) solveKnots.Dispose();
			if (solveEdges.IsCreated) solveEdges.Dispose();
			if (solveState.IsCreated) solveState.Dispose();
		}

		/// <summary>Everything measured so far is gone with the map it was measured on; the track's nodes go back to where they were placed.</summary>
		public void Clear()
		{
			edgeHandle.Complete();
			solveHandle.Complete();
			edgeInFlight = false;
			if (solving) DisposeSolve();
			solving = false;
			knots.Clear();
			state.Clear();
			edges.Clear();
			keys.Clear();
			knotByKey.Clear();
			knotByPlace.Clear();
			lastKnotGeneration = lastOwnKnot = tipKnot = -1;
			fixes.Clear();
			hasWaitingFix = false;
			ownEdgesSolved = false;
			linkKey = 0;
			nextOwnIndex = 0;
			sentPoses.Clear();
			solvedPoses.Clear();
			waitingEdges.Clear();
			knotsToSend.Clear();
			edgesToSend.Clear();
			Owner = NewOwner();
			localizer.Map.LocalOwner = Owner;
			waiting.Clear();
			olderViews.Clear();
			pairs.Clear();
			edgesSinceSolve = 0;
			track.ForgetKnots();
		}

		/// <summary>A new keyframe: matched against older views, the newest first.</summary>
		public void Add(VisualKeyframe keyframe)
		{
			if (settings.enabled && keyframe.node != 0) Enqueue(keyframe);
		}

		/// <summary>Another headset's view, just received: what this headset hasn't shared is matched against it here, since its owner can't.</summary>
		public void AddReceived(VisualKeyframe keyframe)
		{
			if (settings.enabled) Enqueue(keyframe);
		}

		private void Enqueue(VisualKeyframe keyframe)
		{
			if (waiting.Count >= math.max(settings.maxWaiting, 1)) waiting.RemoveAt(0);
			waiting.Add((keyframe, Time.unscaledTime));
		}

		public void OnEvicted(VisualKeyframe keyframe)
		{
			olderViews.Remove(keyframe);
			pairs.Remove(keyframe);
			waiting.RemoveAll(w => w.keyframe == keyframe);
		}

		/// <summary>
		/// The knot a new segment belongs to, made when the walk reaches a new one. It starts
		/// where the segment is placed: a frame's tracking moved only by the walk since.
		/// </summary>
		public int KnotFor(int generation, float path, float3 head, Pose4 placement)
		{
			long place = ((long)generation << 32) | (uint)(int)math.floor(path / settings.knotSpacing);
			if (knotByPlace.TryGetValue(place, out int index)) return index;

			// a new alignment's raw frame is another: what placed the old one says nothing of it
			bool newTrack = generation != lastKnotGeneration;
			if (newTrack)
			{
				fixes.Clear();
				hasWaitingFix = false;
			}

			// the frame everything else is held to; any other track starts only tied to the graph:
			// until an alignment does that, this headset's views are its own alone
			bool isRoot = knots.Length == 0 && SyncBus.IsAuthority;
			if (newTrack && linkKey == 0 && !isRoot) return -1;

			ulong key = KnotKeys.Make(Owner, nextOwnIndex++);
			int previous = lastKnotGeneration == generation ? lastOwnKnot : -1;
			double4 transform = new(placement.translation, placement.yaw);
			index = AddKnot(key, head, path, previous, transform, isRoot);
			knotByPlace[place] = index;
			lastKnotGeneration = generation;
			lastOwnKnot = index;
			if (SyncBus.Active)
				knotsToSend.Add(new KnotMessage
				{
					key = key, previous = previous >= 0 ? keys[previous] : 0,
					origin = head, path = path, transform = (float4)transform, isFixed = isRoot ? 1 : 0,
				});

			if (newTrack && linkKey != 0)
			{
				AddEdge(linkKey, key, linkEdge);
				linkKey = 0;
			}

			return index;
		}

		/// <summary>
		/// The alignment just found, which the next track starts from: at <paramref name="focus"/> it
		/// held the new raw frame and the map together, so the new track's first knot is tied to
		/// <paramref name="anchorNode"/>'s, which it was measured against, as a saved or recentered
		/// walk's are to the rest. A new raw frame is the map's own at the time, so it is the identity.
		/// </summary>
		public void LinkNextTrack(ulong anchorNode, float3 focus)
		{
			linkKey = 0;
			if (!TryKey(anchorNode, out ulong anchor) || !knotByKey.ContainsKey(anchor))
			{
				if (keys.Count == 0) return;
				anchor = keys[0];
			}

			Pose4 world = WorldOfKey(anchor);
			linkKey = anchor;
			linkEdge = new KeyframeEdge
			{
				pointA = world.ApplyInverse(focus),
				pointB = focus,
				yaw = -world.yaw,
				positionInformation = 1f / (settings.edgeSigma * settings.edgeSigma),
				yawInformation = 1f / math.square(math.radians(settings.edgeYawSigmaDegrees)),
				inliers = LinkInliers,
			};
		}

		private const int LinkInliers = -1;

		/// <summary>Tracking is lost or recentered: what was waiting to be matched is dropped, and nothing more is contributed until a new alignment.</summary>
		public void DropWaiting()
		{
			waiting.Clear();
			pairs.Clear();
		}

		private int AddKnot(ulong key, float3 origin, float path, int previous, double4 transform, bool isFixed)
		{
			int index = knots.Length;
			knots.Add(new KeyframeGraphKnot { origin = origin, path = path, previous = previous, next = -1, isFixed = isFixed });
			state.Add(transform);
			keys.Add(key);
			sentPoses.Add((float4)transform);
			knotByKey[key] = index;
			if (previous >= 0)
			{
				KeyframeGraphKnot before = knots[previous];
				before.next = index;
				knots[previous] = before;
			}

			return index;
		}

		/// <summary>A knot walked elsewhere, or earlier: come from the authority, a headset, or a saved map.</summary>
		public void AddOtherKnot(in KnotMessage knot)
		{
			if (knotByKey.ContainsKey(knot.key)) return;
			int previous = knot.previous != 0 && knotByKey.TryGetValue(knot.previous, out int found) ? found : -1;
			AddKnot(knot.key, knot.origin, knot.path, previous, knot.transform, knot.isFixed != 0);
			track.SetOtherWorld(KnotKeys.Node(knot.key), new Pose4(knot.transform.xyz, knot.transform.w));
			if (SyncBus.IsAuthority && SyncBus.Active) knotsToSend.Add(knot);

			for (int i = waitingEdges.Count - 1; i >= 0; i--)
			{
				EdgeMessage e = waitingEdges[i];
				if (!TryAddSolvable(e.keyA, e.keyB, e.edge)) continue;
				waitingEdges.RemoveAt(i);
				if (SyncBus.IsAuthority && SyncBus.Active) edgesToSend.Add(e);
			}
		}

		/// <summary>An edge any headset has measured. The authority takes it and tells everyone; anyone else sends it there, and holds it when it comes back.</summary>
		public void AddEdge(ulong a, ulong b, in KeyframeEdge edge)
		{
			if (a == b) return;
			EdgeMessage message = new() { keyA = a, keyB = b, edge = edge };
			if (!SyncBus.IsAuthority)
			{
				edgesToSend.Add(message);
				return;
			}

			if (TryAddSolvable(a, b, edge))
			{
				if (SyncBus.Active) edgesToSend.Add(message);
			}
			// its knots are on their way: everyone's come in order, but not everyone's together
			else if (waitingEdges.Count < settings.maxEdges / 4) waitingEdges.Add(message);
		}

		/// <summary>An edge the authority has told everyone of.</summary>
		public void AddToldEdge(in EdgeMessage message)
		{
			if (!TryAddSolvable(message.keyA, message.keyB, message.edge) && waitingEdges.Count < settings.maxEdges / 4)
				waitingEdges.Add(message);
		}

		private bool TryAddSolvable(ulong a, ulong b, in KeyframeEdge edge)
		{
			if (!knotByKey.TryGetValue(a, out int indexA) || !knotByKey.TryGetValue(b, out int indexB)) return false;
			KeyframeEdge e = edge;
			e.knotA = indexA;
			e.knotB = indexB;
			// past capacity one goes at random: what any one pair of views says is said again by others.
			// What ties a track to the graph is said only once
			if (edges.Length >= settings.maxEdges)
			{
				uint at = seed++ * 2654435761u % (uint)edges.Length;
				while (edges[(int)at].inliers < 0) at = (at + 1) % (uint)edges.Length;
				edges[(int)at] = e;
			}
			else edges.Add(e);
			edgesSinceSolve++;
			return true;
		}

		// ---- the whole graph: to a joiner, and to and from a saved map ------------

		private const uint WholeMagic = 0x47524756; // VGRG

		/// <summary>Every knot and edge, as a joiner or a map file holds them.</summary>
		public byte[] Encode()
		{
			int knotSize = SyncBytes.Size<KnotMessage>(), edgeSize = SyncBytes.Size<EdgeMessage>();
			int offset = sizeof(uint) + 2 * sizeof(int);
			byte[] bytes = new byte[offset + knots.Length * knotSize + edges.Length * edgeSize];
			SyncBytes.Write(bytes, 0, WholeMagic);
			SyncBytes.Write(bytes, sizeof(uint), knots.Length);
			SyncBytes.Write(bytes, sizeof(uint) + sizeof(int), edges.Length);
			for (int k = 0; k < knots.Length; k++, offset += knotSize)
			{
				KeyframeGraphKnot knot = knots[k];
				SyncBytes.Write(bytes, offset, new KnotMessage
				{
					key = keys[k], previous = knot.previous >= 0 ? keys[knot.previous] : 0,
					origin = knot.origin, path = knot.path, transform = (float4)state[k], isFixed = knot.isFixed ? 1 : 0,
				});
			}

			for (int e = 0; e < edges.Length; e++, offset += edgeSize)
			{
				KeyframeEdge edge = edges[e];
				SyncBytes.Write(bytes, offset, new EdgeMessage { keyA = keys[edge.knotA], keyB = keys[edge.knotB], edge = edge });
			}

			return bytes;
		}

		/// <summary>Takes in what <see cref="Encode"/> gave; false when it isn't that.</summary>
		public bool Decode(byte[] bytes, int offset = 0)
		{
			int knotSize = SyncBytes.Size<KnotMessage>(), edgeSize = SyncBytes.Size<EdgeMessage>();
			if (bytes == null || bytes.Length - offset < sizeof(uint) + 2 * sizeof(int) || SyncBytes.Read<uint>(bytes, offset) != WholeMagic) return false;
			int knotCount = SyncBytes.Read<int>(bytes, offset + sizeof(uint)), edgeCount = SyncBytes.Read<int>(bytes, offset + sizeof(uint) + sizeof(int));
			offset += sizeof(uint) + 2 * sizeof(int);
			if (knotCount < 0 || edgeCount < 0 || bytes.Length - offset != knotCount * knotSize + edgeCount * edgeSize) return false;
			for (int k = 0; k < knotCount; k++, offset += knotSize)
				AddOtherKnot(SyncBytes.Read<KnotMessage>(bytes, offset));
			for (int e = 0; e < edgeCount; e++, offset += edgeSize)
			{
				EdgeMessage message = SyncBytes.Read<EdgeMessage>(bytes, offset);
				TryAddSolvable(message.keyA, message.keyB, message.edge);
			}

			return true;
		}

		/// <summary>
		/// A revisit that passed the gates, between the views it stood on and the frame it was
		/// made from: fewer rays' worth of agreement than a pair of views if not for its several
		/// frames and tighter gates, so it counts by its own sigmas.
		/// </summary>
		public void AddMeasurement(ulong queryNode, ulong anchorNode, Pose4 correction, float3 focus, float sigmaTranslation, float sigmaYaw)
		{
			if (!settings.enabled || !TryKey(queryNode, out ulong b) || !TryKey(anchorNode, out ulong a) || a == b) return;
			Pose4 anchorWorld = track.WorldOf(anchorNode);
			Pose4 toAnchor = Pose4.Between(anchorWorld, correction);
			float3 point = anchorWorld.ApplyInverse(focus);
			Pose4 anchorCorrection = track.CorrectionOf(anchorNode);
			AddEdge(a, b, new KeyframeEdge
			{
				pointA = anchorCorrection.ApplyInverse(point),
				pointB = toAnchor.ApplyInverse(point),
				yaw = toAnchor.yaw - anchorCorrection.yaw,
				positionInformation = 1f / (sigmaTranslation * sigmaTranslation),
				yawInformation = 1f / (sigmaYaw * sigmaYaw),
				inliers = 1,
			});
		}

		/// <summary>What has yet to be passed on: every knot, since the edges that tie them on follow, and at most this many edges.</summary>
		public void TakeToSend(List<KnotMessage> knotsOut, List<EdgeMessage> edgesOut, int maxEdges)
		{
			knotsOut.AddRange(knotsToSend);
			knotsToSend.Clear();
			int edgeCount = math.min(edgesToSend.Count, maxEdges);
			edgesOut.AddRange(edgesToSend.GetRange(0, edgeCount));
			edgesToSend.RemoveRange(0, edgeCount);
		}

		/// <summary>The authority: the knots the latest solve moved, to tell everyone.</summary>
		public void TakeSolvedPoses(List<PoseMessage> posesOut)
		{
			posesOut.AddRange(solvedPoses);
			solvedPoses.Clear();
		}

		/// <summary>Everyone else: where the authority's solve has the knots.</summary>
		public void ApplyPoses(List<PoseMessage> poses)
		{
			bool ownMoved = false;
			foreach (PoseMessage pose in poses)
			{
				if (knotByKey.TryGetValue(pose.key, out int index)) state[index] = pose.transform;
				if (KnotKeys.Owner(pose.key) != Owner)
				{
					track.SetOtherWorld(KnotKeys.Node(pose.key), new Pose4(pose.transform.xyz, pose.transform.w));
					continue;
				}

				ownMoved = true;
				if (index > tipKnot)
				{
					tipKnot = index;
					tipGeneration = lastKnotGeneration;
				}
			}

			Solves++;
			if (ownMoved) track.ApplyGraph(this);
			localizer.Map.RefreshPlacement();
		}

		/// <summary>The knot this node is, by the name every headset knows it by.</summary>
		private bool TryKey(ulong node, out ulong key)
		{
			key = 0;
			if (node == 0) return false;
			int knot = track.KnotOf(node);
			if (knot >= 0) key = keys[knot];
			else if (track.IsOwn(node)) return false;
			else key = KnotKeys.Key(node);
			return true;
		}

		/// <summary>The name a node goes by on other headsets.</summary>
		public ulong WireNode(ulong node) => TryKey(node, out ulong key) ? KnotKeys.Node(key) : 0;

		/// <summary>Raw tracking to the map at a node, named as it is on other headsets.</summary>
		public float4 TransformOfWire(ulong node)
		{
			if (knotByKey.TryGetValue(KnotKeys.Key(node), out int index)) return (float4)state[index];
			Pose4 world = track.WorldOf(node);
			return new float4(world.translation, world.yaw);
		}

		/// <summary>
		/// Where the latest solve puts this headset's tracking, as a constraint like a site's: the
		/// laps of everyone's map say where it belongs even where no revisit has passed its gates.
		/// False before a solve has measured this headset's knots, or once it has been recentered.
		/// </summary>
		public bool TryTipConstraint(in Matrix4x4 worldFromPlaced, out ColocationConstraint constraint)
		{
			constraint = default;
			if (!settings.enabled || tipKnot < 0 || tipKnot >= knots.Length) return false;
			if (tipGeneration != lastKnotGeneration) return false;
			KeyframeGraphKnot knot = knots[tipKnot];
			Pose4 transform = Transform(tipKnot, knot.path);
			constraint = new ColocationConstraint(new Pose(worldFromPlaced.MultiplyPoint3x4(knot.origin), worldFromPlaced.rotation),
				new Pose(transform.Apply(knot.origin), transform.Rotation), true,
				settings.tipSigma, math.radians(settings.tipYawSigmaDegrees), knot.path);
			return true;
		}

		/// <summary>Whether the graph has anything to place this headset's tracking with.</summary>
		public bool HasConstraints => settings.enabled && (fixes.Count > 0 || tipKnot >= 0 && tipGeneration == lastKnotGeneration);

		/// <summary>The newest agreed matches of this headset's own views, through where the graph has the views they matched now.</summary>
		public void AddFixConstraints(List<ColocationConstraint> results, in Matrix4x4 worldFromPlaced)
		{
			if (!settings.enabled) return;
			Quaternion placedRotation = worldFromPlaced.rotation;
			float yawSigma = math.radians(settings.fixYawSigmaDegrees);
			foreach (Fix fix in fixes)
			{
				Pose4 older = WorldOfKey(fix.keyA);
				results.Add(new ColocationConstraint(new Pose(worldFromPlaced.MultiplyPoint3x4(fix.pointB), placedRotation),
					new Pose(older.Apply(fix.pointA), quaternion.RotateY(older.yaw + fix.yaw)), true, settings.fixSigma, yawSigma, fix.path));
			}
		}

		private Pose4 WorldOfKey(ulong key)
		{
			if (knotByKey.TryGetValue(key, out int index))
			{
				double4 x = state[index];
				return new Pose4((float3)x.xyz, (float)x.w);
			}

			return track.WorldOf(KnotKeys.Node(key));
		}

		// one match can be wrong and move the world by a meter: it has to agree with the last
		// that was believed, or failing that with the one after it
		private void OfferFix(ulong keyA, in KeyframeEdge edge, float path)
		{
			Fix fix = new() { keyA = keyA, pointA = edge.pointA, pointB = edge.pointB, yaw = edge.yaw, path = path };
			float3 implied = Implied(fix);
			if (fixes.Count > 0 && math.distance(implied, Implied(fixes[^1])) <= settings.fixAgreement)
			{
				AddFix(fix);
				hasWaitingFix = false;
			}
			else if (hasWaitingFix && math.distance(implied, Implied(waitingFix)) <= settings.fixAgreement)
			{
				AddFix(waitingFix);
				AddFix(fix);
				hasWaitingFix = false;
			}
			else
			{
				waitingFix = fix;
				hasWaitingFix = true;
			}
		}

		// how far the match moves raw tracking, at the point it placed
		private float3 Implied(in Fix fix) => WorldOfKey(fix.keyA).Apply(fix.pointA) - fix.pointB;

		private void AddFix(in Fix fix)
		{
			if (fixes.Count >= math.max(settings.freshFixes, 1)) fixes.RemoveAt(0);
			fixes.Add(fix);
		}

#if UNITY_EDITOR
		/// <summary>Every pair tried since a test last took them: own older view, meters and degrees apart, landmarks in frame, A's and B's features, how it came out.</summary>
		public readonly List<string> PairLog = new();
		private string pairNote;
		private VisualKeyframe newerCamera;

		/// <summary>The graph as it stands, one JSON document, for tests to hold against the simulation's truth.</summary>
		public string ToDebugJson()
		{
			var text = new System.Text.StringBuilder(256 + knots.Length * 96 + edges.Length * 96);
			text.Append("{\"owner\":").Append(Owner).Append(",\"tipKnot\":").Append(tipKnot).Append(",\"knots\":[");
			for (int k = 0; k < knots.Length; k++)
			{
				KeyframeGraphKnot knot = knots[k];
				double4 x = state[k];
				if (k > 0) text.Append(',');
				text.Append(FormattableString.Invariant($"[{KnotKeys.Owner(keys[k])},{knot.path:F3},{knot.origin.x:F4},{knot.origin.y:F4},{knot.origin.z:F4},{x.x:F5},{x.y:F5},{x.z:F5},{x.w:F6},{(knot.isFixed ? 1 : 0)},{knot.previous}]"));
			}

			text.Append("],\"edges\":[");
			for (int e = 0; e < edges.Length; e++)
			{
				KeyframeEdge edge = edges[e];
				if (e > 0) text.Append(',');
				text.Append(FormattableString.Invariant($"[{edge.knotA},{edge.knotB},{edge.pointA.x:F4},{edge.pointA.y:F4},{edge.pointA.z:F4},{edge.pointB.x:F4},{edge.pointB.y:F4},{edge.pointB.z:F4},{edge.yaw:F6},{edge.inliers}]"));
			}

			return text.Append("]}").ToString();
		}

		/// <summary>The newest place the solve has this headset's tracking, raw to the map, or false before one.</summary>
		public bool TryTipTransform(out float4 transform)
		{
			transform = default;
			if (tipKnot < 0 || tipKnot >= knots.Length) return false;
			transform = (float4)state[tipKnot];
			return true;
		}
#endif

		/// <summary>Raw tracking to the map at this far along a knot's stretch, between it and the next.</summary>
		/// <summary>Appends lines as pairs of points: each knot's axes and the walk between knots, and the matches between knots.</summary>
		public void AddDebugLines(List<Vector3> walkLines, List<Vector3> matchLines, float axisLength)
		{
			for (int k = 0; k < knots.Length; k++)
			{
				Vector3 at = WorldOfKnot(k);
				Quaternion rotation = Transform(k, knots[k].path).Rotation;
				walkLines.Add(at);
				walkLines.Add(at + rotation * Vector3.up * (axisLength * 0.75f));
				walkLines.Add(at);
				walkLines.Add(at + rotation * Vector3.right * (axisLength * 0.5f));
				walkLines.Add(at);
				walkLines.Add(at + rotation * Vector3.forward * axisLength);
				if (knots[k].previous < 0) continue;
				walkLines.Add(at);
				walkLines.Add(WorldOfKnot(knots[k].previous));
			}

			for (int e = 0; e < edges.Length; e++)
			{
				if (edges[e].knotA == edges[e].knotB) continue;
				matchLines.Add(WorldOfKnot(edges[e].knotA));
				matchLines.Add(WorldOfKnot(edges[e].knotB));
			}
		}

		private Vector3 WorldOfKnot(int knot) => Transform(knot, knots[knot].path).Apply(knots[knot].origin);

		public Pose4 Transform(int knot, float path)
		{
			double4 x = state[knot];
			int next = knots[knot].next;
			if (next >= 0)
			{
				float span = knots[next].path - knots[knot].path;
				if (span > 1e-3f) x = math.lerp(x, state[next], math.saturate((path - knots[knot].path) / span));
			}

			return new Pose4((float3)x.xyz, (float)x.w);
		}

		/// <summary>Call every frame.</summary>
		public void Update()
		{
			if (!settings.enabled) return;
			UpdatePairs();
			if (solving || SyncBus.IsAuthority) UpdateSolve();
		}

		// ---- pairs ---------------------------------------------------------------

		private void UpdatePairs()
		{
			if (edgeInFlight)
			{
				if (!edgeHandle.IsCompleted) return;
				edgeHandle.Complete();
				edgeInFlight = false;
				pairSeconds += Time.realtimeSinceStartup - pairBegan;
#if UNITY_EDITOR
				PairLog.Add(FormattableString.Invariant($"{pairNote},{edgeResult[0].inliers}"));
#endif
				KeyframeEdge edge = edgeResult[0];
				if (edge.inliers > 0)
				{
					// few inliers are about twice as far from the truth
					float confidence = edge.inliers < settings.confidentInliers ? 0.25f : 1f;
					edge.positionInformation = confidence / (settings.edgeSigma * settings.edgeSigma);
					edge.yawInformation = confidence / math.square(math.radians(settings.edgeYawSigmaDegrees));
					AddEdge(keyA, keyB, edge);
					if (edge.inliers >= settings.confidentInliers && KnotKeys.Owner(keyB) == Owner) OfferFix(keyA, edge, pathB);
					if (AnaglyphDebugging.DebugMode && KnotKeys.Owner(keyB) == Owner)
						for (int i = 0; i < edge.inliers; i++)
							LandmarkMatched(olderOfPair.landmarks[featuresOfPair[inliersOfA[i]]]);
					edgeLagSum += Time.unscaledTime - newerAt;
					edgeLagCount++;
					if (++edgesOfNewer >= settings.edgesPerKeyframe) pairs.Clear();
				}
				else pairsLost[-edge.inliers]++;
			}

			if (Time.unscaledTime < nextPair) return;

			if (pairs.Count == 0 && !BeginNextKeyframe()) return;
			VisualKeyframe older = pairs[0];
			pairs.RemoveAt(0);
			SchedulePair(older);
			nextPair = Time.unscaledTime + 1f / math.max(settings.pairsPerSecond, 0.1f);
		}

		// the newest keyframe's own side, and which older views it will try
		private bool BeginNextKeyframe()
		{
			while (waiting.Count > 0)
			{
				(VisualKeyframe keyframe, float at) = waiting[^1];
				waiting.RemoveAt(waiting.Count - 1);
				if (!LoadNewer(keyframe)) continue;
				newerAt = at;
#if UNITY_EDITOR
				newerCamera = keyframe;
#endif
				edgesOfNewer = 0;
				SelectOlder(keyframe);
				if (pairs.Count > 0) return true;
			}

			return false;
		}

		// the views nearest it and most alike in heading, a few from each pass of its place
		private void SelectOlder(VisualKeyframe newer)
		{
			candidates.Clear();
			pairsOfPass.Clear();
			VisualMap map = localizer.Map;
			float maxCosine = math.cos(math.radians(settings.pairAngle));
			Vector3 at = newer.camera.position, forward = newer.camera.forward;
			bool newerIsOwn = track.IsOwn(newer.node);
			foreach (VisualKeyframe k in map.Keyframes)
			{
				if (k == newer || k.node == 0) continue;
				bool own = track.IsOwn(k.node);
				// another headset's view: no walk or clock in common with this one, and it is
				// matched against only what its owner can't have, what this headset hasn't shared
				if (newerIsOwn ? own && (k.timestampNs >= newer.timestampNs || track.Walked(k.node, newer.node) < settings.pairMinWalk) : !own || k.shared) continue;
				Vector3 to = k.camera.position - at;
				to.y = 0;
				if (to.sqrMagnitude > settings.pairDistance * settings.pairDistance) continue;
				float cosine = Vector3.Dot(k.camera.forward, forward);
				if (cosine < maxCosine) continue;
				// the likeliest first: a keyframe stops at a few edges, so the rest are tried only when these fail
				candidates.Add((to.magnitude + math.degrees(math.acos(math.saturate(cosine))) / 10f, k));
			}

			candidates.Sort((x, y) => x.likeness.CompareTo(y.likeness));
			long passNs = (long)(math.max(settings.passSeconds, 0.1f) * 1e9);
			int taken = 0;
			foreach ((float _, VisualKeyframe k) in candidates)
			{
				if (taken >= settings.pairsPerKeyframe) break;
				long pass = ((long)k.owner << 32) ^ (k.timestampNs / passNs);
				pairsOfPass.TryGetValue(pass, out int count);
				// near and turned alike can still share nothing: a pair is worth trying only with enough of its landmarks in frame
				if (count >= settings.pairsPerPass || Overlap(newer, k) < settings.minOverlap) continue;
				pairsOfPass[pass] = count + 1;
				pairs.Add(k);
				taken++;
			}
		}

		// how many of an older view's ranged landmarks fall in the newer view's image: drift is small against the room
		private int Overlap(VisualKeyframe newer, VisualKeyframe older)
		{
			float3[] points = LoadOlder(older).points;
			PinholeCamera camera = newer.intrinsics;
			float width = 2 * camera.cx, height = 2 * camera.cy;
			quaternion toCamera = math.conjugate(rotationB);
			int count = 0;
			for (int i = 0; i < points.Length; i++)
			{
				float3 p = math.mul(toCamera, points[i] - centerB);
				if (p.z < 0.1f) continue;
				float x = camera.fx * p.x / p.z + camera.cx, y = camera.fy * p.y / p.z + camera.cy;
				if (x >= 0 && y >= 0 && x < width && y < height) count++;
			}

			return count;
		}

		public bool IsRanged(Landmark l) => l.IsUsable && l.inverseDepth >= settings.minInverseDepth && l.inverseDepthSigma < settings.maxDepthSigmaShare * l.inverseDepth;

		// a landmark where the tracking that made it placed it: no graph in it
		private float3 RawPoint(Landmark l)
		{
			float3 inNode = (float3)l.reference.position + math.mul(l.reference.rotation, l.bearing) / l.inverseDepth;
			return track.CorrectionOf(l.node).ApplyInverse(inNode);
		}

		// its strongest features: only their rays and descriptors, which it has from the start
		private bool LoadNewer(VisualKeyframe keyframe)
		{
			if (!TryKey(keyframe.node, out keyB)) return false;
			FeatureSet features = keyframe.features;
			int rows = math.min(features.Count, NewerFeatures);
			NativeArray<half>.Copy(features.descriptors, 0, descriptorsB, 0, rows * FeatureSet.DescriptorSize);
			for (int j = 0; j < rows; j++) raysB[j] = keyframe.intrinsics.Ray(features.keypoints[j]);
			countB = rows;
			pathB = track.PathOf(keyframe.node);
			angularSigma = settings.pixelSigma / keyframe.intrinsics.fx;

			Pose raw = track.CorrectionOf(keyframe.node).Inverse.Apply(keyframe.cameraInNode);
			centerB = raw.position;
			rotationB = raw.rotation;
			for (int i = 0; i < rows; i++) raysB[i] = math.mul(rotationB, raysB[i]);
			return true;
		}

		private OlderView LoadOlder(VisualKeyframe keyframe)
		{
			if (olderViews.TryGetValue(keyframe, out OlderView view)) return view;
			List<int> features = new();
			List<float3> points = new();
			VisualMap map = localizer.Map;
			for (int j = 0; j < keyframe.features.Count && features.Count < pointsA.Length; j++)
			{
				if (!map.TryGetLandmark(keyframe.landmarks[j], out Landmark l) || !IsRanged(l)) continue;
				features.Add(j);
				points.Add(RawPoint(l));
			}

			view = new OlderView { features = features.ToArray(), points = points.ToArray() };
			olderViews[keyframe] = view;
			return view;
		}

		private void SchedulePair(VisualKeyframe older)
		{
			OlderView view = LoadOlder(older);
			if (view.features.Length < settings.minInliers || !TryKey(older.node, out keyA) || keyA == keyB) return;
			const int d = FeatureSet.DescriptorSize;
			for (int i = 0; i < view.features.Length; i++)
			{
				NativeArray<half>.Copy(older.features.descriptors, view.features[i] * d, descriptorsA, i * d, d);
				pointsA[i] = view.points[i];
			}

			PairsTried++;
			pairBegan = Time.realtimeSinceStartup;
			olderOfPair = older;
			featuresOfPair = view.features;
#if UNITY_EDITOR
			Vector3 toOlder = older.camera.position - newerCamera.camera.position;
			toOlder.y = 0;
			pairNote = FormattableString.Invariant($"{(track.IsOwn(older.node) ? 1 : 0)},{toOlder.magnitude:F2},{Vector3.Angle(older.camera.forward, newerCamera.camera.forward):F1},{Overlap(newerCamera, older)},{view.features.Length},{countB}");
#endif
			edgeHandle = new KeyframeEdgeJob
			{
				descriptorsA = descriptorsA, descriptorsB = descriptorsB,
				pointsA = pointsA, raysB = raysB,
				countA = view.features.Length, countB = countB,
				centerB = centerB, angularSigma = angularSigma,
				minInliers = settings.minInliers, maxYawSigma = math.radians(settings.maxYawSigmaDegrees),
				seed = seed++, result = edgeResult, inliersOfA = inliersOfA,
			}.Schedule();
			JobHandle.ScheduleBatchedJobs();
			edgeInFlight = true;
		}

		// ---- solving -------------------------------------------------------------

		private void UpdateSolve()
		{
			if (solving)
			{
				if (!solveHandle.IsCompleted) return;
				solveHandle.Complete();
				if (discardSolve)
				{
					EndSolve();
					return;
				}

				solveIteration++;
				if (solveIteration < settings.iterations && largestStep[0] > 1e-4) ScheduleStep();
				else FinishSolve();
				return;
			}

			if (edgesSinceSolve < settings.minNewEdges || Time.unscaledTime < nextSolve) return;
			BeginSolve();
		}

		// how far a knot moves before anyone else hears of it, meters and radians
		private const float MinTold = 0.003f, MinToldYaw = 0.0002f;

		// The system's cost grows with the cube of its knots, and a long game's would stall a
		// headset's worker for seconds. Past the limit the older half is solved in groups along each
		// walk, each as one rigid piece. Every knot keeps its own place and its name on other
		// headsets, and moves with its group
		private void GroupKnots()
		{
			if (groupOf.Length < solveKnotCount)
			{
				groupOf = new int[solveKnotCount * 2];
				groupFirst = new int[solveKnotCount * 2];
				groupSize = new int[solveKnotCount * 2];
				groupWas = new double4[solveKnotCount * 2];
			}

			int limit = math.max(settings.maxSolveKnots, 32), older = solveKnotCount - limit / 2;
			int perGroup = older > 0 ? (older + limit / 2 - 1) / (limit / 2) : 1;
			if (solveKnotCount <= limit) perGroup = 1;
			solveGroupCount = 0;
			for (int k = 0; k < solveKnotCount; k++)
			{
				int previous = knots[k].previous;
				if (perGroup > 1 && k < older && previous >= 0 && groupSize[groupOf[previous]] < perGroup)
				{
					groupOf[k] = groupOf[previous];
					groupSize[groupOf[k]]++;
					continue;
				}

				groupOf[k] = solveGroupCount;
				groupFirst[solveGroupCount] = k;
				groupSize[solveGroupCount++] = 1;
			}
		}

		// an edge as its knots' groups see it: its points and yaw in the frame of each group's first knot
		private KeyframeEdge InGroups(KeyframeEdge edge)
		{
			int groupB = groupOf[edge.knotB];
			float4 b = (float4)state[edge.knotB], firstB = (float4)state[groupFirst[groupB]];
			edge.pointB = Pose4.RotateY(Pose4.RotateY(edge.pointB, b.w) + b.xyz - firstB.xyz, -firstB.w);
			edge.yaw -= b.w - firstB.w;
			edge.knotB = groupB;
			int groupA = groupOf[edge.knotA];
			float4 a = (float4)state[edge.knotA], firstA = (float4)state[groupFirst[groupA]];
			edge.pointA = Pose4.RotateY(Pose4.RotateY(edge.pointA, a.w) + a.xyz - firstA.xyz, -firstA.w);
			edge.yaw += a.w - firstA.w;
			edge.knotA = groupA;
			return edge;
		}

		// a copy of the graph: the walk and the matches go on adding to it meanwhile
		private void BeginSolve()
		{
			edgesSinceSolve = 0;
			solveKnotCount = knots.Length;
			solveEdgeCount = edges.Length;
			GroupKnots();
			solveKnots = new NativeArray<KeyframeGraphKnot>(solveGroupCount, Allocator.Persistent);
			solveEdges = new NativeArray<KeyframeEdge>(solveEdgeCount, Allocator.Persistent);
			solveState = new NativeArray<double4>(solveGroupCount, Allocator.Persistent);
			for (int g = 0; g < solveGroupCount; g++)
			{
				KeyframeGraphKnot group = knots[groupFirst[g]];
				group.previous = group.previous >= 0 ? groupOf[group.previous] : -1;
				group.next = -1;
				solveKnots[g] = group;
				solveState[g] = state[groupFirst[g]];
			}

			for (int k = 0; k < solveKnotCount; k++)
				if (knots[k].isFixed && !solveKnots[groupOf[k]].isFixed)
				{
					KeyframeGraphKnot group = solveKnots[groupOf[k]];
					group.isFixed = true;
					solveKnots[groupOf[k]] = group;
				}

			for (int e = 0; e < solveEdgeCount; e++)
			{
				KeyframeEdge edge = edges[e];
				ownEdgesSolved |= KnotKeys.Owner(keys[edge.knotB]) == Owner || KnotKeys.Owner(keys[edge.knotA]) == Owner;
				solveEdges[e] = InGroups(edge);
			}

			solving = true;
			discardSolve = false;
			solveIteration = 0;
			solveBegan = Time.realtimeSinceStartup;
			ScheduleStep();
		}

		private void ScheduleStep()
		{
			DriftModel model = track.Model;
			solveHandle = new KeyframeGraphSolveJob
			{
				knots = solveKnots,
				edges = solveEdges,
				state = solveState,
				largestStep = largestStep,
				positionRateSquared = (double)settings.driftRate * settings.driftRate,
				yawRateSquared = (double)model.yawRate * model.yawRate,
				positionFloorSquared = 0.005 * 0.005,
				yawFloorSquared = math.square(math.radians(0.02)),
				huber = settings.huber,
			}.Schedule();
			JobHandle.ScheduleBatchedJobs();
		}

		private void FinishSolve()
		{
			float moved = 0;
			bool telling = SyncBus.Active;
			for (int g = 0; g < solveGroupCount; g++) groupWas[g] = state[groupFirst[g]];
			for (int k = 0; k < solveKnotCount; k++)
			{
				float3 origin = knots[k].origin;
				int group = groupOf[k];
				double4 before = state[k], was = groupWas[group], solved = solveState[group];
				// its group's move, about wherever its group was
				double turned = solved.w - was.w;
				math.sincos(turned, out double sine, out double cosine);
				double3 offset = before.xyz - was.xyz;
				double4 after = new(cosine * offset.x + sine * offset.z + solved.x, offset.y + solved.y, -sine * offset.x + cosine * offset.z + solved.z, before.w + turned);
				float4 now = (float4)after;
				float3 placedNow = Pose4.RotateY(origin, now.w) + now.xyz;
				moved = math.max(moved, math.distance(Pose4.RotateY(origin, (float)before.w) + (float3)before.xyz, placedNow));
				state[k] = after;
				ulong key = keys[k];
				if (KnotKeys.Owner(key) != Owner) track.SetOtherWorld(KnotKeys.Node(key), new Pose4(now.xyz, now.w));

				// everyone hears of what moved enough to matter
				float4 sent = sentPoses[k];
				if (telling && (math.distancesq(Pose4.RotateY(origin, sent.w) + sent.xyz, placedNow) > MinTold * MinTold || math.abs(sent.w - now.w) > MinToldYaw))
				{
					solvedPoses.Add(new PoseMessage { key = key, transform = now });
					sentPoses[k] = now;
				}
			}

			if (ownEdgesSolved && lastOwnKnot >= 0 && lastOwnKnot < solveKnotCount)
			{
				tipKnot = lastOwnKnot;
				tipGeneration = lastKnotGeneration;
			}

			LastSolveMoved = moved;
			LastSolveSeconds = Time.realtimeSinceStartup - solveBegan;
			Solves++;
			track.ApplyGraph(this);
			localizer.Map.RefreshPlacement();
			EndSolve();
			// a long session's system grows: it never takes more than a tenth of a worker, and far less than the frames this counts
			nextSolve = Time.unscaledTime + math.max(settings.solveInterval, 10f * LastSolveSeconds);
		}

		private void EndSolve()
		{
			DisposeSolve();
			solving = false;
			discardSolve = false;
		}
	}
}
