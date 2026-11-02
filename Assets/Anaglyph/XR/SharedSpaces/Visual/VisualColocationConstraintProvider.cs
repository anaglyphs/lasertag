using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Anaglyph.XR.VisualLocalization;
using Unity.Mathematics;
using UnityEngine;
using Phase = Anaglyph.XR.SharedSpaces.CanonAlignment.Phase;
using Purpose = Anaglyph.XR.SharedSpaces.MeasurementPurpose;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// Colocation, relocalization and drift correction from one camera landmark map per
	/// headset (<see cref="VisualLocalizer"/>, its keyframes hung off a
	/// <see cref="TrackAnchoring"/>). Needs no scan of its own to colocate: a joiner's scan
	/// stays shut until it is aligned, and the authority's snapshot goes straight into it.
	///
	/// Every headset shares the views that add to what all of them hold (<see cref="VisualMapSync"/>),
	/// and a joiner aligns by localizing against them on its own GPU. A headset relocalizing after a
	/// recenter queries its own map. An aligned headset keeps measuring its drift where it can:
	/// revisits of its own map (the same place after a walk) and of other headsets' views merged
	/// into it (cross-links).
	/// Each joins a site (<see cref="DriftSites"/>), locked to the first views of its place,
	/// every site is one colocation constraint, and the colocator's fit over them aligns the rig.
	/// A <see cref="KeyframeGraph"/> over every headset's laps moves the views, and the sites locked
	/// to them, to where the laps agree. Results only count once a <see cref="MeasurementGate"/>
	/// lets them through.
	/// </summary>
	[RequireComponent(typeof(VisualLocalizer))]
	public class VisualColocationConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		[SerializeField] private bool queriesEnabled = true;
		[SerializeField] private float queryInterval = 1f;

		[SerializeField] private MeasurementGate.Settings gateSettings = new();
		[SerializeField] private DriftSites.Settings siteSettings = new();
		[SerializeField] private KeyframeGraph.Settings graphSettings = new();

		[Header("Revisits (drift correction)")]
		[SerializeField] private bool loopClosure = true;
		[Tooltip("Revisits measure against the first views of a place only: later laps' views, placed by whatever the fit was then, go to the graph alone")]
		[SerializeField] private bool lockRevisits = true;
		[Tooltip("Seconds between loop closure queries, at most; none is made unless an old keyframe is near")]
		[SerializeField] private float loopInterval = 3f;
		[Tooltip("Seconds before retrying a query whose frame was skipped (the head turning too fast to capture sharply): a skipped frame doesn't use up the interval")]
		[SerializeField] private float blurredRetry = 0.25f;
		[Tooltip("Old keyframes farther than this (m) from where the graph puts the camera are not tried")]
		[SerializeField] private float loopSearchDistance = 8f;
		[Tooltip("Meters walked, at least, between a loop query and the keyframes it may close against. A correction step alone makes the views of a moment ago count as another stretch of tracking, and matching them only gives back the correction they were placed with: on device this undid a 33 cm loop closure seconds after it was made")]
		[SerializeField] private float loopMinWalk = 6f;
		[Tooltip("How well the graph is assumed to know where the camera is before a loop closure, meters: the position prior far landmarks' yaw leans on")]
		[SerializeField] private float loopCenterPriorSigma = 1f;
		[Tooltip("Seconds after a revisit makes a site before the next loop query: drift grows with the walk between measurements, so on device one every ~5 m left the fit ~4.5 cm off at the next, where one every ~2 m should leave it ~3")]
		[SerializeField] private float loopCooldown = 1f;
		[Tooltip("Meters a revisit's camera prior is taken to be off before any drift is counted: with the drift allowed over the path since the last site, how far around each projected landmark a guided loop query looks")]
		[SerializeField] private float loopSearchFloor = 0.1f;
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

		private class Pending
		{
			public VisualQuery query;
			public Purpose purpose;
			public int epoch;
			public int generation;
			// the map points the solve stood on, for the site's debug view
			public Vector3[] features;
		}

		private float nextQuery;
		private float nextLoop;
		private float nextLoopTargetCheck;
		private const float LoopTargetRetry = 0.25f;
		private float nextQuietLoopLog;
		private bool queryInFlight;
		private float lastKeyframeAt;

		private VisualLocalizer localizer;
		private readonly TrackAnchoring track = new();
		private KeyframeGraph graph;
		private MeasurementGate gate;
		private DriftSites driftSites;
		private ColocationLog log;
		private int sitesGeneration = -1, sitesEpoch = -1;

		public static VisualColocationConstraintProvider Instance { get; private set; }

		/// <summary>The provider the colocator has active, if it is this one.</summary>
		public static VisualColocationConstraintProvider Active =>
			Colocator.Instance != null ? Colocator.Instance.Provider as VisualColocationConstraintProvider : null;

		private CanonAlignment alignment;

		/// <summary>
		/// A debugging view: holds the rig on the found alignment alone, as raw tracking has it,
		/// while everything behind it runs on (sites, their fit, and the correction keyframes are
		/// placed with), so it can be flipped either way mid-walk. The scan integrates wherever
		/// the rig is, so it smears when flipped far from the correction.
		/// </summary>
		public bool ShowRawTracking { get; set; }

		private readonly ColocationFit rawFit = new();
		private readonly List<ColocationConstraint> rawConstraints = new();

		/// <summary>The alignment found against the authority's frame, its phase, and the drift correction on top of it.</summary>
		public CanonAlignment Alignment => alignment;

		public bool IsAvailable => EnvScanner.Instance != null && MainXRRig.Instance != null;
		public bool IsRunning => alignment.IsRunning;

		public void StartProviding()
		{
			alignment.Start();
			if (Colocator.Instance != null)
			{
				Colocator.Instance.Fitted -= OnFitted;
				Colocator.Instance.Fitted += OnFitted;
			}
		}

		public void StopProviding()
		{
			alignment.Stop();
			if (Colocator.Instance != null) Colocator.Instance.Fitted -= OnFitted;
		}

		private static string LogTag => $"[{nameof(VisualColocationConstraintProvider)}]";

		public TrackAnchoring Track => track;
		public ConstraintSites Sites => driftSites.Sites;
		public KeyframeGraph Graph => graph;

		/// <summary>
		/// Every drift measurement that solved: the query, the site correction it implies (the
		/// whole correction, not the step from the one in force), the solve, the map segment it
		/// leaned on, and why it was turned down (null when
		/// it counted). For tests that know the truth.
		/// </summary>
		public event System.Action<VisualQuery, Pose4, GravityPnPResult, ulong, string> MeasurementSolved = delegate { };

		/// <summary>Raised when a measurement becomes or updates a site.</summary>
		public event System.Action<ConstraintSites.Site> SiteMeasured = delegate { };
		public VisualLocalizationResult LastResult { get; private set; }
		public string LastOutcome => log.LastOutcome;

		/// <summary>Recent outcomes, oldest first, each stamped with the realtime it happened at.</summary>
		public IEnumerable<string> RecentOutcomes => log.Recent;

		/// <summary>The latest attempt and what came of it, for diagnosis.</summary>
		public string LastAttemptDescription => LastOutcome ?? "none";

		/// <summary>
		/// Relocalizes against the map as it stands, a map just loaded for the space: the rig
		/// goes where the map says, as after a recenter. False when there is nothing to do it with.
		/// </summary>
		public bool RelocalizeAgainstMap() => localizer.Map.Count > 0 && alignment.BeginRelocalize("saved map");

		private void Awake()
		{
			Instance = this;
			localizer = GetComponent<VisualLocalizer>();
			log = new ColocationLog(this, LogTag, localizer);
			gate = new MeasurementGate(gateSettings, track.Drift, track.Walked);
			driftSites = new DriftSites(siteSettings);
			driftSites.Sites.WorldOf = track.WorldOf;
			graph = new KeyframeGraph(graphSettings, localizer, track);
			track.KnotFor = graph.KnotFor;
			alignment = new CanonAlignment(LogTag, () => IsAvailable, () => localizer.Map.Count > 0);
			localizer.KeyframeAdded += OnKeyframeAdded;
			localizer.KeyframeExtracted += graph.Add;
		}

		private void Start()
		{
			// keyframes hang off this headset's track; the localizer's map exists from its Awake
			localizer.Anchoring = track;
			localizer.Map.LocalOwner = graph.Owner;
			localizer.Map.Evicted += graph.OnEvicted;
			localizer.Map.Cleared += graph.Clear;
			// the map's frame is gone when the scan is
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared += ClearMap;
			alignment.PhaseChanged += OnPhaseChanged;
			alignment.FrameAbandoned += ClearScan;
			mapSync = new VisualMapSync(localizer, graph, track, LogTag, log.Outcome);
			mapSync.HostMapReceived += OnHostMapReceived;
			graphSync = new KeyframeGraphSync(graph);
		}

		private VisualMapSync mapSync;
		private KeyframeGraphSync graphSync;

		// the authority's map replaced this joiner's while it was aligned (its own was cleared): found against it again
		private void OnHostMapReceived()
		{
			if (alignment.CurrentPhase == Phase.Aligned) alignment.BeginRelocalize("the authority's map arrived");
		}

		private void OnDestroy()
		{
			if (Colocator.Instance != null) Colocator.Instance.Fitted -= OnFitted;
			alignment.Dispose();
			rawFit.Dispose();
			mapSync?.Dispose();
			graphSync?.Dispose();
			if (localizer)
			{
				localizer.Map.Evicted -= graph.OnEvicted;
				localizer.Map.Cleared -= graph.Clear;
				localizer.KeyframeExtracted -= graph.Add;
			}
			graph.Dispose();
			if (localizer) localizer.KeyframeAdded -= OnKeyframeAdded;
			if (EnvScanner.Instance != null) EnvScanner.Instance.Cleared -= ClearMap;
			alignment.PhaseChanged -= OnPhaseChanged;
			alignment.FrameAbandoned -= ClearScan;
			if (Instance == this) Instance = null;
		}

		/// <summary>Drops the map, its sites and track: the frame they were built in is gone (the scan cleared, or the space's frame changed).</summary>
		public void ClearMap()
		{
			if (localizer.Map.Count > 0)
				Debug.Log($"{LogTag} map cleared ({localizer.Quality}): its frame is gone");
			localizer.Map.Clear();
			driftSites.Clear();
			graph.Clear();
			track.Clear();
			gate.Clear();
		}

		// the scan was built in a frame that is gone
		private static void ClearScan()
		{
			if (EnvScanner.Instance != null) EnvScanner.Instance.Clear();
		}

		private void OnPhaseChanged(Phase phase)
		{
			// searching means this world is not canon, whatever the map was built in
			if (phase == Phase.Searching) ClearMap();
			gate.Clear();
		}

		// ---- asking ------------------------------------------------------------

		private void Update()
		{
			DriftModel drift = Colocator.Instance != null ? Colocator.Instance.DriftRates : DriftModel.Default;
			track.Model = drift;
			gate.Model = drift;
			alignment.Update();
			UpdateCorrection();
			graph.Update();
			graphSync?.Update();
			FitBehindRawTracking();
			mapSync?.Update(IsRunning);

			if (!queriesEnabled || queryInFlight || Time.unscaledTime < nextQuery || !localizer.IsAvailable) return;

			// a headset waiting on an alignment runs the networks over fewer frames (while
			// stopped, the pace is whoever else uses the localizer's to set)
			if (IsRunning) localizer.Urgent = alignment.CurrentPhase is Phase.Searching or Phase.Relocalizing;

			if (!NeedsCapture(out Purpose purpose)) return;
			nextQuery = Time.unscaledTime + queryInterval;
			Run(purpose);
		}

		// a frame of its own to capture: colocating, relocalizing, or standing near old views
		// where no new keyframe comes to make a loop query with
		private bool NeedsCapture(out Purpose purpose)
		{
			switch (alignment.CurrentPhase)
			{
				case Phase.Searching:
					// the authority's map, matched here once it has come
					purpose = Purpose.Colocate;
					return mapSync != null && mapSync.HasHostMap && localizer.Map.Count > 0;

				case Phase.Relocalizing:
					purpose = Purpose.Relocalize;
					return localizer.Map.Count > 0;

				case Phase.Aligned:
					purpose = Purpose.Loop;
					if (loopFromKeyframes && Time.unscaledTime - lastKeyframeAt < loopCaptureAfterQuiet) return false;
					if (Time.unscaledTime < nextLoopTargetCheck || !LoopAllowed(track.Current)) return false;
					if (OldViewNear(MainXRRig.Camera.transform.position, track.Current)) return true;
					// a scan of every keyframe: none near now means the head has to move first,
					// so not again every frame
					nextLoopTargetCheck = Time.unscaledTime + LoopTargetRetry;
					return false;

				default:
					purpose = default;
					return false;
			}
		}

		/// <summary>
		/// A new keyframe is a view the map didn't have; near old views from another stretch
		/// of tracking it is also the best loop query there is, and its features are already
		/// extracted. Matched at once, without waiting for a capture.
		/// </summary>
		private void OnKeyframeAdded(VisualKeyframe keyframe)
		{
			lastKeyframeAt = Time.unscaledTime;
			RecordMapped(keyframe);
			if (!loopFromKeyframes || queryInFlight || !LoopAllowed(keyframe.node) || !OldViewNear(keyframe.camera.position, keyframe.node)) return;

			Run(Purpose.Loop, new VisualQuery
			{
				features = keyframe.features,
				intrinsics = keyframe.intrinsics,
				camera = keyframe.camera,
				node = keyframe.node,
				cameraInNode = keyframe.cameraInNode,
				timestampNs = keyframe.timestampNs,
				frame = keyframe.frame,
			});
		}

		// aligned and tracking, and no sooner than the loop interval allows: each query is up to
		// keyframesPerQuery LighterGlue runs on the GPU (a confirming frame is let through at once)
		private bool LoopAllowed(ulong node) =>
			// the graph measures every new view against old ones, at a fraction of a query's networks
			loopClosure && !graphSettings.enabled && IsRunning && alignment.CurrentPhase == Phase.Aligned && Time.unscaledTime >= nextLoop &&
			track.IsTracking && node != 0 && localizer.Map.Count > 0;

		// a query costs an extraction and LighterGlue runs: only when an old view is near to find
		private bool OldViewNear(Vector3 camera, ulong node) =>
			localizer.HasUntrustedKeyframeNear(camera, node, loopSearchDistance, LoopMinWalkDrift, lockRevisits);

		/// <summary>Localizes <paramref name="query"/>, or the current frame captured when there is none, and considers the result.</summary>
		private async void Run(Purpose purpose, VisualQuery query = null)
		{
			queryInFlight = true;
			if (purpose == Purpose.Loop) nextLoop = Time.unscaledTime + loopInterval;
			try
			{
				Pending p = new()
				{
					purpose = purpose,
					epoch = alignment.FrameEpoch,
					generation = alignment.Generation,
					query = query,
				};

				p.query ??= await localizer.CaptureQueryAsync();
				if (!this) return;
				if (p.query == null)
				{
					// blurred (or no frame yet): try again shortly rather than after a whole interval
					nextQuery = Time.unscaledTime + blurredRetry;
					if (purpose == Purpose.Loop) nextLoop = Time.unscaledTime + blurredRetry;
					return;
				}

				await Localize(p);
			}
			finally
			{
				queryInFlight = false;
			}
		}

		private async Awaitable Localize(Pending p)
		{
			bool loop = p.purpose == Purpose.Loop;
			IReadOnlyList<VisualQuery> frames;
			LocalizeOptions options;
			if (loop)
			{
				if (!TryLoopOptions(p, out options)) return;
				// with the keyframes just before it: one solve over their baseline
				frames = FramesWith(p.query, loopFrames);
			}
			else
			{
				frames = new[] { p.query };
				// a joiner matching the authority's map is waiting on it: the urgent pace
				options = new LocalizeOptions { urgent = p.purpose == Purpose.Colocate };
			}

			VisualLocalizationResult result = await localizer.LocalizeAsync(frames, options);
			if (!this) return;
			LastResult = result;
			p.features = result.inlierPoints;

			// a loop query finding nothing old in view is the normal case: noted, at most every 10 s
			if (loop && result.keyframesMatched == 0)
			{
				if (Time.unscaledTime >= nextQuietLoopLog)
				{
					nextQuietLoopLog = Time.unscaledTime + 10f;
					log.Outcome($"Loop: nothing to close against: {result.failure} ({localizer.Quality})");
				}

				return;
			}
			Consider(p, result.pose, result.anchorNode, result.ToString());
		}

		/// <summary>
		/// A loop query's camera where the alignment placed it, matched by projection against
		/// views from another stretch of tracking, looking as far around each landmark as the
		/// drift since the last site is expected to have moved it. False when its segment is gone.
		/// </summary>
		private bool TryLoopOptions(Pending p, out LocalizeOptions options)
		{
			options = default;
			if (p.query.node == 0 || !Landmark.TryCamera(localizer.Anchoring, p.query.node, p.query.cameraInNode, out p.query.camera)) return false;
			options = new LocalizeOptions
			{
				near = p.query.camera, nearDistance = loopSearchDistance, untrustedFrom = p.query.node,
				centerPriorSigma = loopCenterPriorSigma,
				maxMatchFeatures = loopMatchFeatures,
				minMutualToMatch = loopMinMutual,
				maxViewAngle = loopMaxViewAngle,
				guided = true,
				guidedTranslation = ExpectedLoopDrift(p),
				minLoopWalkDrift = LoopMinWalkDrift,
				lockedOnly = lockRevisits,
			};
			return true;
		}

		// ---- deciding ----------------------------------------------------------

		/// <summary>
		/// A solved query: gated, agreed with a second frame, then applied as the alignment
		/// (colocating, relocalizing) or kept as a site (a loop closure measuring drift).
		/// </summary>
		private void Consider(Pending p, GravityPnPResult pose, ulong anchor, string description)
		{
			if (!IsRunning) return;
			log.About(p.query);

			// the frame the query was captured in has to still be the one aligned against
			if (p.epoch != alignment.FrameEpoch || p.generation != alignment.Generation)
			{
				log.Outcome($"{p.purpose}: discarded, the alignment changed while localizing ({description})");
				return;
			}

			bool loop = p.purpose == Purpose.Loop;
			float3 camera = p.query.camera.position;
			Pose4 correction = new(pose.translation, pose.yaw);
			string rejection = gate.Rejection(pose, p.purpose) ?? (loop ? gate.Implausible(correction, camera, WalkedSinceSite(p)) : null);

			// a drift measurement maps the camera as its segment's correction placed it onto the
			// map: composed with that correction, it is the whole correction the site needs
			if (loop) correction = correction * track.PlacementOf(p.query.node);
			if (loop && pose.success)
				MeasurementSolved.Invoke(p.query, correction, pose, anchor, rejection);

			// a frame that fails says nothing against the candidate: it stays for the next good one
			if (rejection != null)
			{
				log.Outcome($"{p.purpose}: not accepted, {rejection} ({description})");
				return;
			}

			MeasurementGate.Frame frame = new()
			{
				purpose = p.purpose, generation = p.generation, epoch = p.epoch,
				node = p.query.node, anchor = anchor, camera = camera,
				correction = correction, solve = pose, features = p.features,
			};
			if (!gate.TryAgree(frame, out MeasurementGate.Agreed agreed))
			{
				log.Outcome($"{p.purpose}: candidate {correction}, waiting for a second frame to agree ({description})");
				// the confirming frame at once, while the old views are still in sight
				nextQuery = Time.unscaledTime;
				if (loop) nextLoop = Time.unscaledTime;
				return;
			}

			if (agreed.alone) description = $"one frame, confident enough alone: {description}";
			if (loop) AddSite(p, agreed, pose.yawSigma, anchor, description);
			else ApplyAlignment(p, agreed.correction, description);
		}

		private void ApplyAlignment(Pending p, Pose4 agreed, string description)
		{
			if (!IsAvailable)
			{
				log.Outcome($"{p.purpose}: not applied, {agreed} found while the rig or scanner is missing");
				return;
			}

			alignment.Apply(agreed, p.query.trackingSpace);
			log.Outcome($"{p.purpose}: applied {agreed} ({description})");
		}

		private void AddSite(Pending p, in MeasurementGate.Agreed agreed, float yawSigma, ulong anchor, string description)
		{
			// a revisit of views this headset's tracking didn't make is a cross-link
			bool crossLink = !track.IsOwn(anchor);
			float3 camera = p.query.camera.position;

			ConstraintSites.Site site = driftSites.Add(agreed, camera, yawSigma, track.Path, Time.realtimeSinceStartup, crossLink, anchor);
			graph.AddMeasurement(p.query.node, anchor, agreed.correction, agreed.focus, site.Latest.sigmaTranslation, site.Latest.sigmaYaw);
			nextLoop = Time.unscaledTime + loopCooldown;
			log.Outcome($"{p.purpose}{(crossLink ? " (cross-link)" : "")}: site {site.id} ({site.measurements} measurements, {site.features?.Length ?? 0} features, {Sites.Count} sites) now {agreed.correction} ({description})");
			log.SiteLap(site, crossLink, alignment.SiteCorrection, Sites.Count);
			SiteMeasured.Invoke(site);
		}

		/// <summary>How far off a revisit's camera prior may be, meters: the drift allowance for the path since the latest site, over a floor.</summary>
		private float ExpectedLoopDrift(Pending p) => loopSearchFloor + track.Model.PositionAllowance(WalkedSinceSite(p));

		private float LoopMinWalkDrift => track.Model.PositionSigma(loopMinWalk);

		private float WalkedSinceSite(Pending p) => Sites.PathSinceLatest(track.PathOf(p.query.node), track.PathOf(p.query.node));

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

			Pose4 placement = track.PlacementOf(latest.node);
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
				Pose camera = placement.Apply(track.CorrectionOf(k.node).Inverse.Apply(k.cameraInNode));
				frames.Add(new VisualQuery
				{
					features = k.features,
					intrinsics = k.intrinsics,
					camera = camera,
					node = k.node,
					cameraInNode = k.cameraInNode,
					timestampNs = k.timestampNs,
					frame = k.frame,
				});
			}

			return frames;
		}

		// ---- correcting --------------------------------------------------------

		/// <summary>
		/// A new alignment or a recenter leaves every site's correction describing a frame that
		/// is gone; the track follows the head and the correction in force.
		/// </summary>
		private void UpdateCorrection()
		{
			if (sitesGeneration != alignment.Generation || sitesEpoch != alignment.FrameEpoch)
			{
				driftSites.Clear();
				sitesGeneration = alignment.Generation;
				sitesEpoch = alignment.FrameEpoch;
			}

			track.Update(alignment);
		}

		/// <summary>
		/// The colocator fit the sites and moved the rig there: the correction the sites call
		/// for is where the tracking space now stands against the found alignment, the one
		/// keyframes are placed with. Without sites the fit only holds the alignment's own
		/// constraint, which already carries the correction
		/// </summary>
		private void OnFitted(Pose space)
		{
			if (ShowRawTracking || !IsRunning || alignment.CurrentPhase != Phase.Aligned || (Sites.Count == 0 && !graph.HasConstraints) || !ReferenceEquals(Active, this)) return;
			SetCorrectionFrom(space);
		}

		private void SetCorrectionFrom(Pose space)
		{
			Pose canon = alignment.CanonSpacePose;
			alignment.SiteCorrection = Pose4.FromMatrix(Matrix4x4.TRS(space.position, space.rotation, Vector3.one) *
				Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse);
		}

		// the colocator holds the rig on raw tracking and fits nothing: the fit the sites call for, run here
		private void FitBehindRawTracking()
		{
			if (!ShowRawTracking || alignment.CurrentPhase != Phase.Aligned || (Sites.Count == 0 && !graph.HasConstraints) || !IsAvailable || Colocator.Instance == null) return;
			rawConstraints.Clear();
			AddDriftConstraints(rawConstraints);
			if (!rawFit.TryEvaluate(rawConstraints, Colocator.Instance.Drift, out Pose delta)) return;
			Transform space = MainXRRig.TrackingSpace;
			SetCorrectionFrom(new Pose(delta.position + delta.rotation * space.position, delta.rotation * space.rotation));
		}

		// ---- constraints -------------------------------------------------------

		// a keyframe where this session's map had none near yet stands as a measurement
		private void RecordMapped(VisualKeyframe keyframe)
		{
			if (!IsRunning || alignment.CurrentPhase != Phase.Aligned || keyframe.node == 0 || !track.IsTracking) return;
			driftSites.RecordMapped(keyframe.camera.position, track.CorrectionOf(keyframe.node), track.PathOf(keyframe.node), Time.realtimeSinceStartup);
		}

		/// <summary>One constraint per site and one from the graph once there are any; the found alignment's own until then.</summary>
		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (alignment.CurrentPhase != Phase.Aligned || !IsAvailable) return;
			if (ShowRawTracking) alignment.AddUncorrectedConstraint(results);
			else if (Sites.Count == 0 && !graph.HasConstraints) alignment.AddConstraint(results);
			else AddDriftConstraints(results);
		}

		private void AddDriftConstraints(List<ColocationConstraint> results)
		{
			Matrix4x4 worldFromPlaced = WorldFromPlaced;
			driftSites.AddConstraints(results, worldFromPlaced);
			if (graph.TryTipConstraint(worldFromPlaced, out ColocationConstraint tip)) results.Add(tip);
			graph.AddFixConstraints(results, worldFromPlaced);
		}

		/// <summary>Where this session first mapped each place and the correction it was mapped with, for diagnosis.</summary>
		public IReadOnlyList<ConstraintSites.Measurement> MappedPlaces => driftSites.Mapped;

		// from where the uncorrected alignment places things, through the tracking space as it stands
		private Matrix4x4 WorldFromPlaced
		{
			get
			{
				Pose canon = alignment.CanonSpacePose;
				return MainXRRig.TrackingSpace.localToWorldMatrix * Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one).inverse;
			}
		}

		private void LateUpdate()
		{
			if (!AnaglyphDebugging.DebugMode || MainXRRig.Instance == null || Sites.Count == 0 || alignment.CurrentPhase != Phase.Aligned) return;
			driftSites.DrawDebug(WorldFromPlaced);
		}
	}
}
