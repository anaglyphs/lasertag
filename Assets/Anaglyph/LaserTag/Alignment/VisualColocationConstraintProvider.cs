using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.DepthKit.EnvScanning.Slam;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Colocation, relocalization and loop closure from one camera landmark map per
	/// headset (<see cref="VisualLocalizer"/>, anchored to <see cref="SharedSlam"/>'s pose
	/// graph). Needs no scan of its own to colocate: a joiner's scan stays shut until it
	/// is aligned, and the authority's snapshot goes straight into it.
	///
	/// A joiner sends query frames to the authority, which localizes them against its map
	/// and replies with the transform from the joiner's world to canon. A headset
	/// relocalizing after a recenter queries its own map. A tracking headset keeps
	/// querying its own map for views from another stretch of tracking, the same place
	/// after a long walk, and turns what it finds into loop edges for the pose graph.
	///
	/// Every result is gated on how well it is pinned down (the solver's yaw and
	/// translation sigmas), not on how many points matched: a map seen from one spot pins
	/// yaw but not translation until someone walks around. Two results that agree are
	/// needed before one is applied.
	/// </summary>
	[RequireComponent(typeof(VisualLocalizer))]
	public class VisualColocationConstraintProvider : CanonColocationProvider
	{
		[SerializeField] private bool queriesEnabled = true;
		[SerializeField] private float queryInterval = 1f;
		[SerializeField] private float replyTimeout = 5f;

		[Header("Acceptance")]
		[SerializeField] private int minInliers = 30;
		[Tooltip("Inlier residuals' RMS in their own sigmas (pixel noise plus landmark depth uncertainty)")]
		[SerializeField] private float maxNormalizedRms = 2f;
		[SerializeField] private float maxYawSigmaDegrees = 0.5f;
		[SerializeField] private float maxTranslationSigma = 0.04f;
		[Header("Single-frame acceptance")]
		[Tooltip("A result this certain is applied without waiting for a second frame")]
		[SerializeField] private bool acceptConfidentSingleFrames = true;
		[SerializeField] private float confidentYawSigmaDegrees = 0.15f;
		[SerializeField] private float confidentTranslationSigma = 0.01f;
		[SerializeField] private int confidentInliers = 150;
		[Tooltip("Inliers a loop closure needs to be taken from one frame: its search is already confined to old views near where the graph puts the camera, and a moving player rarely gives a second frame of the same views in time")]
		[SerializeField] private int confidentLoopInliers = 80;
		[SerializeField] private float confidentLoopTranslationSigma = 0.02f;
		[SerializeField] private float confidentLoopYawSigmaDegrees = 0.2f;
		[Tooltip("Inliers as a share of the correspondences: repeated structure tends to give confident fits from a minority")]
		[SerializeField] private float confidentInlierShare = 0.6f;

		[Header("Agreement")]
		[SerializeField] private float agreeTranslation = 0.05f;
		[SerializeField] private float agreeYawDegrees = 1f;

		[Header("Loop closure")]
		[SerializeField] private bool loopClosure = true;
		[Tooltip("Seconds between loop closure queries, at most; none is made unless an old keyframe is near")]
		[SerializeField] private float loopInterval = 3f;
		[Tooltip("Seconds before retrying a query whose frame was skipped (the head turning too fast to capture sharply): a skipped frame doesn't use up the interval")]
		[SerializeField] private float blurredRetry = 0.25f;
		[Tooltip("Old keyframes farther than this (m) from where the graph puts the camera are not tried")]
		[SerializeField] private float loopSearchDistance = 8f;
		[Tooltip("How well the graph is assumed to know where the camera is before a loop closure, meters: the position prior far landmarks' yaw leans on")]
		[SerializeField] private float loopCenterPriorSigma = 1f;
		[Tooltip("A closure that pins yaw but not translation (far landmarks only) still becomes an edge, with translation left loose")]
		[SerializeField] private bool yawOnlyLoops = true;
		[SerializeField] private float loopCooldown = 3f;
		[Tooltip("A loop closure corrects the drift the graph expects between the two stretches of tracking: one moving the camera or turning it by more than this many times that drift (plus the floors) found the wrong place, such as the far end of a symmetric hallway")]
		[SerializeField] private float loopPlausibleSigmas = 3f;
		[SerializeField] private float loopPlausibleMeters = 0.5f;
		[SerializeField] private float loopPlausibleDegrees = 5f;
		[Tooltip("Least yaw sigma a loop edge carries, degrees: the solver's own runs several times too small against ground truth (0.6–0.9° errors at ±0.1–0.3°)")]
		[SerializeField] private float loopEdgeMinYawSigmaDegrees = 0.5f;
		[Tooltip("Features per side a loop query's LighterGlue runs attend over: cost grows with the square, and a loop closure needs far fewer than a cold relocalization")]
		[SerializeField] private int loopMatchFeatures = 512;
		[Tooltip("An old keyframe sharing fewer descriptor matches than this with the view isn't worth a LighterGlue run")]
		[SerializeField] private int loopMinMutual = 15;
		[Tooltip("Old keyframes facing more than this many degrees away from the view can't share it, and aren't tried")]
		[SerializeField] private float loopMaxViewAngle = 90f;
		[Tooltip("Every new keyframe made near old views is matched as a loop query at once, with the features mapping already extracted")]
		[SerializeField] private bool loopFromKeyframes = true;
		[Tooltip("Two loop results may lean on different old nodes, as long as those are this close (m of expected drift): the correction agreeing is the real test")]
		[SerializeField] private float loopAnchorTolerance = 0.25f;

		private readonly SyncEventBytes queryEvent = new("vloc.query", EventRoute.ToAuthority);
		private readonly SyncEventBytes replyEvent = new("vloc.reply", EventRoute.Direct);

		[StructLayout(LayoutKind.Sequential)]
		private struct Reply
		{
			public int queryId;
			public int success;
			public float yaw;
			public float3 translation;
			public int inliers;
			public int rangedInliers;
			public int correspondences;
			public float rmsPixels;
			public float normalizedRms;
			public float yawSigma;
			public float translationSigma;
			public int usableLandmarks;
		}

		private enum Purpose { Colocate, Relocalize, Loop }

		private class Pending
		{
			public VisualQuery query;
			public Purpose purpose;
			public int epoch;
			public int generation;
			public float sentAt;
			// the pose graph's solves when the query was placed in it: a loop correction is
			// relative to the node poses of that moment, and a solve since has moved them
			public int solves;
		}

		private readonly Dictionary<int, Pending> pending = new();
		private int nextQueryId = 1;
		private float nextQuery;
		private float nextLoop;
		private float nextQuietLoopLog;
		private bool queryInFlight;
		private int graphSolves;
		private bool serving;

		// the first of two results that must agree
		private Pending candidate;
		private Pose4 candidatePose;
		private float candidateTranslationSigma;
		private bool candidateYawOnly;
		private ulong candidateAnchor;

		private VisualLocalizer localizer;

		public static VisualColocationConstraintProvider Instance { get; private set; }

		/// <summary>
		/// Every loop query that solved: the old node, the query's node, the correction, the
		/// solve, and why it was turned down (null when it counted toward a closure). For
		/// tests that know the truth.
		/// </summary>
		public event System.Action<ulong, ulong, Pose4, GravityPnPResult, string> LoopSolved = delegate { };
		public VisualLocalizationResult LastResult { get; private set; }
		public string LastOutcome { get; private set; }

		// what every recent attempt came to, for AlignmentDiagnosis
		private const int OutcomeLogLength = 400;
		private readonly Queue<string> outcomes = new();

		/// <summary>Recent outcomes, oldest first, each stamped with the realtime it happened at.</summary>
		public IEnumerable<string> RecentOutcomes => outcomes;

		public override string LastAttemptDescription => LastOutcome ?? "none";

		public override bool SupportsSharedSlam => loopClosure;

		protected override bool HasMapToRelocalizeAgainst => localizer.Map.Count > 0;

		protected override void ResetWorkingState() => candidate = null;

		protected override void Awake()
		{
			Instance = this;
			localizer = GetComponent<VisualLocalizer>();
			base.Awake();
			queryEvent.Register();
			queryEvent.Received += OnQueryReceived;
			replyEvent.Register();
			replyEvent.Received += OnReplyReceived;
			localizer.KeyframeAdded += OnKeyframeAdded;
			PoseGraph.Shared.Solved += OnGraphSolved;
		}

		private void Start()
		{
			// the map's frame is gone when the scan (and the pose graph with it) is
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared += ClearMap;
			LaserTagMapCoordinator.WorldFrameRebased += ClearMap;
			PhaseChanged += OnPhaseChanged;
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			queryEvent.Received -= OnQueryReceived;
			queryEvent.Unregister();
			replyEvent.Received -= OnReplyReceived;
			replyEvent.Unregister();
			if (localizer) localizer.KeyframeAdded -= OnKeyframeAdded;
			PoseGraph.Shared.Solved -= OnGraphSolved;
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared -= ClearMap;
			LaserTagMapCoordinator.WorldFrameRebased -= ClearMap;
			PhaseChanged -= OnPhaseChanged;
			if (Instance == this) Instance = null;
		}

		private void ClearMap()
		{
			if (localizer.Map.Count > 0)
				Debug.Log($"{LogTag} map cleared ({localizer.Quality}): its frame is gone");
			localizer.Map.Clear();
			candidate = null;
		}

		// node poses moved: loop results and candidates measured against the old ones are stale
		private void OnGraphSolved(IReadOnlyDictionary<ulong, Pose4> _)
		{
			graphSolves++;
			if (candidate != null && candidate.purpose == Purpose.Loop) candidate = null;
		}

		private void OnPhaseChanged(Phase phase)
		{
			// searching means this world is not canon, whatever the map was built in
			if (phase == Phase.Searching) ClearMap();
			candidate = null;
		}

		// ---- asking ------------------------------------------------------------

		protected override void Update()
		{
			base.Update();

			// keyframes hang off the pose graph's nodes
			if (localizer.Anchoring == null && SharedSlam.Instance != null) localizer.Anchoring = SharedSlam.Instance;

			// replies that never came
			if (pending.Count > 0)
			{
				List<int> expired = null;
				foreach ((int id, Pending p) in pending)
					if (Time.unscaledTime - p.sentAt > replyTimeout) (expired ??= new()).Add(id);
				if (expired != null) foreach (int id in expired) pending.Remove(id);
			}

			if (!queriesEnabled || queryInFlight || pending.Count > 0 || Time.unscaledTime < nextQuery || !localizer.IsAvailable) return;

			// a headset waiting on an alignment runs the networks over fewer frames
			localizer.Urgent = CurrentPhase is Phase.Searching or Phase.Registering or Phase.Relocalizing;

			if (!NeedsQuery(out Purpose purpose, out bool remote)) return;

			nextQuery = Time.unscaledTime + queryInterval;
			if (purpose == Purpose.Loop) nextLoop = Time.unscaledTime + loopInterval;
			Query(purpose, remote);
		}

		private bool NeedsQuery(out Purpose purpose, out bool remote)
		{
			purpose = default;
			remote = SyncBus.Active && !SyncBus.IsAuthority;
			switch (CurrentPhase)
			{
				case Phase.Searching:
				case Phase.Registering:
					purpose = Purpose.Colocate;
					return remote;

				case Phase.Relocalizing:
					purpose = Purpose.Relocalize;
					remote = false;
					return localizer.Map.Count > 0;

				case Phase.Aligned:
					purpose = Purpose.Loop;
					remote = false;
					// a query costs an extraction and LighterGlue runs: only when an old view is near to find
					return loopClosure && Time.unscaledTime >= nextLoop && SharedSlam.Instance != null && SharedSlam.Instance.IsTracking &&
					       localizer.Map.Count > 0 &&
					       localizer.HasUntrustedKeyframeNear(XR.MainXRRig.Camera.transform.position, SharedSlam.Instance.ActiveNode, loopSearchDistance);

				default:
					return false;
			}
		}

		private async void Query(Purpose purpose, bool remote)
		{
			queryInFlight = true;
			try
			{
				Pending p = new()
				{
					purpose = purpose,
					epoch = FrameEpoch,
					generation = AlignmentGeneration,
				};

				p.query = await localizer.CaptureQueryAsync();
				if (!this) return;
				await Localize(p, remote);
			}
			finally
			{
				queryInFlight = false;
			}
		}

		/// <summary>
		/// A new keyframe is a view the map didn't have; near old views from another stretch
		/// of tracking it is also the best loop query there is, and its features are already
		/// extracted. Matched at once, without waiting for the loop interval or a capture.
		/// </summary>
		private async void OnKeyframeAdded(VisualKeyframe keyframe)
		{
			if (!loopFromKeyframes || !loopClosure || !IsRunning || CurrentPhase != Phase.Aligned || queryInFlight || pending.Count > 0) return;
			if (SharedSlam.Instance == null || !SharedSlam.Instance.IsTracking || keyframe.node == 0) return;
			if (!localizer.HasUntrustedKeyframeNear(keyframe.camera.position, keyframe.node, loopSearchDistance)) return;

			queryInFlight = true;
			try
			{
				Pending p = new()
				{
					purpose = Purpose.Loop,
					epoch = FrameEpoch,
					generation = AlignmentGeneration,
					query = new VisualQuery
					{
						features = keyframe.features,
						intrinsics = keyframe.intrinsics,
						camera = keyframe.camera,
						node = keyframe.node,
						cameraInNode = keyframe.cameraInNode,
						timestampNs = keyframe.timestampNs,
						frame = keyframe.frame,
					},
				};

				nextLoop = Time.unscaledTime + loopInterval;
				await Localize(p, false);
			}
			finally
			{
				queryInFlight = false;
			}
		}

		private async Awaitable Localize(Pending p, bool remote)
		{
			Purpose purpose = p.purpose;
			if (p.query == null)
			{
				// blurred (or no frame yet): try again shortly rather than after a whole interval
				nextQuery = Time.unscaledTime + blurredRetry;
				if (purpose == Purpose.Loop) nextLoop = Time.unscaledTime + blurredRetry;
				return;
			}

			if (remote)
			{
				int id = nextQueryId++;
				p.sentAt = Time.unscaledTime;
				pending[id] = p;
				queryEvent.Raise(VisualQueryCodec.Encode(p.query, id));
				return;
			}

			LocalizeOptions options = default;
			if (purpose == Purpose.Loop)
			{
				// the camera where the graph has it, against views from another stretch of tracking
				if (p.query.node == 0 || !Landmark.TryCamera(localizer.Anchoring, p.query.node, p.query.cameraInNode, out p.query.camera)) return;
				p.solves = graphSolves;
				options = new LocalizeOptions
				{
					near = p.query.camera, nearDistance = loopSearchDistance, untrustedFrom = p.query.node,
					centerPriorSigma = loopCenterPriorSigma,
					maxMatchFeatures = loopMatchFeatures,
					minMutualToMatch = loopMinMutual,
					maxViewAngle = loopMaxViewAngle,
				};
			}

			VisualLocalizationResult result = await localizer.LocalizeAsync(p.query, options);
			if (!this) return;
			LastResult = result;

			// a loop query finding nothing old in view is the normal case: noted, at most every 10 s
			if (purpose == Purpose.Loop && result.keyframesMatched == 0)
			{
				if (Time.unscaledTime >= nextQuietLoopLog)
				{
					nextQuietLoopLog = Time.unscaledTime + 10f;
					Outcome($"Loop: nothing to close against: {result.failure} ({localizer.Quality})");
				}

				return;
			}
			Consider(p, result.pose, result.anchorNode, result.ToString());
		}

		private void OnReplyReceived(ulong sender, byte[] payload)
		{
			if (payload.Length < Marshal.SizeOf<Reply>()) return;
			Reply reply = SyncBytes.Read<Reply>(payload, 0);
			if (!pending.Remove(reply.queryId, out Pending p)) return;

			GravityPnPResult pose = new()
			{
				success = reply.success != 0,
				yaw = reply.yaw,
				translation = reply.translation,
				inliers = reply.inliers,
				rangedInliers = reply.rangedInliers,
				correspondences = reply.correspondences,
				rmsPixels = reply.rmsPixels,
				normalizedRms = reply.normalizedRms,
				yawSigma = reply.yawSigma,
				translationSigma = reply.translationSigma,
				failure = reply.success != 0 ? null : $"authority found no pose ({reply.usableLandmarks} usable landmarks in its map)",
			};

			Consider(p, pose, 0, $"from authority: {pose}");
		}

		// ---- deciding ----------------------------------------------------------

		private void Consider(Pending p, GravityPnPResult pose, ulong anchor, string description)
		{
			if (!IsRunning) return;
			considering = p;

			// the frame the query was captured in has to still be the one aligned against
			if (p.epoch != FrameEpoch || p.generation != AlignmentGeneration)
			{
				Outcome($"{p.purpose}: discarded, the alignment changed while localizing ({description})");
				return;
			}

			bool yawOnly = false;
			string rejection = Rejection(pose);
			if (rejection == null && pose.translationSigma > maxTranslationSigma)
			{
				if (p.purpose == Purpose.Loop && yawOnlyLoops) yawOnly = true;
				else rejection = $"translation ±{pose.translationSigma * 100:F1}cm > {maxTranslationSigma * 100:F0}cm: the map needs ranged landmarks here (walk around)";
			}

			if (rejection == null && p.purpose == Purpose.Loop) rejection = Implausible(p, new Pose4(pose.translation, pose.yaw), anchor);

			if (p.purpose == Purpose.Loop && p.solves != graphSolves)
			{
				Outcome($"Loop: discarded, the graph was solved while localizing ({description})");
				return;
			}

			if (p.purpose == Purpose.Loop && pose.success)
				LoopSolved.Invoke(anchor, p.query.node, new Pose4(pose.translation, pose.yaw), pose, rejection);

			// a frame that fails says nothing against the candidate: it stays for the next good one
			if (rejection != null)
			{
				Outcome($"{p.purpose}: not accepted, {rejection} ({description})");
				return;
			}

			Pose4 correction = new(pose.translation, pose.yaw);
			bool loop = p.purpose == Purpose.Loop;
			bool confident = acceptConfidentSingleFrames && !yawOnly &&
			                 math.degrees(pose.yawSigma) <= (loop ? confidentLoopYawSigmaDegrees : confidentYawSigmaDegrees) &&
			                 pose.translationSigma <= (loop ? confidentLoopTranslationSigma : confidentTranslationSigma) &&
			                 pose.inliers >= (loop ? confidentLoopInliers : confidentInliers) &&
			                 pose.inliers >= confidentInlierShare * pose.correspondences;

			// two independent frames must agree before anything moves, unless one is certain enough on its own
			if (confident)
			{
				candidate = p;
				candidatePose = correction;
				candidateTranslationSigma = pose.translationSigma;
				candidateAnchor = anchor;
				candidateYawOnly = false;
				description = $"one frame, confident enough alone: {description}";
			}
			else if (!AgreesWithCandidate(p, correction, pose.translationSigma, anchor, yawOnly))
			{
				candidate = p;
				candidatePose = correction;
				candidateTranslationSigma = pose.translationSigma;
				candidateAnchor = anchor;
				candidateYawOnly = yawOnly;
				Outcome($"{p.purpose}: candidate {correction}{(yawOnly ? " (yaw only)" : "")}, waiting for a second frame to agree ({description})");
				// the confirming frame straight away, while the old views are still in sight
				nextQuery = Time.unscaledTime;
				if (p.purpose == Purpose.Loop) nextLoop = Time.unscaledTime;
				return;
			}

			// averaged about the camera, not the world origin, which may be tens of meters
			// away and turns a hundredth of a degree into centimeters there
			float3 camera = p.query.camera.position;
			float yawMean = candidatePose.yaw + Pose4.WrapAngle(correction.yaw - candidatePose.yaw) * 0.5f;
			// weighted by how well each pinned translation: a yaw-only frame's says nothing
			float candidateWeight = candidateYawOnly ? 0 : 1f / math.max(candidateTranslationSigma * candidateTranslationSigma, 1e-8f);
			float weight = yawOnly ? 0 : 1f / math.max(pose.translationSigma * pose.translationSigma, 1e-8f);
			if (candidateWeight + weight <= 0) candidateWeight = weight = 1;
			float3 centerMean = (candidatePose.Apply(camera) * candidateWeight + correction.Apply(camera) * weight) / (candidateWeight + weight);
			Pose4 agreed = new(centerMean - Pose4.RotateY(camera, yawMean), yawMean);
			candidate = null;

			if (p.purpose == Purpose.Loop)
			{
				SharedSlam slam = SharedSlam.Instance;
				// the two frames together pin translation as well as the better of them: a closure
				// is yaw-only when neither pinned it
				bool bothYawOnly = yawOnly && candidateYawOnly;
				float sigmaTranslation = bothYawOnly ? math.max(pose.translationSigma, 1f)
					: yawOnly ? candidateTranslationSigma
					: candidateYawOnly ? pose.translationSigma
					: math.min(pose.translationSigma, candidateTranslationSigma);
				yawOnly = bothYawOnly;
				if (slam != null && slam.ProposeVisualLoop(anchor, p.query.node, agreed, sigmaTranslation, math.max(pose.yawSigma, math.radians(loopEdgeMinYawSigmaDegrees))))
				{
					nextLoop = Time.unscaledTime + loopCooldown;
					Outcome($"{p.purpose}: closed {agreed}{(yawOnly ? " (yaw only)" : "")} ({description})");
				}
				return;
			}

			if (!IsAvailable)
			{
				Outcome($"{p.purpose}: not applied, {agreed} found while the rig or scanner is missing");
				return;
			}

			ApplyAlignment(agreed, p.query.trackingSpace);
			Outcome($"{p.purpose}: applied {agreed} ({description})");
		}

		private bool AgreesWithCandidate(Pending p, Pose4 correction, float translationSigma, ulong anchor, bool yawOnly)
		{
			if (candidate == null || candidate.purpose != p.purpose || candidate.generation != p.generation || candidate.epoch != p.epoch) return false;

			if (p.purpose == Purpose.Loop)
			{
				// one stretch of tracking against one old map: the query's node may have moved on
				// within trust, and the old nodes matched may differ, but must be one region of the old map
				float trust = localizer.TrustDrift;
				SharedSlam slam = SharedSlam.Instance;
				if (slam == null || slam.Drift(candidate.query.node, p.query.node) > trust || slam.Drift(candidateAnchor, anchor) > loopAnchorTolerance) return false;
			}

			float yawDifference = math.abs(Pose4.WrapAngle(correction.yaw - candidatePose.yaw));
			if (yawDifference > math.radians(agreeYawDegrees)) return false;
			if (yawOnly) return true;

			// where the two put the camera: their translations alone differ by the yaw
			// difference times the camera's distance from the world origin
			float3 camera = p.query.camera.position;
			float apart = math.distance(candidatePose.Apply(camera), correction.Apply(camera));
			float tolerance = math.max(agreeTranslation, 3 * math.sqrt(candidateTranslationSigma * candidateTranslationSigma + translationSigma * translationSigma));
			return apart <= tolerance;
		}


		/// <summary>
		/// Why a loop correction can't be the drift between the two stretches it joins, or
		/// null. The graph's expected drift between them (the shortest path in trust) bounds
		/// how far the camera can really be from where the graph has it, and how far turned.
		/// </summary>
		private string Implausible(Pending p, Pose4 correction, ulong anchor)
		{
			SharedSlam slam = SharedSlam.Instance;
			if (slam == null) return null;
			float drift = slam.Drift(anchor, p.query.node);
			if (float.IsInfinity(drift)) return null; // nothing joins them yet: no prior to hold it to

			PoseGraph graph = PoseGraph.Shared;
			float3 camera = p.query.camera.position;
			float moved = math.distance(correction.Apply(camera), camera);
			float turned = math.degrees(math.abs(Pose4.WrapAngle(correction.yaw)));
			// the yaw drift that goes with that much translation drift, at the graph's rates
			float yawDrift = drift / math.max(graph.DriftPerMeter, 1e-4f) * graph.YawDriftDegreesPerMeter;
			float maxMove = loopPlausibleMeters + loopPlausibleSigmas * drift;
			float maxTurn = loopPlausibleDegrees + loopPlausibleSigmas * yawDrift;
			if (moved > maxMove) return $"moves the camera {moved:F2} m, but the graph expects {drift:F2} m of drift (≤ {maxMove:F2} m)";
			if (turned > maxTurn) return $"turns the camera {turned:F1}°, but the graph expects {yawDrift:F1}° of drift (≤ {maxTurn:F1}°)";
			return null;
		}

		private string Rejection(GravityPnPResult pose)
		{
			if (!pose.success) return pose.failure ?? "no pose";
			if (pose.inliers < minInliers) return $"{pose.inliers} inliers < {minInliers}";
			if (pose.normalizedRms > maxNormalizedRms) return $"residuals {pose.normalizedRms:F2}σ > {maxNormalizedRms}σ";
			if (math.degrees(pose.yawSigma) > maxYawSigmaDegrees) return $"yaw ±{math.degrees(pose.yawSigma):F2}° > {maxYawSigmaDegrees}°";
			return null;
		}

		// the query an outcome is about, so the outcome lands on its frame too
		private Pending considering;

		private void Outcome(string message)
		{
			LastOutcome = message;
			if (considering?.query != null) localizer.Frames.Note(considering.query.frame, message);
			outcomes.Enqueue($"{Time.realtimeSinceStartup:F2} {message}");
			while (outcomes.Count > OutcomeLogLength) outcomes.Dequeue();
			Debug.Log($"{LogTag} {message}");
		}

		// ---- answering ---------------------------------------------------------

		private async void OnQueryReceived(ulong sender, byte[] payload)
		{
			if (!SyncBus.IsAuthority || serving || !localizer.IsAvailable) return;
			if (!VisualQueryCodec.TryDecode(payload, out VisualQuery query, out int queryId)) return;

			serving = true;
			VisualFrameRecord record = localizer.Frames.BeginRemote(query, sender);
			query.frame = record?.sequence ?? 0;
			try
			{
				LocalizeOptions options = query.nearMapFrame ? new LocalizeOptions { near = query.camera, nearDistance = loopSearchDistance } : default;
				VisualLocalizationResult result = await localizer.LocalizeAsync(query, options);
				if (!this) return;
				LastResult = result;
				considering = null;
				Outcome($"answered query {queryId} from client {sender}: {result}");

				GravityPnPResult pose = result.pose;
				Reply reply = new()
				{
					queryId = queryId,
					success = pose.success ? 1 : 0,
					yaw = pose.yaw,
					translation = pose.translation,
					inliers = pose.inliers,
					rangedInliers = pose.rangedInliers,
					correspondences = pose.correspondences,
					rmsPixels = pose.rmsPixels,
					normalizedRms = pose.normalizedRms,
					yawSigma = pose.yawSigma,
					translationSigma = pose.translationSigma,
					usableLandmarks = localizer.Quality.usable,
				};

				replyEvent.RaiseTo(sender, SyncBytes.Of(reply));
			}
			finally
			{
				serving = false;
			}
		}
	}
}
