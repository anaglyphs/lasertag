using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
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
		[Tooltip("A colocation or relocalization this certain is applied without waiting for a second frame (loop closures always wait)")]
		[SerializeField] private bool acceptConfidentSingleFrames = true;
		[SerializeField] private float confidentYawSigmaDegrees = 0.15f;
		[SerializeField] private float confidentTranslationSigma = 0.01f;
		[SerializeField] private int confidentInliers = 150;
		[Tooltip("Inliers as a share of the correspondences: repeated structure tends to give confident fits from a minority")]
		[SerializeField] private float confidentInlierShare = 0.6f;

		[Header("Agreement")]
		[SerializeField] private float agreeTranslation = 0.05f;
		[SerializeField] private float agreeYawDegrees = 1f;

		[Header("Loop closure")]
		[SerializeField] private bool loopClosure = true;
		[Tooltip("Seconds between loop closure queries, at most; none is made unless an old keyframe is near")]
		[SerializeField] private float loopInterval = 5f;
		[Tooltip("Old keyframes farther than this (m) from where the graph puts the camera are not tried")]
		[SerializeField] private float loopSearchDistance = 8f;
		[Tooltip("How well the graph is assumed to know where the camera is before a loop closure, meters: the position prior far landmarks' yaw leans on")]
		[SerializeField] private float loopCenterPriorSigma = 1f;
		[Tooltip("A closure that pins yaw but not translation (far landmarks only) still becomes an edge, with translation left loose")]
		[SerializeField] private bool yawOnlyLoops = true;
		[SerializeField] private float loopCooldown = 3f;
		[Tooltip("Features per side a loop query's LighterGlue runs attend over: cost grows with the square, and a loop closure needs far fewer than a cold relocalization")]
		[SerializeField] private int loopMatchFeatures = 512;
		[Tooltip("An old keyframe sharing fewer descriptor matches than this with the view isn't worth a LighterGlue run")]
		[SerializeField] private int loopMinMutual = 30;
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
		}

		private readonly Dictionary<int, Pending> pending = new();
		private int nextQueryId = 1;
		private float nextQuery;
		private float nextLoop;
		private bool queryInFlight;
		private bool serving;

		// the first of two results that must agree
		private Pending candidate;
		private Pose4 candidatePose;
		private float candidateTranslationSigma;
		private ulong candidateAnchor;

		private VisualLocalizer localizer;

		public static VisualColocationConstraintProvider Instance { get; private set; }
		public VisualLocalizationResult LastResult { get; private set; }
		public string LastOutcome { get; private set; }

		// what every recent attempt came to, for AlignmentDiagnosis
		private const int OutcomeLogLength = 400;
		private readonly Queue<string> outcomes = new();

		/// <summary>Recent outcomes, oldest first, each stamped with the realtime it happened at.</summary>
		public IEnumerable<string> RecentOutcomes => outcomes;

		public override string LastAttemptDescription => LastOutcome ?? "none";

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
				if (p.query == null || !this) return;

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
					options = new LocalizeOptions
					{
						near = p.query.camera, nearDistance = loopSearchDistance, untrustedFrom = p.query.node,
						centerPriorSigma = loopCenterPriorSigma,
						maxMatchFeatures = loopMatchFeatures,
						minMutualToMatch = loopMinMutual,
					};
				}

				VisualLocalizationResult result = await localizer.LocalizeAsync(p.query, options);
				if (!this) return;
				LastResult = result;

				// a loop query finding nothing old in view is the normal case, not news
				if (purpose == Purpose.Loop && result.keyframesMatched == 0) return;
				Consider(p, result.pose, result.anchorNode, result.ToString());
			}
			finally
			{
				queryInFlight = false;
			}
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

			// a frame that fails says nothing against the candidate: it stays for the next good one
			if (rejection != null)
			{
				Outcome($"{p.purpose}: not accepted, {rejection} ({description})");
				return;
			}

			Pose4 correction = new(pose.translation, pose.yaw);
			bool confident = acceptConfidentSingleFrames && p.purpose != Purpose.Loop && !yawOnly &&
			                 math.degrees(pose.yawSigma) <= confidentYawSigmaDegrees && pose.translationSigma <= confidentTranslationSigma &&
			                 pose.inliers >= confidentInliers && pose.inliers >= confidentInlierShare * pose.correspondences;

			// two independent frames must agree before anything moves, unless one is certain enough on its own
			if (confident)
			{
				candidate = p;
				candidatePose = correction;
				candidateTranslationSigma = pose.translationSigma;
				candidateAnchor = anchor;
				description = $"one frame, confident enough alone: {description}";
			}
			else if (!AgreesWithCandidate(p, correction, pose.translationSigma, anchor, yawOnly))
			{
				candidate = p;
				candidatePose = correction;
				candidateTranslationSigma = pose.translationSigma;
				candidateAnchor = anchor;
				Outcome($"{p.purpose}: candidate {correction}{(yawOnly ? " (yaw only)" : "")}, waiting for a second frame to agree ({description})");
				return;
			}

			// averaged about the camera, not the world origin, which may be tens of meters
			// away and turns a hundredth of a degree into centimeters there
			float3 camera = p.query.camera.position;
			float yawMean = candidatePose.yaw + Pose4.WrapAngle(correction.yaw - candidatePose.yaw) * 0.5f;
			float3 centerMean = (candidatePose.Apply(camera) + correction.Apply(camera)) * 0.5f;
			Pose4 agreed = new(centerMean - Pose4.RotateY(camera, yawMean), yawMean);
			candidate = null;

			if (p.purpose == Purpose.Loop)
			{
				SharedSlam slam = SharedSlam.Instance;
				float sigmaTranslation = yawOnly ? math.max(pose.translationSigma, 1f) : pose.translationSigma;
				if (slam != null && slam.ProposeVisualLoop(anchor, p.query.node, agreed, sigmaTranslation, pose.yawSigma))
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
