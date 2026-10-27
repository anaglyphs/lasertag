using System.Collections.Generic;
using System.Runtime.InteropServices;
using Anaglyph.Debugging;
using Anaglyph.Debugging.Visuals;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Colocation, relocalization and drift correction from one camera landmark map per
	/// headset (<see cref="VisualLocalizer"/>, its keyframes hung off a
	/// <see cref="TrackAnchoring"/>). Needs no scan of its own to colocate: a joiner's scan
	/// stays shut until it is aligned, and the authority's snapshot goes straight into it.
	///
	/// A joiner sends query frames to the authority, which localizes them against its map
	/// and replies with the transform from the joiner's world to canon. A headset
	/// relocalizing after a recenter queries its own map. An aligned headset keeps
	/// measuring its drift where it can: revisits of its own map (the same place after a
	/// walk) and, for a joiner, cross-links of its keyframes against the authority's map.
	/// Each becomes a <see cref="ConstraintSites"/> site, every site a colocation constraint,
	/// and the colocator's rigid fit over them aligns the rig; the maps themselves never move.
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
		[Tooltip("Seconds before asking again after the authority answered that it was busy localizing another headset's query")]
		[SerializeField] private float busyRetry = 0.2f;

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
		[SerializeField] private float confidentLoopYawSigmaDegrees = 0.25f;
		[Tooltip("Inliers as a share of the correspondences a loop closure from one frame needs")]
		[SerializeField] private float confidentLoopInlierShare = 0.75f;
		[Tooltip("Inliers as a share of the correspondences: repeated structure tends to give confident fits from a minority")]
		[SerializeField] private float confidentInlierShare = 0.6f;

		[Header("Agreement")]
		[SerializeField] private float agreeTranslation = 0.05f;
		[SerializeField] private float agreeYawDegrees = 1f;
		[Tooltip("Loop closures measured this far apart along the path (m) can still agree; between them, tracking drifts on and the correction with it")]
		[SerializeField] private float loopAgreePath = 30f;
		[Tooltip("How much farther apart (m per m walked between them) two loop measurements may be and still agree")]
		[SerializeField] private float loopAgreeTranslationPerMeter = 0.02f;
		[Tooltip("How much farther turned (° per m walked between them) two loop measurements may be and still agree")]
		[SerializeField] private float loopAgreeYawPerMeter = 0.3f;

		[Header("Revisits (drift correction)")]
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
		[SerializeField] private bool yawOnlyLoops = false;
		[SerializeField] private float loopCooldown = 3f;
		[Tooltip("A revisit corrects the drift walked since the last measurement: one moving the camera by more than this plus the per-meter allowance found the wrong place, such as the far end of a symmetric hallway")]
		[SerializeField] private float loopPlausibleMeters = 0.5f;
		[SerializeField] private float loopPlausibleFraction = 0.03f;
		[SerializeField] private float loopPlausibleDegrees = 5f;
		[SerializeField] private float loopPlausibleDegreesPerMeter = 0.5f;
		[Tooltip("Meters a revisit's camera prior is taken to be off before any drift is counted: with the drift expected over the path since the last site (the track's rate), how far around each projected landmark a guided loop query looks")]
		[SerializeField] private float loopSearchFloor = 0.1f;
		[Tooltip("Least yaw sigma a site carries, degrees: the solver's own runs several times too small against ground truth (0.6–0.9° errors at ±0.1–0.3°)")]
		[SerializeField] private float loopEdgeMinYawSigmaDegrees = 0.5f;
		[Tooltip("Least translation sigma a site carries, meters: likewise several times too small against ground truth")]
		[SerializeField] private float siteMinTranslationSigma = 0.03f;

		[Header("Sites")]
		[Tooltip("A measurement this close to a site updates it, meters: a few sites per room, each refined by every revisit")]
		[SerializeField] private float siteRadius = 8f;
		[Tooltip("Head travel over which a site's earlier measurement loses most of its weight against a revisit's, meters: drift grows with distance walked")]
		[SerializeField] private float sitePathScale = 15f;
		[SerializeField] private Color debugRevisitColor = new(1f, 0.8f, 0.2f);
		[SerializeField] private Color debugCrossLinkColor = new(0.2f, 0.9f, 1f);
		[Tooltip("The site measured most recently")]
		[SerializeField] private Color debugLatestColor = new(0.2f, 1f, 0.3f);
		[Tooltip("Size of the markers drawn at the features each site's latest measurement stood on, meters")]
		[SerializeField] private float debugFeatureSize = 0.03f;
		[Tooltip("Features per side a loop query's LighterGlue runs attend over, when the localizer's guided matching is off: cost grows with the square, and a loop closure needs far fewer than a cold relocalization")]
		[SerializeField] private int loopMatchFeatures = 512;
		[Tooltip("An old keyframe sharing fewer descriptor matches than this with the view isn't worth a LighterGlue run (guided matching off)")]
		[SerializeField] private int loopMinMutual = 15;
		[Tooltip("Old keyframes facing more than this many degrees away from the view can't share it, and aren't tried")]
		[SerializeField] private float loopMaxViewAngle = 90f;
		[Tooltip("Every new keyframe made near old views is matched as a loop query at once, with the features mapping already extracted")]
		[SerializeField] private bool loopFromKeyframes = true;
		[Tooltip("With keyframe loop queries on, a loop query of its own (a capture, an extraction and a LighterGlue run) is made only after this many seconds without a keyframe: standing still near old views, where no new keyframe comes")]
		[SerializeField] private float loopCaptureAfterQuiet = 3f;
		[Tooltip("Recent keyframes of this stretch of tracking solved together with a loop query's frame: the baseline between them pins translation as one view can't. 1 for the frame alone")]
		[SerializeField] private int loopFrames = 3;
		[Tooltip("Seconds back a keyframe may be to join a measurement: tracking drifts little in that long, so the frames share one correction")]
		[SerializeField] private float loopFrameWindow = 4f;

		[Header("Cross-headset links")]
		[Tooltip("Once aligned, a joiner keeps sending keyframes to the authority; a confident answer becomes a site, so the headsets keep agreeing on the shared frame instead of each drifting against its own map")]
		[SerializeField] private bool crossLinks = true;
		[Tooltip("Seconds between cross-link queries, at most: each is a LighterGlue run on the authority and ~50 KB on the network")]
		[SerializeField] private float crossLinkInterval = 2f;
		[SerializeField] private float crossLinkCooldown = 6f;
		[Tooltip("Keyframes a cross-link sends, the newest first, solved together on the authority: the baseline between them pins translation as one view can't. Two fit the transport at 448 features each")]
		[SerializeField] private int crossLinkFrames = 2;
		[SerializeField] private int crossLinkFeaturesPerFrame = 448;
		[Tooltip("A joiner near a site a cross-link made leaves its own map alone there: the authority's map is the frame to agree on, and a revisit of its own map pulls a few centimeters elsewhere")]
		[SerializeField] private bool crossLinksPrevail = true;
		[Tooltip("Two revisit results may lean on different old segments, as long as those are this close (m of expected drift): the correction agreeing is the real test")]
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
			/// <summary>Nonzero when the authority didn't look at the query: <see cref="BusyServing"/> or <see cref="BusyUnavailable"/>.</summary>
			public int busy;
		}

		// the authority was localizing another headset's query (ask again shortly), or has no
		// camera to localize with (ask again later)
		private const int BusyServing = 1, BusyUnavailable = 2;

		// Loop: a revisit of this headset's own map. CrossLink: a joiner's keyframe localized in
		// the authority's map once both are aligned. Both measure drift and become sites
		private enum Purpose { Colocate, Relocalize, Loop, CrossLink }

		// a drift measurement, against this headset's map or the authority's
		private static bool ClosesLoop(Purpose purpose) => purpose is Purpose.Loop or Purpose.CrossLink;

		private class Pending
		{
			public VisualQuery query;
			/// <summary>The query and the recent frames solved with it, the query first; null for the query alone.</summary>
			public List<VisualQuery> frames;
			public Purpose purpose;
			public int epoch;
			public int generation;
			public float sentAt;
			// the map points the solve stood on, for the site's debug view
			public Vector3[] features;
		}

		private readonly Dictionary<int, Pending> pending = new();
		// cross-link queries in flight: kept apart, so they don't hold up colocation or local loops
		private readonly Dictionary<int, Pending> crossLinkQueries = new();
		private float nextCrossLink;
		private int nextQueryId = 1;
		private float nextQuery;
		private float nextLoop;
		private float nextQuietLoopLog;
		private float nextBusyLog;
		private bool queryInFlight;
		private float lastKeyframeAt;
		private bool serving;

		// the first of two results that must agree
		private Pending candidate;
		private Pose4 candidatePose;
		private float candidateTranslationSigma;
		private bool candidateYawOnly;
		private ulong candidateAnchor;

		private VisualLocalizer localizer;
		private readonly TrackAnchoring track = new();
		private readonly ConstraintSites sites = new();
		private int lastSiteId;
		private int sitesGeneration = -1, sitesEpoch = -1;

		public static VisualColocationConstraintProvider Instance { get; private set; }

		public TrackAnchoring Track => track;
		public ConstraintSites Sites => sites;

		/// <summary>
		/// Every drift measurement that solved: the query, the site correction it implies (the
		/// whole correction, not the step from the one in force), the solve, the map segment it
		/// leaned on (0 for a cross-link: the authority's), and why it was turned down (null when
		/// it counted). For tests that know the truth.
		/// </summary>
		public event System.Action<VisualQuery, Pose4, GravityPnPResult, ulong, string> MeasurementSolved = delegate { };

		/// <summary>Raised when a measurement becomes or updates a site.</summary>
		public event System.Action<ConstraintSites.Site> SiteMeasured = delegate { };
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

		/// <summary>
		/// Relocalizes against the map as it stands, a map just loaded for the space: the rig
		/// goes where the map says, as after a recenter. False when there is nothing to do it with.
		/// </summary>
		public bool RelocalizeAgainstMap() => localizer.Map.Count > 0 && BeginRelocalize("saved map");

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
		}

		private void Start()
		{
			// keyframes hang off this headset's track; the localizer's map exists from its Awake
			localizer.Anchoring = track;
			// the map's frame is gone when the scan is
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared += ClearMap;
			Maps.SpaceManager.FrameChanged += ClearMap;
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
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared -= ClearMap;
			Maps.SpaceManager.FrameChanged -= ClearMap;
			PhaseChanged -= OnPhaseChanged;
			if (Instance == this) Instance = null;
		}

		private void ClearMap()
		{
			if (localizer.Map.Count > 0)
				Debug.Log($"{LogTag} map cleared ({localizer.Quality}): its frame is gone");
			localizer.Map.Clear();
			sites.Clear();
			track.Clear();
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
			UpdateCorrection();

			// replies that never came
			if (pending.Count > 0)
			{
				List<int> expired = null;
				foreach ((int id, Pending p) in pending)
					if (Time.unscaledTime - p.sentAt > replyTimeout) (expired ??= new()).Add(id);
				if (expired != null) foreach (int id in expired) pending.Remove(id);
			}

			if (crossLinkQueries.Count > 0)
			{
				List<int> expired = null;
				foreach ((int id, Pending p) in crossLinkQueries)
					if (Time.unscaledTime - p.sentAt > replyTimeout) (expired ??= new()).Add(id);
				if (expired != null) foreach (int id in expired) crossLinkQueries.Remove(id);
			}

			if (!queriesEnabled || queryInFlight || pending.Count > 0 || Time.unscaledTime < nextQuery || !localizer.IsAvailable) return;

			// a headset waiting on an alignment runs the networks over fewer frames (while
			// stopped, the pace is whoever else uses the localizer's to set)
			if (IsRunning) localizer.Urgent = CurrentPhase is Phase.Searching or Phase.Registering or Phase.Relocalizing;

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
					return loopClosure && Time.unscaledTime >= nextLoop && track.IsTracking &&
					       (!loopFromKeyframes || Time.unscaledTime - lastKeyframeAt >= loopCaptureAfterQuiet) &&
					       localizer.Map.Count > 0 &&
					       !CrossLinkCovers(MainXRRig.Camera.transform.position) &&
					       localizer.HasUntrustedKeyframeNear(MainXRRig.Camera.transform.position, track.Current, loopSearchDistance);

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
			lastKeyframeAt = Time.unscaledTime;
			SendCrossLink(keyframe);
			if (!loopFromKeyframes || !loopClosure || !IsRunning || CurrentPhase != Phase.Aligned || queryInFlight || pending.Count > 0) return;
			// each is up to keyframesPerQuery LighterGlue runs on the GPU: no more often than the loop
			// interval, however fast keyframes come (a confirming frame is let through at once)
			if (Time.unscaledTime < nextLoop) return;
			if (!track.IsTracking || keyframe.node == 0) return;
			if (CrossLinkCovers(keyframe.camera.position)) return;
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

		/// <summary>
		/// A joiner's new keyframe, sent to the authority to localize in its map. The camera
		/// goes where this headset's alignment placed it; the answer says where the authority's
		/// map has it, which is the drift to correct.
		/// </summary>
		private void SendCrossLink(VisualKeyframe keyframe)
		{
			if (!crossLinks || !IsRunning || CurrentPhase != Phase.Aligned || !SyncBus.Active || SyncBus.IsAuthority) return;
			if (crossLinkQueries.Count > 0 || Time.unscaledTime < nextCrossLink) return;
			if (!track.IsTracking || keyframe.node == 0) return;
			if (!Landmark.TryCamera(localizer.Anchoring, keyframe.node, keyframe.cameraInNode, out Pose camera)) return;

			nextCrossLink = Time.unscaledTime + crossLinkInterval;
			VisualQuery query = new()
			{
				features = keyframe.features,
				intrinsics = keyframe.intrinsics,
				camera = camera,
				node = keyframe.node,
				cameraInNode = keyframe.cameraInNode,
				timestampNs = keyframe.timestampNs,
				frame = keyframe.frame,
				nearMapFrame = true,
			};

			// with the keyframes just before it, solved together on the authority
			List<VisualQuery> frames = FramesWith(query, crossLinkFrames);
			int id = nextQueryId++;
			crossLinkQueries[id] = new Pending
			{
				purpose = Purpose.CrossLink,
				epoch = FrameEpoch,
				generation = AlignmentGeneration,
				query = query,
				frames = frames,
				sentAt = Time.unscaledTime,
			};
			queryEvent.Raise(VisualQueryCodec.Encode(frames, id, frames.Count > 1 ? crossLinkFeaturesPerFrame : VisualQueryCodec.DefaultMaxFeatures));
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
				// the camera where the alignment placed it, against views from another stretch of tracking
				if (p.query.node == 0 || !Landmark.TryCamera(localizer.Anchoring, p.query.node, p.query.cameraInNode, out p.query.camera)) return;
				// matched by projection through that camera, looking as far around each landmark
				// as the drift since the last site is expected to have moved it
				options = new LocalizeOptions
				{
					near = p.query.camera, nearDistance = loopSearchDistance, untrustedFrom = p.query.node,
					centerPriorSigma = loopCenterPriorSigma,
					maxMatchFeatures = loopMatchFeatures,
					minMutualToMatch = loopMinMutual,
					maxViewAngle = loopMaxViewAngle,
					guided = true,
					guidedTranslation = ExpectedLoopDrift(p),
				};
			}

			// a loop query with the keyframes just before it: one solve over their baseline
			if (purpose == Purpose.Loop) p.frames = FramesWith(p.query, loopFrames);
			VisualLocalizationResult result = p.frames != null
				? await localizer.LocalizeAsync(p.frames, options)
				: await localizer.LocalizeAsync(p.query, options);
			if (!this) return;
			LastResult = result;
			p.features = result.inlierPoints;

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
			if (reply.busy != 0)
			{
				OnBusyReply(reply);
				return;
			}

			if (crossLinkQueries.Remove(reply.queryId, out Pending link))
			{
				OnCrossLinkReply(link, reply);
				return;
			}

			if (!pending.Remove(reply.queryId, out Pending p)) return;
			GravityPnPResult pose = PoseOf(reply);
			Consider(p, pose, 0, $"from authority: {pose}");
		}

		// the authority didn't look at the query: asked again soon, not after the reply timeout
		private void OnBusyReply(Reply reply)
		{
			bool crossLink = crossLinkQueries.Remove(reply.queryId, out _);
			if (!crossLink && !pending.Remove(reply.queryId, out _)) return;

			// serving another headset passes in a moment; having no camera doesn't
			float retry = reply.busy == BusyServing ? busyRetry : queryInterval;
			if (crossLink) nextCrossLink = Time.unscaledTime + retry;
			else nextQuery = Time.unscaledTime + retry;

			if (Time.unscaledTime < nextBusyLog) return;
			nextBusyLog = Time.unscaledTime + 5f;
			considering = null;
			string why = reply.busy == BusyServing ? "busy localizing another headset's query" : "unable to localize (no camera)";
			Outcome($"{(crossLink ? "CrossLink" : "Colocate")}: the authority was {why}, asking again in {retry:F1} s");
		}

		private static GravityPnPResult PoseOf(Reply reply) => new()
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

		private void OnCrossLinkReply(Pending p, Reply reply)
		{
			GravityPnPResult pose = PoseOf(reply);
			if (!pose.success) return; // nothing of the authority's in view: the usual case
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
				if (ClosesLoop(p.purpose) && yawOnlyLoops) yawOnly = true;
				else rejection = $"translation ±{pose.translationSigma * 100:F1}cm > {maxTranslationSigma * 100:F0}cm: the map needs ranged landmarks here (walk around)";
			}

			if (rejection == null && ClosesLoop(p.purpose)) rejection = Implausible(p, new Pose4(pose.translation, pose.yaw));

			// a drift measurement maps the camera as its segment's correction placed it onto the
			// map: composed with that correction, it is the whole correction the site needs
			Pose4 correction = new(pose.translation, pose.yaw);
			bool loop = ClosesLoop(p.purpose);
			if (loop) correction = correction * track.CorrectionOf(p.query.node);

			if (loop && pose.success)
				MeasurementSolved.Invoke(p.query, correction, pose, p.purpose == Purpose.CrossLink ? 0 : anchor, rejection);

			// a frame that fails says nothing against the candidate: it stays for the next good one
			if (rejection != null)
			{
				Outcome($"{p.purpose}: not accepted, {rejection} ({description})");
				return;
			}

			bool confident = acceptConfidentSingleFrames && !yawOnly &&
			                 math.degrees(pose.yawSigma) <= (loop ? confidentLoopYawSigmaDegrees : confidentYawSigmaDegrees) &&
			                 pose.translationSigma <= (loop ? confidentLoopTranslationSigma : confidentTranslationSigma) &&
			                 pose.inliers >= (loop ? confidentLoopInliers : confidentInliers) &&
			                 pose.inliers >= (loop ? confidentLoopInlierShare : confidentInlierShare) * pose.correspondences;

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
				if (p.purpose == Purpose.CrossLink) nextCrossLink = Time.unscaledTime;
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
			Vector3[] features = candidate == p || candidate?.features == null ? p.features
				: p.features == null ? candidate.features
				: System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(candidate.features, p.features));
			candidate = null;

			if (ClosesLoop(p.purpose))
			{
				// a joiner's own map has nothing to add where the authority's reaches
				if (p.purpose == Purpose.Loop && CrossLinkCovers(camera))
				{
					Outcome($"{p.purpose}: not a site, a cross-link site covers here ({description})");
					return;
				}

				// the two frames together pin translation as well as the better of them: a site
				// is yaw-only when neither pinned it
				bool bothYawOnly = yawOnly && candidateYawOnly;
				float sigmaTranslation = bothYawOnly ? math.max(pose.translationSigma, 1f)
					: yawOnly ? candidateTranslationSigma
					: candidateYawOnly ? pose.translationSigma
					: math.min(pose.translationSigma, candidateTranslationSigma);
				yawOnly = bothYawOnly;

				ConstraintSites.Site site = sites.Add(camera, agreed, math.max(sigmaTranslation, siteMinTranslationSigma),
					math.max(pose.yawSigma, math.radians(loopEdgeMinYawSigmaDegrees)), track.Path, Time.realtimeSinceStartup, p.purpose == Purpose.CrossLink, features);
				lastSiteId = site.id;
				if (p.purpose == Purpose.Loop) nextLoop = Time.unscaledTime + loopCooldown;
				else nextCrossLink = Time.unscaledTime + crossLinkCooldown;
				Outcome($"{p.purpose}: site {site.id} ({site.measurements} measurements, {site.features?.Length ?? 0} features, {sites.Count} sites) now {site.correction}{(yawOnly ? " (yaw only)" : "")} ({description})");
				SiteMeasured.Invoke(site);
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

			// a moving player's two loop measurements come seconds and meters apart, and tracking
			// drifts on between them: they may differ by as much as it could have drifted
			float walked = 0;
			if (ClosesLoop(p.purpose))
			{
				// the old segments matched may differ (a revisit's), but must be one region of the old map
				float between = track.Drift(candidate.query.node, p.query.node);
				walked = between / math.max(track.DriftPerMeter, 1e-4f);
				if (!(walked <= loopAgreePath)) return false;
				if (p.purpose == Purpose.Loop && track.Drift(candidateAnchor, anchor) > loopAnchorTolerance) return false;
			}

			float yawDifference = math.abs(Pose4.WrapAngle(correction.yaw - candidatePose.yaw));
			if (yawDifference > math.radians(agreeYawDegrees + walked * loopAgreeYawPerMeter)) return false;
			if (yawOnly) return true;

			// where the two put the camera: their translations alone differ by the yaw
			// difference times the camera's distance from the world origin
			float3 camera = p.query.camera.position;
			float apart = math.distance(candidatePose.Apply(camera), correction.Apply(camera));
			float tolerance = math.max(agreeTranslation, 3 * math.sqrt(candidateTranslationSigma * candidateTranslationSigma + translationSigma * translationSigma));
			return apart <= tolerance + walked * loopAgreeTranslationPerMeter;
		}


		/// <summary>
		/// Why a drift measurement can't be the drift walked since the correction it is relative
		/// to was measured, or null: tracking drifts a little per meter, so a step much larger
		/// found the wrong place.
		/// </summary>
		private string Implausible(Pending p, Pose4 step)
		{
			float maxMove = MaxLoopMove(p, out float walked);
			float3 camera = p.query.camera.position;
			float moved = math.distance(step.Apply(camera), camera);
			float turned = math.degrees(math.abs(Pose4.WrapAngle(step.yaw)));
			float maxTurn = loopPlausibleDegrees + loopPlausibleDegreesPerMeter * walked;
			if (moved > maxMove) return $"moves the camera {moved:F2} m after {walked:F0} m walked (≤ {maxMove:F2} m)";
			if (turned > maxTurn) return $"turns the camera {turned:F1}° after {walked:F0} m walked (≤ {maxTurn:F1}°)";
			return null;
		}

		/// <summary>
		/// How far a revisit may find the camera from where its segment's correction placed it,
		/// meters: the drift of the path <paramref name="walked"/> since the latest site, over a floor.
		/// </summary>
		private float MaxLoopMove(Pending p, out float walked)
		{
			walked = WalkedSinceSite(p);
			return loopPlausibleMeters + loopPlausibleFraction * walked;
		}

		/// <summary>How far off a revisit's camera prior is expected to be, meters: the track's drift rate over the path since the latest site, over a floor. Several times less than <see cref="MaxLoopMove"/>, which is what the gate allows.</summary>
		private float ExpectedLoopDrift(Pending p) => loopSearchFloor + WalkedSinceSite(p) * track.DriftPerMeter;

		private float WalkedSinceSite(Pending p) => sites.PathSinceLatest(track.PathOf(p.query.node), track.PathOf(p.query.node));

		// a joiner near a site a cross-link made: the authority's map is the frame to agree on
		private bool CrossLinkCovers(float3 position) =>
			crossLinksPrevail && SyncBus.Active && !SyncBus.IsAuthority && sites.CrossLinkWithin(position, siteRadius);

		/// <summary>
		/// The latest frame with the newest keyframes of its stretch of tracking taken within
		/// the window before it, up to <paramref name="count"/> frames in all, newest first:
		/// each camera placed as the latest frame's correction places it rather than its own
		/// segment's, so all share one query world and one solve.
		/// </summary>
		private List<VisualQuery> FramesWith(VisualQuery latest, int count)
		{
			List<VisualQuery> frames = new() { latest };
			if (count <= 1 || latest.node == 0) return frames;

			Pose4 correction = track.CorrectionOf(latest.node);
			long oldest = latest.timestampNs - (long)(loopFrameWindow * 1e9);
			List<VisualKeyframe> recent = new();
			foreach (VisualKeyframe k in localizer.Map.Keyframes)
			{
				if (k.node == 0 || k.timestampNs >= latest.timestampNs || k.timestampNs < oldest) continue;
				if (track.Drift(k.node, latest.node) > localizer.TrustDrift) continue;
				recent.Add(k);
			}

			recent.Sort((a, b) => b.timestampNs.CompareTo(a.timestampNs));
			foreach (VisualKeyframe k in recent)
			{
				if (frames.Count >= count) break;
				Pose camera = correction.Apply(track.CorrectionOf(k.node).Inverse.Apply(k.cameraInNode));
				frames.Add(new VisualQuery
				{
					features = k.features,
					intrinsics = k.intrinsics,
					camera = camera,
					node = k.node,
					cameraInNode = k.cameraInNode,
					timestampNs = k.timestampNs,
					frame = k.frame,
					nearMapFrame = latest.nearMapFrame,
				});
			}

			return frames;
		}

		// ---- correcting --------------------------------------------------------

		/// <summary>
		/// The rig's correction from the sites around the head, and the scanner told the goal
		/// so scans land where the rig is going while the colocator eases it there. A new
		/// alignment or a recenter leaves every site's correction describing a frame that is gone.
		/// </summary>
		private void UpdateCorrection()
		{
			if (sitesGeneration != AlignmentGeneration || sitesEpoch != FrameEpoch)
			{
				sites.Clear();
				sitesGeneration = AlignmentGeneration;
				sitesEpoch = FrameEpoch;
			}

			sites.SiteRadius = siteRadius;
			sites.PathScale = sitePathScale;
			track.Update(this);

			EnvScanner scanner = EnvScanner.Instance;
			if (CurrentPhase != Phase.Aligned || MainXRRig.Instance == null)
			{
				if (scanner != null) scanner.SensorCorrection = Matrix4x4.identity;
				return;
			}

			// where the colocator's fit over the sites takes the rig, the same fit it runs: the
			// correction keyframes are placed with, and the scan's goal while the rig eases there
			if (sites.Count > 0)
			{
				siteConstraints.Clear();
				AddSiteConstraints(siteConstraints);
				if (ColocationFit.TryEvaluate(siteConstraints, out Pose delta, out _, out _))
				{
					Pose canon = CanonSpacePose;
					Matrix4x4 goal = Matrix4x4.TRS(delta.position, delta.rotation, Vector3.one) * MainXRRig.TrackingSpace.localToWorldMatrix;
					SiteCorrection = Pose4.FromMatrix(goal * Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse);
				}
			}

			if (scanner != null)
			{
				Pose target = TargetSpacePose;
				scanner.SensorCorrection = Matrix4x4.TRS(target.position, target.rotation, Vector3.one) * MainXRRig.TrackingSpace.worldToLocalMatrix;
			}
		}

		// ---- constraints -------------------------------------------------------

		private readonly List<ColocationConstraint> siteConstraints = new();

		/// <summary>One constraint per site once there are any; the found alignment's own until then.</summary>
		public override void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (CurrentPhase != Phase.Aligned || !IsAvailable || sites.Count == 0)
			{
				base.GetColocationConstraints(results);
				return;
			}

			AddSiteConstraints(results);
		}

		/// <summary>
		/// Each site as an anchor would be: where the uncorrected alignment places it, seen
		/// through the tracking space as it stands (observed), and where the map has it (canon),
		/// turned by its correction's yaw so that one site alone still fixes rotation.
		/// </summary>
		private void AddSiteConstraints(List<ColocationConstraint> results)
		{
			Pose canon = CanonSpacePose;
			Matrix4x4 worldFromPlaced = MainXRRig.TrackingSpace.localToWorldMatrix * Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse;
			Quaternion placedRotation = worldFromPlaced.rotation;
			foreach (ConstraintSites.Site site in sites.Sites)
			{
				float3 placed = site.correction.ApplyInverse(site.position);
				// weighted by how well the site pinned each: the fit leans on the surer sites,
				// and takes their measured yaws rather than deriving yaw from positions alone
				results.Add(new ColocationConstraint(new Pose(worldFromPlaced.MultiplyPoint3x4(placed), placedRotation),
					new Pose(site.position, site.correction.Rotation), true,
					1f / math.max(site.sigmaTranslation * site.sigmaTranslation, 1e-6f),
					1f / math.max(site.sigmaYaw * site.sigmaYaw, 1e-8f)));
			}
		}

		// every site as an axis where the map has it (green for the one measured last), a line
		// from there to where the rig's current alignment puts it: how far the fit leaves each
		// one, and the features its latest measurement stood on
		private void LateUpdate()
		{
			if (!AnaglyphDebugging.DebugMode || MainXRRig.Instance == null || sites.Count == 0 || CurrentPhase != Phase.Aligned) return;
			Pose canon = CanonSpacePose;
			Matrix4x4 worldFromPlaced = MainXRRig.TrackingSpace.localToWorldMatrix * Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse;
			foreach (ConstraintSites.Site site in sites.Sites)
			{
				Color color = site.id == lastSiteId ? debugLatestColor : site.crossLink ? debugCrossLinkColor : debugRevisitColor;
				DebugAxisVisual.DrawDebugAxis(site.position, site.correction.Rotation, color, 0.3f);
				XRGizmos.DrawLine(site.position, worldFromPlaced.MultiplyPoint3x4(site.correction.ApplyInverse(site.position)), color);
				// the features its latest measurement stood on, where the map has them
				if (site.features != null) XRGizmos.DrawPointSet(site.features, color, debugFeatureSize);
			}
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
			// several a second while moving: no stack trace, which costs on device
			Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "{0} {1}", LogTag, message);
		}

		// ---- answering ---------------------------------------------------------

		private async void OnQueryReceived(ulong sender, byte[] payload)
		{
			if (!SyncBus.IsAuthority) return;
			if (serving || !localizer.IsAvailable)
			{
				// said at once, so the asker tries again shortly instead of waiting out its reply timeout
				if (VisualQueryCodec.TryReadId(payload, out int id))
					replyEvent.RaiseTo(sender, SyncBytes.Of(new Reply { queryId = id, busy = serving ? BusyServing : BusyUnavailable }));
				return;
			}

			if (!VisualQueryCodec.TryDecode(payload, out List<VisualQuery> frames, out int queryId) || frames.Count == 0) return;
			VisualQuery query = frames[0];

			serving = true;
			VisualFrameRecord record = localizer.Frames.BeginRemote(query, sender);
			query.frame = record?.sequence ?? 0;
			try
			{
				// a cross-link is a drift measurement with a camera prior: confined to the views
				// near it and capped like a local loop query, since this GPU serves every joiner.
				// A colocation query is a headset waiting to join: the urgent pace
				LocalizeOptions options = query.nearMapFrame
					? new LocalizeOptions
					{
						near = query.camera, nearDistance = loopSearchDistance,
						centerPriorSigma = loopCenterPriorSigma,
						maxMatchFeatures = loopMatchFeatures,
						minMutualToMatch = loopMinMutual,
						maxViewAngle = loopMaxViewAngle,
						guided = true,
					}
					: new LocalizeOptions { urgent = true };
				VisualLocalizationResult result = await localizer.LocalizeAsync(frames, options);
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
