using System;
using System.Collections.Generic;
using System.IO;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.DepthKit.EnvScanning.Slam;
using Anaglyph.XR.VisualLocalization;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Multiplayer pose-graph SLAM over the shared environment scan, keeping every
	/// headset aligned to the room and to each other as their tracking drifts.
	///
	/// One graph, replicated on every headset (<see cref="PoseGraph.Shared"/>). Each
	/// headset adds its own trajectory: a node every chunk of travel, in its own tracking
	/// (odometry) frame and in the shared frame. Loop closures are found by whoever
	/// sees them, against any player's nodes: chunks of the map anchored to nodes too
	/// far away in expected drift are locked (frames do not integrate into them), and
	/// when the local sensor keeps landing in locked chunks the fresh view is scanned
	/// into a scratch volume and registered against them in a window sized by that
	/// drift. Two agreeing registrations make an edge. The session authority solves and
	/// broadcasts node poses plus the chunk groups that move; every headset moves those
	/// chunks identically (meshes at once, TSDF data resampled over the next frames)
	/// and eases its own rig onto its active node's correction. Frames, avatars and
	/// anchored objects carry node-relative poses, so a solve moves them with the map.
	/// </summary>
	public class SharedSlam : MonoBehaviour, IKeyframeAnchoring
	{
		public static SharedSlam Instance { get; private set; }

		// Sizes below scale with the scanner: lengths in chunks (chunk world size) or voxels,
		// areas in chunk faces (chunk world size², converted to surface samples by
		// TsdfRegistration.SurfaceSamples), per-frame counts as a fraction of the depth frame

		[Header("Nodes")]
		[Tooltip("Odometry travel between nodes, in chunks: chunk groups move with their node, so finer than a chunk buys nothing")]
		[SerializeField] private float nodeSpacingChunks = 1f;
		[Tooltip("Turn that also starts a node. Tracking drifts with distance, not with looking around")]
		[SerializeField] private float nodeYawDegrees = 90f;

		[Header("Trust")]
		// how far the headset can travel before its tracking can't be trusted to have held.
		// Quest tracking holds within a space and drifts over long walks, from about 20 m.
		// Frames fuse into any chunk anchored within the drift that much travel accumulates
		// (PoseGraph.DriftPerMeter × this) and lock out of chunks beyond it, which is where
		// loop closure begins. The drift rate itself is left alone: once tracking does
		// drift, the solve's weights and the search window need its true size
		[Tooltip("Travel, in meters, before the headset's tracking may have drifted: frames lock out of chunks anchored further away, and loop closure takes over")]
		[SerializeField] private float trustedTravelMeters = 20f;
		[Tooltip("A new segment attaches to the map anchored within this radius of the head, in chunks")]
		[SerializeField] private float attachRadiusChunks = 1.33f;
		[SerializeField] private float attachSigma = 0.05f;
		[SerializeField] private float attachSigmaDegrees = 2f;

		[Header("Loop closure")]
		// off while visual loop closure (VisualColocationConstraintProvider, through ProposeVisualLoop) is the
		// mechanism: locked chunks still keep a drifted view out of the old map, but no
		// scratch scan is registered against them
		[SerializeField] private bool scanLoopClosure = false;
		[Tooltip("Fraction of the sensor depth texture's pixels landing in locked chunks before a candidate opens")]
		[SerializeField] private float minLockedFrameFraction = 0.015f;
		[SerializeField] private int candidateFrames = 3;
		[SerializeField] private int3 scratchChunks = new(6, 4, 6);
		[SerializeField] private int minScratchChunks = 3;
		// coverage and acceptance are the colocation provider's gates
		// (ScanRegistrationColocationConstraintProvider.Gates), so a loop closure is held to
		// the same bar as colocation and relocalization
		[SerializeField] private float attemptInterval = 1f;
		[SerializeField] private float candidateTimeout = 20f;
		[SerializeField] private float candidateIdleTimeout = 3f;
		[SerializeField] private float windowScale = 3f;
		[SerializeField] private float minWindowVoxels = 3f;
		[SerializeField] private float maxWindowChunks = 1f;
		[SerializeField] private float minWindowDegrees = 5f;
		[SerializeField] private float maxWindowDegrees = 30f;
		[SerializeField] private float confirmVoxels = 0.8f;
		[SerializeField] private float confirmDegrees = 1.5f;
		[SerializeField] private float loopSigmaDegrees = 1f;

		[Header("Solve")]
		[SerializeField] private float solveDelay = 0.25f;
		[SerializeField] private int solveIterations = 15;
		[SerializeField] private float huberThreshold = 3f;
		[Tooltip("χ² of a loop or attach edge after the solve above which it is rejected as a false match")]
		[SerializeField] private float rejectChi2 = 25f;
		[Tooltip("A chunk group's accumulated move, in voxels at a one-chunk lever arm, before its data is resampled")]
		[SerializeField] private float resampleThresholdVoxels = 0.2f;

		// Direct events: graph additions are per-sender ordered with that sender's frames,
		// which reference its nodes, on the same reliable channel
		private readonly SyncEventBytes nodeEvent = new("slam.node", EventRoute.Direct);
		private readonly SyncEventBytes edgeEvent = new("slam.edge", EventRoute.Direct);
		private readonly SyncEventBytes solveEvent = new("slam.solve", EventRoute.Direct);
		private readonly SyncEventBytes snapshotEvent = new("slam.snapshot", EventRoute.Direct);
		private readonly SyncEvent<int> snapshotRequest = new("slam.snapshot.request", EventRoute.ToAuthority);

		private PoseGraph Graph => PoseGraph.Shared;

		private float ChunkSize => scanner.ChunkWorldSizeDim;
		private float VoxelSize => scanner.VoxSize;
		private float NodeSpacing => nodeSpacingChunks * ChunkSize;
		private float LockDrift => Graph.DriftPerMeter * trustedTravelMeters;
		private float AttachRadius => attachRadiusChunks * ChunkSize;
		// locked hits are counted per sensor depth pixel, so the floor follows the depth resolution
		private float MinLockedSamples
		{
			get
			{
				DepthKitDriver dkd = DepthKitDriver.Instance;
				Texture depth = dkd != null ? dkd.DepthTex : null;
				return depth != null ? minLockedFrameFraction * depth.width * depth.height : float.PositiveInfinity;
			}
		}
		private float MinWindowMeters => minWindowVoxels * VoxelSize;
		private float MaxWindowMeters => maxWindowChunks * ChunkSize;
		private float ConfirmMeters => confirmVoxels * VoxelSize;
		private float ResampleThreshold => resampleThresholdVoxels * VoxelSize;
		private ChunkMover mover;
		private EnvScanner scanner;

		// this run's player id in node keys: random so that keys made offline, before a
		// session assigns client ids, never collide with anyone's
		private uint playerTag;
		private uint nextSequence = 1;
		private uint nextEdgeSequence = 1;
		private readonly Dictionary<ulong, uint> playerOfClient = new();

		private bool segmentOpen;
		private int segmentGeneration = -1;
		private ulong activeNode;
		private bool graphSynced;
		private float lastSnapshotRequest = float.NegativeInfinity;

		// local sensor captures, so a frame sent a few frames later still carries the node
		// pose it was captured under
		private readonly Queue<(int tag, ulong node, Pose4 world)> captures = new();
		private int captureTag;

		// lock masks: the local sensor's, and one per node remote frames came from
		private uint[] sensorLocks;
		private int sensorLocksRevision;
		private readonly Dictionary<ulong, (uint[] bits, int revision)> nodeLocks = new();
		private uint[] lockScratch;

		// host
		private bool solveRequested;
		private float solveRequestTime;
		private bool solving;
		private int solveGeneration;
		private readonly Dictionary<ulong, Pose4> pendingAnchorDelta = new();

		// large payloads (solves, snapshots) travel in parts
		private const int MaxPartBytes = 48000;
		private uint nextMessageId = 1;
		private readonly Dictionary<(ulong sender, uint id), byte[][]> partial = new();

		public bool IsTracking { get; private set; }
		public ulong ActiveNode => activeNode;
		public uint PlayerTag => playerTag;

		/// <summary>Raised when a loop or attach edge this headset found is proposed.</summary>
		public event Action<PoseEdge> EdgeProposed = delegate { };

		private void Awake()
		{
			Instance = this;
			playerTag = (uint)UnityEngine.Random.Range(1, int.MaxValue);

			nodeEvent.Register(); nodeEvent.Received += OnNodeReceived;
			edgeEvent.Register(); edgeEvent.Received += OnEdgeReceived;
			solveEvent.Register(); solveEvent.Received += OnSolveReceived;
			snapshotEvent.Register(); snapshotEvent.Received += OnSnapshotReceived;
			snapshotRequest.Register(); snapshotRequest.Received += OnSnapshotRequested;

			SyncBus.Activated += OnBusChanged;
			SyncBus.Deactivated += OnBusChanged;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			Graph.EdgeAdded += OnGraphEdgeAdded;
		}

		private void Start()
		{
			scanner = EnvScanner.Instance;
			if (scanner == null) return;
			scanner.Cleared += OnScanCleared;
			scanner.SensorIntegrated += OnSensorIntegrated;
		}

		private void OnDestroy()
		{
			nodeEvent.Received -= OnNodeReceived; nodeEvent.Unregister();
			edgeEvent.Received -= OnEdgeReceived; edgeEvent.Unregister();
			solveEvent.Received -= OnSolveReceived; solveEvent.Unregister();
			snapshotEvent.Received -= OnSnapshotReceived; snapshotEvent.Unregister();
			snapshotRequest.Received -= OnSnapshotRequested; snapshotRequest.Unregister();

			SyncBus.Activated -= OnBusChanged;
			SyncBus.Deactivated -= OnBusChanged;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			Graph.EdgeAdded -= OnGraphEdgeAdded;

			if (scanner != null)
			{
				scanner.Cleared -= OnScanCleared;
				scanner.SensorIntegrated -= OnSensorIntegrated;
				scanner.SensorChunkLocks = null;
				scanner.SensorCorrection = Matrix4x4.identity;
				if (candidate != null && scanner.AuxVolume == candidate.scratch) scanner.AuxVolume = null;
			}

			CloseCandidate("destroyed");
			registration?.Dispose();
			target?.Dispose();
			mover?.Dispose();
			if (Instance == this) Instance = null;
		}

		// ---- lifecycle ---------------------------------------------------------

		/// <summary>The graph is the scan's: a cleared scan starts a new one.</summary>
		private void OnScanCleared()
		{
			Graph.Clear();
			mover?.Clear();
			pendingAnchorDelta.Clear();
			nodeLocks.Clear();
			partial.Clear();
			captures.Clear();
			segmentOpen = false;
			activeNode = 0;
			graphSynced = false;
			lastSnapshotRequest = float.NegativeInfinity;
			solveRequested = false;
			solveGeneration++;
			CloseCandidate("scan cleared");
		}

		private void OnBusChanged()
		{
			partial.Clear();
			playerOfClient.Clear();
			graphSynced = false;
			lastSnapshotRequest = float.NegativeInfinity;
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			pendingAnchorDelta.Clear();
			if (isAuthority && Graph.NodeCount > 0) RequestSolve();
		}

		// whichever map-matching provider aligns this headset: scan registration or visual
		private CanonColocationProvider Provider => CanonColocationProvider.Active;

		private bool CanTrack()
		{
			var provider = Provider;
			var coordinator = LaserTagMapCoordinator.Instance;
			return scanner != null && MainXRRig.Instance != null && provider != null &&
				provider.CurrentPhase == CanonColocationProvider.Phase.Aligned &&
				ColocationManager.IsColocated && coordinator != null && coordinator.CanIntegrateEnvironment &&
				(SyncBus.IsAuthority || graphSynced);
		}

		private void Update()
		{
			if (scanner == null) return;
			mover ??= new ChunkMover(scanner);
			mover.Tick();

			if (SyncBus.Active && !SyncBus.IsAuthority && !graphSynced && Time.unscaledTime - lastSnapshotRequest > 3f)
			{
				lastSnapshotRequest = Time.unscaledTime;
				snapshotRequest.Raise(0);
			}

			IsTracking = CanTrack();
			if (!IsTracking)
			{
				if (segmentOpen) Debug.Log($"[{nameof(SharedSlam)}] segment closed");
				segmentOpen = false;
				CloseCandidate("not tracking");
				scanner.SensorCorrection = Matrix4x4.identity;
			}
			else
			{
				UpdateTrajectory();
				ApplyCorrection();
				UpdateCandidate();
			}

			UpdateSensorLocks();

			if (SyncBus.IsAuthority && solveRequested && !solving && Time.unscaledTime - solveRequestTime >= solveDelay)
				Solve();
		}

		// ---- trajectory --------------------------------------------------------

		private Pose CanonSpacePose => Provider.CanonSpacePose;

		/// <summary>The head in this headset's odometry frame: its tracking under the colocation alignment, before any SLAM correction.</summary>
		private Pose4 OdometryHead()
		{
			Pose canon = CanonSpacePose;
			Matrix4x4 a = Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one);
			Matrix4x4 head = a * MainXRRig.TrackingSpace.worldToLocalMatrix * MainXRRig.Camera.transform.localToWorldMatrix;
			return Pose4.FromMatrix(head);
		}

		private void UpdateTrajectory()
		{
			Pose4 odometry = OdometryHead();
			int generation = Provider.AlignmentGeneration;

			// a new alignment (colocation, relocalization) already includes whatever the map
			// had been corrected by: it starts a segment with no correction of its own
			if (!segmentOpen || generation != segmentGeneration || !Graph.TryGetNode(activeNode, out PoseNode active))
			{
				StartSegment(odometry, generation);
				return;
			}

			Pose4 step = Pose4.Between(active.odometry, odometry);
			if (math.length(step.translation) < NodeSpacing && math.abs(math.degrees(step.yaw)) < nodeYawDegrees) return;

			CreateNode(active.key, false, odometry, active.Correction * odometry, false);
		}

		private void StartSegment(Pose4 odometry, int generation)
		{
			segmentGeneration = generation;
			segmentOpen = true;
			Provider.SlamCorrection = Pose4.Identity;

			bool gauge = Graph.NodeCount == 0 && SyncBus.IsAuthority;
			ulong attachTo = gauge ? 0 : FindAttachNode(odometry.translation);

			CreateNode(0, true, odometry, odometry, gauge);

			// a scan made before the graph existed moves with the session's first node
			if (gauge && EnvMesher.Instance != null)
				foreach (Chunk chunk in EnvMesher.Instance.ChunksList)
					if (!Graph.TryGetAnchor(chunk.chunkIndex, out _))
						Graph.SetAnchor(chunk.chunkIndex, activeNode);

			if (attachTo != 0 && Graph.TryGetNode(attachTo, out PoseNode anchor) && Graph.TryGetNode(activeNode, out PoseNode first))
			{
				PoseEdge edge = new()
				{
					id = NextEdgeId(), from = anchor.key, to = first.key,
					measurement = Pose4.Between(anchor.world, first.world),
					sigmaTranslation = attachSigma, sigmaYaw = math.radians(attachSigmaDegrees), kind = EdgeKind.Attach
				};
				ProposeEdge(edge);
			}

			Debug.Log($"[{nameof(SharedSlam)}] segment started at {odometry}{(gauge ? " (session gauge)" : attachTo != 0 ? $", attached to node {Describe(attachTo)}" : ", floating: nothing to attach to")}");
		}

		/// <summary>The node most of the map around the head is anchored to; the nearest node failing that.</summary>
		private ulong FindAttachNode(float3 head)
		{
			Dictionary<ulong, int> votes = new();
			float r2 = AttachRadius * AttachRadius;
			foreach ((int chunk, ulong anchor) in Graph.ChunkAnchors)
			{
				if (!Graph.Contains(anchor)) continue;
				float3 center = scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(chunk)) + scanner.ChunkWorldSizeDim * 0.5f;
				if (math.distancesq(center, head) > r2) continue;
				votes.TryGetValue(anchor, out int n);
				votes[anchor] = n + 1;
			}

			ulong best = 0;
			int bestVotes = 0;
			foreach ((ulong anchor, int n) in votes)
				if (n > bestVotes) { best = anchor; bestVotes = n; }

			if (best != 0) return best;

			PoseNode nearest = Graph.Nearest(head);
			return nearest != null && math.distance(nearest.world.translation, head) < AttachRadius * 2.5f ? nearest.key : 0;
		}

		private void CreateNode(ulong previous, bool segmentStart, Pose4 odometry, Pose4 world, bool prior)
		{
			ulong key = PoseGraph.MakeKey(playerTag, nextSequence++);
			if (!Graph.AddNode(key, previous, segmentStart, odometry, world, prior)) return;
			activeNode = key;

			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			PoseGraph.WriteNode(w, Graph.TryGetNode(key, out PoseNode node) ? node : null);
			nodeEvent.Raise(ms.ToArray());
		}

		private ulong NextEdgeId() => PoseGraph.MakeKey(playerTag, 0x80000000u | nextEdgeSequence++);

		private void ProposeEdge(PoseEdge edge)
		{
			if (!Graph.AddEdge(edge)) return;

			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			PoseGraph.WriteEdge(w, edge);
			edgeEvent.Raise(ms.ToArray());
			EdgeProposed.Invoke(edge);
		}

		private static string Describe(ulong key) => $"{PoseGraph.PlayerOf(key):X8}:{PoseGraph.SequenceOf(key)}";

		// ---- correction --------------------------------------------------------

		/// <summary>
		/// Eases the rig onto the active node's correction through the colocation
		/// constraint, and tells the scanner where the rig is going so the scan and the
		/// frames sent from it stay in the corrected frame meanwhile
		/// </summary>
		private void ApplyCorrection()
		{
			if (!Graph.TryGetNode(activeNode, out PoseNode active)) return;

			Pose4 correction = active.Correction;
			Provider.SlamCorrection = correction;

			Pose target = correction.Apply(CanonSpacePose);
			Matrix4x4 goal = Matrix4x4.TRS(target.position, target.rotation, Vector3.one);
			scanner.SensorCorrection = goal * MainXRRig.TrackingSpace.worldToLocalMatrix;
		}

		/// <summary>
		/// The transform from a player's odometry frame to the shared frame now: their
		/// active node's correction. Identity for a player with no nodes
		/// </summary>
		public Pose4 CorrectionOfClient(ulong clientId)
		{
			// the local player's own latest node, exactly as every peer resolves it
			if (clientId == SyncBus.LocalClientId)
				return Graph.TryGetLatest(playerTag, out PoseNode own) ? own.Correction : Pose4.Identity;

			if (playerOfClient.TryGetValue(clientId, out uint player) && Graph.TryGetLatest(player, out PoseNode node))
				return node.Correction;

			return Pose4.Identity;
		}

		// ---- frames ------------------------------------------------------------

		private void OnSensorIntegrated()
		{
			ulong node = IsTracking ? activeNode : 0;
			Pose4 world = Graph.TryGetNode(node, out PoseNode n) ? n.world : Pose4.Identity;
			captureTag++;
			captures.Enqueue((captureTag, node, world));
			while (captures.Count > 32) captures.Dequeue();
			scanner.SensorFrameTag = captureTag;

			if (node == 0) return;

			scanner.RequestMarkedChunks(chunks => RecordAnchors(chunks, node));
			if (sensorLocks != null) scanner.RequestLockedHits((samples, bits) => OnLockedHits(samples, bits, node));
		}

		/// <summary>The node (and its pose then) a captured local frame belongs to; false when none.</summary>
		public bool TryGetCaptureNode(int tag, out ulong node, out Pose4 nodeWorld)
		{
			foreach ((int t, ulong n, Pose4 w) in captures)
			{
				if (t != tag) continue;
				node = n;
				nodeWorld = w;
				return n != 0;
			}

			node = 0;
			nodeWorld = Pose4.Identity;
			return false;
		}

		/// <summary>
		/// Turns a remote frame's node-relative pose into this replica's shared frame and
		/// gives the chunk locks that node's frames are subject to. False when the node is
		/// unknown here (not yet received), so the frame cannot be placed
		/// </summary>
		public bool PrepareRemoteFrame(ulong node, ref Vector3 position, ref Quaternion rotation, out uint[] locks, out int locksRevision)
		{
			locks = null;
			locksRevision = 0;
			if (!Graph.TryGetNode(node, out PoseNode n)) return false;

			position = n.world.Apply((float3)position);
			rotation = (Quaternion)n.world.Rotation * rotation;

			if (!nodeLocks.TryGetValue(node, out var cached)) cached = (new uint[scanner.LockWordCount], 0);
			if (BuildLocks(node, cached.bits)) cached.revision++;
			nodeLocks[node] = cached;
			if (nodeLocks.Count > 64) nodeLocks.Clear();

			locks = cached.bits;
			locksRevision = cached.revision;
			return true;
		}

		/// <summary>Records which chunks a remote frame from this node integrated into. Call right after integrating it.</summary>
		public void OnRemoteFrameIntegrated(ulong node)
		{
			if (node != 0 && scanner != null) scanner.RequestMarkedChunks(chunks => RecordAnchors(chunks, node));
		}

		// a chunk is anchored to the node of the latest frame that integrated into it: its
		// data is most recently that node's view, and nodes near it in the graph trust it
		private void RecordAnchors(NativeArray<int> chunks, ulong node)
		{
			if (!Graph.Contains(node)) return;
			foreach (int chunk in chunks)
				if (mover == null || !mover.IsPending(chunk))
					Graph.SetAnchor(chunk, node);
		}

		// ---- locks -------------------------------------------------------------

		// true when the contents changed
		private bool BuildLocks(ulong node, uint[] bits)
		{
			lockScratch ??= new uint[bits.Length];
			Graph.BuildLockMask(node, LockDrift, lockScratch);
			if (mover != null)
			{
				uint[] pending = mover.PendingBits;
				for (int i = 0; i < lockScratch.Length && i < pending.Length; i++) lockScratch[i] |= pending[i];
			}

			bool changed = false;
			for (int i = 0; i < bits.Length; i++)
			{
				if (bits[i] == lockScratch[i]) continue;
				bits[i] = lockScratch[i];
				changed = true;
			}

			return changed;
		}

		private void UpdateSensorLocks()
		{
			bool any = IsTracking && Graph.Contains(activeNode) && (Graph.ChunkAnchors.Count > 0 || (mover != null && mover.HasPending));
			if (!any)
			{
				scanner.SensorChunkLocks = null;
				return;
			}

			sensorLocks ??= new uint[scanner.LockWordCount];
			if (BuildLocks(activeNode, sensorLocks)) sensorLocksRevision++;
			scanner.SensorChunkLocks = sensorLocks;
			scanner.SensorChunkLocksRevision = sensorLocksRevision;
		}

		// ---- keyframe anchoring (visual localization) ----------------------------

		/// <summary>
		/// The active node and the camera relative to it, through the same odometry frame
		/// the node was made in. False when not tracking, or before the segment a new
		/// alignment opens has its first node
		/// </summary>
		public bool TryAnchor(Pose cameraInTrackingSpace, out ulong node, out Pose cameraInNode)
		{
			node = 0;
			cameraInNode = default;
			var provider = Provider;
			if (!IsTracking || !segmentOpen || provider == null || segmentGeneration != provider.AlignmentGeneration ||
			    !Graph.TryGetNode(activeNode, out PoseNode active)) return false;

			Pose canon = CanonSpacePose;
			Matrix4x4 odometry = Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one) *
			                     Matrix4x4.TRS(cameraInTrackingSpace.position, cameraInTrackingSpace.rotation, Vector3.one);
			node = active.key;
			cameraInNode = new Pose(active.odometry.ApplyInverse(odometry.GetPosition()),
				(Quaternion)math.conjugate(active.odometry.Rotation) * odometry.rotation);
			return true;
		}

		public bool TryGetNodeWorld(ulong node, out Pose world)
		{
			world = default;
			if (!Graph.TryGetNode(node, out PoseNode n)) return false;
			world = n.world.AsPose;
			return true;
		}

		public float Drift(ulong a, ulong b) => a == b ? 0 : Graph.Drift(a, b);

		/// <summary>
		/// A visual loop closure: <paramref name="correction"/> maps where the graph has
		/// <paramref name="current"/> onto where the old map anchored at <paramref name="old"/>
		/// says it is. Proposed as a loop edge, which the authority's solve weighs against
		/// the rest of the graph. Yaw-only closures (far landmarks alone) carry a large
		/// translation sigma, and so move rotation without dragging position.
		/// </summary>
		public bool ProposeVisualLoop(ulong old, ulong current, Pose4 correction, float sigmaTranslation, float sigmaYaw)
		{
			if (!Graph.TryGetNode(old, out PoseNode m) || !Graph.TryGetNode(current, out PoseNode n) || old == current) return false;
			PoseEdge edge = new()
			{
				id = NextEdgeId(), from = m.key, to = n.key,
				measurement = Pose4.Between(m.world, correction * n.world),
				sigmaTranslation = math.max(Graph.LoopDrift, sigmaTranslation),
				sigmaYaw = math.max(math.radians(0.1f), sigmaYaw), kind = EdgeKind.Loop
			};
			ProposeEdge(edge);
			Debug.Log($"[{nameof(SharedSlam)}] visual loop closed: node {Describe(n.key)} → {Describe(m.key)}, correction {correction} (σ {edge.sigmaTranslation * 100:F1}cm / {math.degrees(edge.sigmaYaw):F2}°)");
			return true;
		}

		// ---- loop closure ------------------------------------------------------

		private sealed class Candidate
		{
			public EnvVolume scratch;
			public Pose4 correction;
			public float opened;
			public float lastHit;
			public float lastAttempt;
			public readonly Dictionary<int, int> hits = new();
			public Pose4? previous;
			public int attempts;
		}

		private Candidate candidate;
		private bool attemptInFlight;
		private int lockedStreak;
		private float candidateCooldownUntil;
		private readonly Dictionary<ulong, int> failuresByAnchor = new();
		private TsdfRegistration registration;
		private RegistrationVolume target;
		private (int revision, int count, int hash) targetKey;

		private void OnLockedHits(uint samples, NativeArray<uint> bits, ulong node)
		{
			if (!IsTracking) return;

			if (samples < MinLockedSamples)
			{
				lockedStreak = 0;
				return;
			}

			lockedStreak++;

			if (candidate == null && !scanLoopClosure) return;
			if (candidate == null)
			{
				if (lockedStreak < candidateFrames || Time.unscaledTime < candidateCooldownUntil || mover == null || mover.IsBusy) return;
				OpenCandidate();
			}

			candidate.lastHit = Time.unscaledTime;
			for (int w = 0; w < bits.Length; w++)
			{
				uint word = bits[w];
				while (word != 0)
				{
					int bit = math.tzcnt(word);
					word &= word - 1;
					int chunk = w * 32 + bit;
					candidate.hits.TryGetValue(chunk, out int n);
					candidate.hits[chunk] = n + 1;
				}
			}
		}

		private void OpenCandidate()
		{
			Graph.TryGetNode(activeNode, out PoseNode active);
			candidate = new Candidate
			{
				scratch = scanner.CreateVolume(scratchChunks, "Loop Closure Scratch"),
				correction = active?.Correction ?? Pose4.Identity,
				opened = Time.unscaledTime,
				lastHit = Time.unscaledTime,
				lastAttempt = Time.unscaledTime
			};
			scanner.AuxVolume = candidate.scratch;
			Debug.Log($"[{nameof(SharedSlam)}] loop closure candidate: the view keeps landing in locked chunks; scanning it fresh");
		}

		private void CloseCandidate(string why)
		{
			if (candidate == null) return;
			if (scanner != null && scanner.AuxVolume == candidate.scratch) scanner.AuxVolume = null;
			candidate.scratch?.Dispose();
			candidate = null;
			lockedStreak = 0;
			if (why != null) Debug.Log($"[{nameof(SharedSlam)}] loop closure candidate closed: {why}");
		}

		private void UpdateCandidate()
		{
			if (candidate == null) return;

			// the scratch scan was made under the correction in force when it opened; a
			// solve that moved this headset's frame invalidates it
			if (Graph.TryGetNode(activeNode, out PoseNode active) &&
			    !Pose4.Approximately(active.Correction, candidate.correction, 0.002f, math.radians(0.05f)))
			{
				CloseCandidate("a solve moved this headset's frame");
				return;
			}

			float now = Time.unscaledTime;
			if (now - candidate.opened > candidateTimeout || now - candidate.lastHit > candidateIdleTimeout)
			{
				ulong anchor = DominantAnchor(candidate);
				failuresByAnchor.TryGetValue(anchor, out int failures);
				failuresByAnchor[anchor] = failures + 1;
				candidateCooldownUntil = now + math.min(5f * (1 << math.min(failures, 3)), 40f);
				CloseCandidate(now - candidate.opened > candidateTimeout ? "timed out" : "the locked region left view");
				return;
			}

			if (attemptInFlight || now - candidate.lastAttempt < attemptInterval || candidate.scratch.Chunks.Count < minScratchChunks) return;
			candidate.lastAttempt = now;
			Attempt(candidate);
		}

		private ulong DominantAnchor(Candidate c)
		{
			Dictionary<ulong, int> votes = new();
			foreach ((int chunk, int n) in c.hits)
			{
				if (!Graph.TryGetAnchor(chunk, out ulong anchor) || !Graph.Contains(anchor)) continue;
				votes.TryGetValue(anchor, out int v);
				votes[anchor] = v + n;
			}

			ulong best = 0;
			int bestVotes = 0;
			foreach ((ulong anchor, int v) in votes)
				if (v > bestVotes) { best = anchor; bestVotes = v; }
			return best;
		}

		private async void Attempt(Candidate c)
		{
			attemptInFlight = true;
			try
			{
				ulong node = activeNode;
				ulong anchor = DominantAnchor(c);
				if (anchor == 0 || !Graph.TryGetNode(node, out PoseNode n) || !Graph.TryGetNode(anchor, out PoseNode m)) return;
				ScanRegistrationColocationConstraintProvider registrar = ScanRegistrationColocationConstraintProvider.Instance;
				RegistrationGates gates = registrar != null ? registrar.Gates : null;
				if (gates == null) return;

				// the target: the locked chunks the view landed in, and their locked
				// neighbors; the old map as it stands, without the trusted parts that would
				// pull the fit toward identity
				List<int> chunks = TargetChunks(c, anchor, gates.MaxRefinedRms(scanner), out string composition);
				if (chunks.Count == 0) return;

				registration ??= new TsdfRegistration(TsdfRegistration.LoadShader(), scanner);
				target ??= new RegistrationVolume(TsdfRegistration.LoadShader(), scanner);
				registration.Target = target;

				// a retry against the same chunks of the same map reuses the target and its pyramid
				int hash = 17;
				foreach (int chunk in chunks) hash = hash * 31 + chunk;
				var key = (Graph.Revision, chunks.Count, hash);
				if (!target.IsAllocated || targetKey != key || (mover != null && mover.IsBusy))
				{
					int3 min = int.MaxValue, max = int.MinValue;
					foreach (int chunk in chunks)
					{
						int3 coord = scanner.ChunkIndexToChunkCoord(chunk);
						min = math.min(min, coord);
						max = math.max(max, coord);
					}

					if (!target.Allocate(min, max))
					{
						Debug.Log($"[{nameof(SharedSlam)}] loop closure: locked region spans {max - min + 1} chunks, more than a registration target holds");
						return;
					}

					target.FillFromAtlas(chunks);
					targetKey = key;
				}

				List<int> source = new(c.scratch.Chunks);
				if (!await registration.SetSourceChunksAsync(source, c.scratch) || candidate != c || !this) return;
				if (!gates.HasCoverage(registration.Constraint, scanner))
				{
					Debug.Log($"[{nameof(SharedSlam)}] loop closure: waiting for a view that constrains the fit, {registration.Constraint} < {gates.MinSourceConstraint(scanner):F0} ({source.Count} chunks)");
					return;
				}

				float drift = Graph.Drift(node, anchor);
				if (float.IsInfinity(drift)) drift = MaxWindowMeters / windowScale;
				float window = math.clamp(drift * windowScale, MinWindowMeters, MaxWindowMeters);
				float windowDegrees = math.clamp(math.degrees(drift / 2f) * windowScale, minWindowDegrees, maxWindowDegrees);
				float3 head = MainXRRig.Camera.transform.position;

				RegistrationResult result = await registration.RegisterAsync(Pose4.Identity, window, windowDegrees, head, false);
				if (candidate != c || !this) return;
				c.attempts++;

				string rejection = gates.Rejection(result, scanner);
				Debug.Log($"[{nameof(SharedSlam)}] loop closure attempt {c.attempts} against node {Describe(anchor)} (drift {drift * 100:F0}cm, window ±{window:F2}m ±{windowDegrees:F0}°, target {composition}): {result}{(rejection != null ? $" — {rejection}" : "")}");
				if (rejection != null) return;

				// one registration can be a confident mistake; two in a row that agree, as the
				// fresh scan grows, are not
				if (c.previous is not { } previous || !Pose4.Approximately(previous, result.pose, ConfirmMeters, math.radians(confirmDegrees)))
				{
					c.previous = result.pose;
					return;
				}

				// T maps the fresh view (this node's frame as the graph has it) onto the old
				// map (the anchor's): T∘X_n is where n belongs relative to m
				if (!Graph.TryGetNode(activeNode, out PoseNode now)) return;
				PoseEdge edge = new()
				{
					id = NextEdgeId(), from = m.key, to = now.key,
					measurement = Pose4.Between(m.world, result.pose * now.world),
					sigmaTranslation = math.max(Graph.LoopDrift, result.refinedRms),
					sigmaYaw = math.radians(loopSigmaDegrees), kind = EdgeKind.Loop
				};
				ProposeEdge(edge);
				failuresByAnchor.Remove(anchor);
				candidateCooldownUntil = Time.unscaledTime + 3f;
				Debug.Log($"[{nameof(SharedSlam)}] loop closed: node {Describe(now.key)} → {Describe(m.key)}, correction {result.pose}");
				CloseCandidate(null);
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				attemptInFlight = false;
			}
		}

		/// <summary>
		/// The locked chunks around where the view landed that agree with
		/// <paramref name="anchor"/>: its own, and those of nodes within
		/// <paramref name="tolerance"/> of it in expected drift, the residual a trusted
		/// registration may leave. Chunks laid down on another lap can sit a drift apart from
		/// these with nothing yet tying them together; one rigid fit can match only one of
		/// the two, and the fresh surfaces over the other land in what it saw as open air
		/// </summary>
		private List<int> TargetChunks(Candidate c, ulong anchor, float tolerance, out string composition)
		{
			List<int> result = new();
			composition = "empty";
			if (sensorLocks == null) return result;
			HashSet<int> set = new();
			HashSet<ulong> kept = new(), dropped = new();
			int droppedChunks = 0;

			foreach (int chunk in c.hits.Keys)
			{
				int3 coord = scanner.ChunkIndexToChunkCoord(chunk);
				for (int x = -1; x <= 1; x++)
				for (int y = -1; y <= 1; y++)
				for (int z = -1; z <= 1; z++)
				{
					int neighbor = scanner.ChunkCoordToChunkIndex(coord + new int3(x, y, z));
					if (neighbor < 0 || !set.Add(neighbor)) continue;
					bool locked = (sensorLocks[neighbor >> 5] & (1u << (neighbor & 31))) != 0;
					if (!locked || (mover != null && mover.IsPending(neighbor)) || !Graph.TryGetAnchor(neighbor, out ulong owner)) continue;
					if (owner != anchor && !(Graph.Drift(anchor, owner) <= tolerance))
					{
						dropped.Add(owner);
						droppedChunks++;
						continue;
					}

					kept.Add(owner);
					result.Add(neighbor);
				}
			}

			// the dense target holds a bounded box; keep what is nearest the head
			if (result.Count > 0)
			{
				int3 head = scanner.WorldPosToChunkCoord(MainXRRig.Camera.transform.position);
				int maxSpan = (RegistrationVolume.MaxDims.x - 2) / (scanner.VoxPerChunkDim - 2);
				int maxSpanY = (RegistrationVolume.MaxDims.y - 2) / (scanner.VoxPerChunkDim - 2);
				int half = maxSpan / 2, halfY = maxSpanY / 2;
				result.RemoveAll(chunk =>
				{
					int3 d = math.abs(scanner.ChunkIndexToChunkCoord(chunk) - head);
					return d.x >= half || d.z >= half || d.y >= halfY;
				});
			}

			composition = $"{result.Count} chunks of {kept.Count} nodes" +
				(droppedChunks > 0 ? $", {droppedChunks} chunks of {dropped.Count} nodes over {tolerance * 100:F0}cm drift away left out" : "");
			return result;
		}

		// ---- network: graph additions -------------------------------------------

		private void OnNodeReceived(ulong sender, byte[] payload)
		{
			if (sender == SyncBus.LocalClientId) return; // added when created
			try
			{
				using BinaryReader r = new(new MemoryStream(payload));
				var n = PoseGraph.ReadNode(r);
				playerOfClient[sender] = PoseGraph.PlayerOf(n.key);
				Graph.AddNode(n.key, n.previous, n.segmentStart, n.odometry, n.world, n.hasPrior);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SharedSlam)}] bad node payload from {sender}: {e.Message}");
			}
		}

		private void OnEdgeReceived(ulong sender, byte[] payload)
		{
			if (sender == SyncBus.LocalClientId) return;
			try
			{
				using BinaryReader r = new(new MemoryStream(payload));
				Graph.AddEdge(PoseGraph.ReadEdge(r));
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SharedSlam)}] bad edge payload from {sender}: {e.Message}");
			}
		}

		private void OnGraphEdgeAdded(PoseEdge edge)
		{
			if (edge.kind != EdgeKind.Odometry) RequestSolve();
		}

		private void RequestSolve()
		{
			if (!SyncBus.IsAuthority) return;
			solveRequested = true;
			solveRequestTime = Time.unscaledTime;
		}

		// ---- host: solving -----------------------------------------------------

		private async void Solve()
		{
			solving = true;
			solveRequested = false;
			int generation = solveGeneration;
			List<ulong> rejected = new();

			try
			{
				// solved, then re-solved without the worst inconsistent edge, a few times at most
				for (int round = 0; round < 4; round++)
				{
					PoseGraphOptimizer.Problem problem = Graph.BuildProblem(out List<ulong> nodeOrder, out List<ulong> edgeOrder);
					try
					{
						JobHandle handle = problem.CreateJob(solveIterations, huberThreshold).Schedule();
						while (!handle.IsCompleted) await Awaitable.NextFrameAsync();
						handle.Complete();
						if (!this || generation != solveGeneration) return;

						if (problem.result[PoseGraphOptimizer.ResultSucceeded] < 0.5f)
						{
							Debug.LogWarning($"[{nameof(SharedSlam)}] solve failed (singular system) over {nodeOrder.Count} nodes, {edgeOrder.Count} edges");
							return;
						}

						// the most inconsistent robust edge, if any is past the gate
						int worst = -1;
						float worstChi2 = rejectChi2;
						for (int e = 0; e < edgeOrder.Count; e++)
						{
							if (problem.edgeRobust[e] == 0 || problem.edgeChi2[e] <= worstChi2) continue;
							worst = e;
							worstChi2 = problem.edgeChi2[e];
						}

						if (worst >= 0 && round < 3)
						{
							ulong id = edgeOrder[worst];
							Graph.SetRejected(id, true);
							rejected.Add(id);
							Debug.Log($"[{nameof(SharedSlam)}] rejected edge {Describe(id)} (χ² {worstChi2:F1}) as inconsistent with the rest of the graph");
							continue;
						}

						Publish(problem, nodeOrder, rejected);
						return;
					}
					finally
					{
						problem.Dispose();
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				solving = false;
			}
		}

		private void Publish(PoseGraphOptimizer.Problem problem, List<ulong> nodeOrder, List<ulong> rejected)
		{
			List<(ulong key, Pose4 pose)> poses = new();
			for (int i = 0; i < nodeOrder.Count; i++)
			{
				if (!Graph.TryGetNode(nodeOrder[i], out PoseNode node)) continue;
				Pose4 solved = Pose4.FromPacked(problem.states[i]);
				if (Pose4.Approximately(node.world, solved, 0.0005f, math.radians(0.02f))) continue;
				poses.Add((node.key, solved));
			}

			// chunk groups: each anchor's move accumulates until it is worth resampling
			Dictionary<ulong, List<int>> chunksByAnchor = new();
			foreach ((int chunk, ulong anchor) in Graph.ChunkAnchors)
			{
				if (!chunksByAnchor.TryGetValue(anchor, out List<int> list)) chunksByAnchor[anchor] = list = new List<int>();
				list.Add(chunk);
			}

			List<(ulong anchor, Pose4 delta, int[] chunks)> groups = new();
			foreach ((ulong key, Pose4 pose) in poses)
			{
				if (!chunksByAnchor.TryGetValue(key, out List<int> chunks) || !Graph.TryGetNode(key, out PoseNode node)) continue;
				Pose4 delta = pose * node.world.Inverse;
				Pose4 pending = delta * (pendingAnchorDelta.TryGetValue(key, out Pose4 earlier) ? earlier : Pose4.Identity);

				if (pending.Magnitude(ChunkSize) < ResampleThreshold)
				{
					pendingAnchorDelta[key] = pending;
					continue;
				}

				pendingAnchorDelta.Remove(key);
				groups.Add((key, pending, chunks.ToArray()));
			}

			int version = Graph.Version + 1;
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(version);
			w.Write(rejected.Count);
			foreach (ulong id in rejected) w.Write(id);
			w.Write(poses.Count);
			foreach ((ulong key, Pose4 pose) in poses)
			{
				w.Write(key);
				PoseGraph.WritePose(w, pose);
			}

			w.Write(groups.Count);
			foreach ((ulong anchor, Pose4 delta, int[] chunks) in groups)
			{
				w.Write(anchor);
				PoseGraph.WritePose(w, delta);
				w.Write(chunks.Length);
				foreach (int chunk in chunks) w.Write(chunk);
			}

			byte[] payload = ms.ToArray();
			Debug.Log($"[{nameof(SharedSlam)}] solve v{version}: {poses.Count}/{nodeOrder.Count} nodes moved, {groups.Count} chunk groups resampled, {rejected.Count} edges rejected ({payload.Length}B)");

			ApplySolve(payload);
			SendLarge(solveEvent, payload, null);
		}

		private void OnSolveReceived(ulong sender, byte[] part)
		{
			if (sender == SyncBus.LocalClientId) return; // applied when published
			byte[] payload = Reassemble(sender, part);
			if (payload != null) ApplySolve(payload);
		}

		private void ApplySolve(byte[] payload)
		{
			try
			{
				using BinaryReader r = new(new MemoryStream(payload));
				int version = r.ReadInt32();
				int rejectedCount = r.ReadInt32();
				List<ulong> rejected = new(rejectedCount);
				for (int i = 0; i < rejectedCount; i++) rejected.Add(r.ReadUInt64());

				int poseCount = r.ReadInt32();
				List<(ulong, Pose4)> poses = new(poseCount);
				for (int i = 0; i < poseCount; i++) poses.Add((r.ReadUInt64(), PoseGraph.ReadPose(r)));

				int groupCount = r.ReadInt32();
				List<ChunkMover.Group> groups = new(groupCount);
				for (int g = 0; g < groupCount; g++)
				{
					ulong anchor = r.ReadUInt64();
					Pose4 delta = PoseGraph.ReadPose(r);
					int[] chunks = new int[r.ReadInt32()];
					for (int i = 0; i < chunks.Length; i++)
					{
						chunks[i] = r.ReadInt32();
						// the authority's anchors are the ones every map moves by
						Graph.SetAnchor(chunks[i], anchor);
					}

					groups.Add(new ChunkMover.Group(delta, chunks));
				}

				if (version <= Graph.Version && !SyncBus.IsAuthority) return;
				Graph.ApplySolve(version, poses, rejected);
				mover ??= new ChunkMover(scanner);
				mover.Enqueue(groups);
				nodeLocks.Clear();
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SharedSlam)}] bad solve payload: {e.Message}");
			}
		}

		// ---- snapshots ---------------------------------------------------------

		private void OnSnapshotRequested(ulong sender, int _)
		{
			if (sender == SyncBus.LocalClientId || !SyncBus.IsAuthority) return;

			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(Graph.Version);
			w.Write(Graph.NodeCount);
			foreach (PoseNode node in Graph.Nodes) PoseGraph.WriteNode(w, node);
			w.Write(Graph.EdgeCount);
			foreach (PoseEdge edge in Graph.Edges) PoseGraph.WriteEdge(w, edge);
			w.Write(Graph.ChunkAnchors.Count);
			foreach ((int chunk, ulong anchor) in Graph.ChunkAnchors)
			{
				w.Write(chunk);
				w.Write(anchor);
			}

			// whose client id is whose player, so the joiner can place avatars
			w.Write(playerOfClient.Count + 1);
			w.Write(SyncBus.LocalClientId);
			w.Write(playerTag);
			foreach ((ulong client, uint player) in playerOfClient)
			{
				w.Write(client);
				w.Write(player);
			}

			byte[] payload = ms.ToArray();
			Debug.Log($"[{nameof(SharedSlam)}] serving the pose graph to client {sender}: {Graph.NodeCount} nodes, {Graph.EdgeCount} edges ({payload.Length}B)");
			SendLarge(snapshotEvent, payload, sender);
		}

		private void OnSnapshotReceived(ulong sender, byte[] part)
		{
			if (sender == SyncBus.LocalClientId || SyncBus.IsAuthority) return;
			byte[] payload = Reassemble(sender, part);
			if (payload == null) return;

			try
			{
				using BinaryReader r = new(new MemoryStream(payload));
				int version = r.ReadInt32();
				int nodeCount = r.ReadInt32();
				var nodes = new List<(ulong, ulong, bool, bool, Pose4, Pose4)>(nodeCount);
				for (int i = 0; i < nodeCount; i++) nodes.Add(PoseGraph.ReadNode(r));
				int edgeCount = r.ReadInt32();
				List<PoseEdge> edges = new(edgeCount);
				for (int i = 0; i < edgeCount; i++) edges.Add(PoseGraph.ReadEdge(r));
				int anchorCount = r.ReadInt32();
				List<(int, ulong)> anchors = new(anchorCount);
				for (int i = 0; i < anchorCount; i++) anchors.Add((r.ReadInt32(), r.ReadUInt64()));
				int players = r.ReadInt32();
				for (int i = 0; i < players; i++)
				{
					ulong client = r.ReadUInt64();
					uint player = r.ReadUInt32();
					if (client != SyncBus.LocalClientId) playerOfClient[client] = player;
				}

				Graph.MergeSnapshot(version, nodes, edges, anchors);
				graphSynced = true;
				nodeLocks.Clear();
				Debug.Log($"[{nameof(SharedSlam)}] received the pose graph: {nodeCount} nodes, {edgeCount} edges, {anchorCount} chunk anchors (v{version})");
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[{nameof(SharedSlam)}] bad graph snapshot: {e.Message}");
			}
		}

		// ---- large payloads ----------------------------------------------------

		// [uint id][ushort part][ushort parts] then the part's bytes
		private void SendLarge(SyncEventBytes channel, byte[] payload, ulong? target)
		{
			if (!SyncBus.Active) return;
			uint id = nextMessageId++;
			int parts = math.max(1, (payload.Length + MaxPartBytes - 1) / MaxPartBytes);
			for (int p = 0; p < parts; p++)
			{
				int offset = p * MaxPartBytes;
				int length = math.min(MaxPartBytes, payload.Length - offset);
				byte[] part = new byte[8 + length];
				SyncBytes.Write(part, 0, id);
				SyncBytes.Write(part, 4, (ushort)p);
				SyncBytes.Write(part, 6, (ushort)parts);
				Buffer.BlockCopy(payload, offset, part, 8, length);
				if (target.HasValue) channel.RaiseTo(target.Value, part);
				else channel.Raise(part);
			}
		}

		private byte[] Reassemble(ulong sender, byte[] part)
		{
			if (part.Length < 8) return null;
			uint id = SyncBytes.Read<uint>(part, 0);
			int index = SyncBytes.Read<ushort>(part, 4);
			int count = SyncBytes.Read<ushort>(part, 6);
			if (count == 0 || index >= count) return null;

			byte[] body = new byte[part.Length - 8];
			Buffer.BlockCopy(part, 8, body, 0, body.Length);
			if (count == 1) return body;

			if (!partial.TryGetValue((sender, id), out byte[][] parts)) partial[(sender, id)] = parts = new byte[count][];
			parts[index] = body;
			foreach (byte[] p in parts) if (p == null) return null;

			partial.Remove((sender, id));
			int total = 0;
			foreach (byte[] p in parts) total += p.Length;
			byte[] whole = new byte[total];
			int offset = 0;
			foreach (byte[] p in parts)
			{
				Buffer.BlockCopy(p, 0, whole, offset, p.Length);
				offset += p.Length;
			}

			return whole;
		}
	}
}
