using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>Features of one captured frame and where its camera was, in the capturing headset's world.</summary>
	public sealed class VisualQuery
	{
		public FeatureSet features;
		public PinholeCamera intrinsics;
		/// <summary>The camera in the capturing headset's world.</summary>
		public Pose camera;
		public Pose cameraInTrackingSpace;
		/// <summary>The tracking space's world pose at capture, which a correction found for this query applies to.</summary>
		public Pose trackingSpace;
		/// <summary>The pose-graph node the frame was captured under, and the camera relative to it; node 0 when not anchored.</summary>
		public ulong node;
		public Pose cameraInNode;
		public long timestampNs;
		/// <summary>Its <see cref="VisualFrameLog"/> record; 0 when not logged.</summary>
		public int frame;
		/// <summary>The capturing headset believes its world is already close to the map's frame, so its camera pose is a prior.</summary>
		public bool nearMapFrame;
	}

	public struct LocalizeOptions
	{
		/// <summary>Only keyframes within <see cref="nearDistance"/> of this camera are tried.</summary>
		public Pose? near;
		public float nearDistance;
		/// <summary>
		/// Keyframes whose node is within the trust window of this node are skipped: a loop
		/// closure is against views from another stretch of tracking only. 0 for none.
		/// </summary>
		public ulong untrustedFrom;
		/// <summary>Keyframes facing more than this many degrees away from the query camera are skipped; 0 for any.</summary>
		public float maxViewAngle;
		/// <summary>
		/// How well the query camera's position in the map frame is known beforehand,
		/// meters; 0 for the solver's default (loose). A far landmark pins yaw only as well
		/// as the camera's position: a tight prior lets it.
		/// </summary>
		public float centerPriorSigma;
		/// <summary>
		/// Features per side LighterGlue attends over, strongest first; 0 for all. Its cost
		/// grows with the square of the count, so a background query halving it costs a quarter.
		/// </summary>
		public int maxMatchFeatures;
		/// <summary>Keyframes retrieval ranks below this many shared features are not worth a LighterGlue run; 0 for the retrieval floor.</summary>
		public int minMutualToMatch;
		/// <summary>
		/// Something waits on the answer (a joiner's query, served by the authority): retrieval
		/// and matching run at the urgent pace whatever the localizer's own state.
		/// </summary>
		public bool urgent;
		/// <summary>
		/// Match by projecting the landmarks of the views near <see cref="near"/> through it
		/// into the query image (<see cref="GuidedMatcher"/>) rather than with LighterGlue:
		/// for a query whose camera prior is good to within <see cref="guidedTranslation"/>
		/// and a few degrees. Needs <see cref="near"/>.
		/// </summary>
		public bool guided;
		/// <summary>Meters the prior's position may be off, widening each landmark's window by its depth; 0 for the localizer's default.</summary>
		public float guidedTranslation;
	}

	public struct VisualLocalizationResult
	{
		public GravityPnPResult pose;
		public int keyframesMatched;
		public int bestMutual;
		/// <summary>The node the matched landmarks are anchored to (those of other nodes too far from it in drift are left out).</summary>
		public ulong anchorNode;
		/// <summary>The map points of the ranged inliers (within 30 m), where the map has them: what the solve stood on.</summary>
		public Vector3[] inlierPoints;
		public string failure;

		public bool Success => pose.success;

		public override string ToString() => pose.success
			? $"{pose} keyframes={keyframesMatched} mutual={bestMutual}"
			: $"failed: {failure ?? pose.failure} keyframes={keyframesMatched} mutual={bestMutual} correspondences={pose.correspondences}";
	}

	/// <summary>
	/// Visual relocalization and loop closure against a map of tracked landmarks.
	///
	/// Mapping: whenever the pose graph tracks the headset (<see cref="Anchoring"/>), new
	/// views of the passthrough camera become keyframes anchored to the current node. Each
	/// is matched (LighterGlue) to the keyframes near it whose nodes are within the drift
	/// window, consecutive views of one stretch of tracking, and the matches extend
	/// landmark tracks, whose inverse depth is triangulated from the sightings with the
	/// scanned mesh as an optional prior. Views from across a longer walk are never
	/// triangulated together: that is what a loop closure is for.
	///
	/// Localizing: a query frame (this headset's or another's) retrieves the keyframes
	/// that share the most features, matches them, and solves the gravity-aligned pose
	/// from the landmarks those matches sight, keeping only landmarks whose nodes agree
	/// with each other in drift. Far landmarks pin yaw, ranged ones translation, and the
	/// result carries both uncertainties for the caller to gate on.
	/// </summary>
	public class VisualLocalizer : MonoBehaviour
	{
		public static VisualLocalizer Instance { get; private set; }

		/// <summary>When set before the localizer wakes, overrides freezeEstablishedLandmarks: for A/B tests.</summary>
		public static bool? FreezeOverride;

		[Header("Mapping")]
		[Tooltip("Seconds after a keyframe before the next is tried")]
		[SerializeField] private float mappingInterval = 0.5f;
		[Tooltip("Seconds before trying again after a frame that couldn't be a keyframe (blurred, or nothing new): soon, so the calm moments of a moving head are caught")]
		[SerializeField] private float mappingRetry = 0f;
		[Tooltip("A head turning slower than this (°/s) is still enough to capture at once; between it and the blur limit, capture waits while the turn is still slowing quickly, for the stillest frame of the settle")]
		[SerializeField] private float calmAngularSpeed = 15f;
		[Tooltip("Seconds before looking again when the view is nothing new: standing still, no image is converted every frame only to be dropped")]
		[SerializeField] private float notNovelRetry = 0.1f;
		[SerializeField] private float keyframeMinDistance = 0.5f;
		[SerializeField] private float keyframeMinAngle = 20f;
		[Tooltip("Head rotation above this (°/s) blurs the passthrough image too much to map or query")]
		[SerializeField] private float maxAngularSpeed = 40f;
		[SerializeField] private int minKeyframeFeatures = 100;
		[SerializeField] private int mapCapacity = 256;
		[Tooltip("When the map is full, keyframes are given up from the most crowded bucket of place and heading, so every place and direction keeps a view. Cell side at the finest level, meters")]
		[SerializeField] private float coverageCell = 1f;
		[Tooltip("Heading bins at the finest level; each coarsening halves them and doubles the cell")]
		[SerializeField] private int coverageHeadings = 8;

		[Header("Tracks")]
		[Tooltip("A landmark triangulated from the views that founded it is fixed from then on: later views sight it but never refit it")]
		[SerializeField] private bool freezeEstablishedLandmarks = true;
		[Tooltip("A landmark is established once its depth sigma is within this fraction of its depth...")]
		[SerializeField] private float establishedDepthSigma = 0.1f;
		[Tooltip("...or once this many views have sighted it, whichever comes first")]
		[SerializeField] private int establishedViews = 6;
		[Tooltip("Views a landmark must have survived before it's established: two always agree along their epipolar line, so a mismatched track takes a third view to show")]
		[SerializeField] private int establishedMinViews = 3;
		[Tooltip("Expected drift (m) between two keyframes' nodes up to which their views are triangulated together; beyond it they can only be loop-closed")]
		[SerializeField] private float trustDrift = 0.05f;
		[SerializeField] private int trackNeighbors = 3;
		[SerializeField] private float neighborDistance = 4f;
		[Tooltip("Match track neighbors with LighterGlue instead of descriptor mutual nearest neighbors: costlier on the GPU, rarely needed between nearby views")]
		[SerializeField] private bool lighterGlueForTracks = false;
		[Tooltip("Lowest descriptor cosine a mutual nearest neighbor match may have")]
		[SerializeField] private float trackMinCosine = 0.82f;

		[Header("Scanned mesh (optional depth)")]
		[SerializeField] private bool useMeshDepth = false;
		[Tooltip("Landmarks seen once but placed by the mesh may be matched; otherwise a landmark needs a second sighting")]
		[SerializeField] private bool singleViewMeshLandmarks = false;
		[SerializeField] private float maxRayDistance = 8f;
		[Tooltip("Hits more grazing than this (cosine to the surface normal) are dropped: depth edges and slanted walls")]
		[SerializeField] private float minHitCosine = 0.2f;
		[SerializeField] private float meshDepthSigma = 0.02f;
		[SerializeField] private float meshDepthFraction = 0.01f;
		[SerializeField] private string environmentLayer = "EnvironmentMesh";

		[Header("Inference")]
		[Tooltip("Where XFeat runs: GPUCompute, or CPU (Burst jobs on worker threads) to leave the GPU to rendering")]
		[SerializeField] private BackendType extractorBackend = BackendType.GPUCompute;
		[Tooltip("Where LighterGlue runs. CPU measured 16 ms at 512 points a side on an M-series Mac (GPU 9 ms); expect several times that on Quest, on worker threads")]
		[SerializeField] private BackendType matcherBackend = BackendType.GPUCompute;
		[Tooltip("Frames each network run is spread over, so no one frame carries a whole network; 1 runs it at once")]
		[SerializeField] private int framesPerRun = 6;
		[Tooltip("Frames per run while the headset is waiting on an alignment (relocalizing, joining): latency matters more then")]
		[SerializeField] private int urgentFramesPerRun = 2;
		[Tooltip("Frames each LighterGlue match is spread over outside urgent alignments (loop closure). It is ~700 layers and the heaviest network; spreading it thin keeps background queries out of the frame time")]
		[SerializeField] private int matcherFramesPerRun = 30;
		[Tooltip("Keyframes scored per frame when retrieving the views a query might share, each a few million multiply-adds on a worker thread; without a camera to narrow it, a query is scored against the whole map")]
		[SerializeField] private int retrievalCandidatesPerFrame = VisualMap.DefaultCandidatesPerFrame;
		[Tooltip("Keyframes scored per frame while something waits on the result (a relocalization, a joiner's query): the whole map in a few frames, at the cost of those frames' worker time")]
		[SerializeField] private int urgentRetrievalCandidatesPerFrame = 16;

		[Header("Localization")]
		[Tooltip("Views LighterGlue matches a query with no camera prior against, best shared features first")]
		[SerializeField] private int keyframesPerQuery = 2;
		[SerializeField] private int minMutual = 12;
		[SerializeField] private GravityPnP.Settings pnp = GravityPnP.Settings.Default;

		[Header("Guided matching")]
		[Tooltip("A query with a camera prior (a revisit, a cross-link) is matched by projecting the landmarks near it into its image and taking mutual nearest descriptors within a window, in a Burst job: LighterGlue is kept for queries with no prior")]
		[SerializeField] private bool guidedMatching = true;
		[Tooltip("Views matched: of the nearest to the prior, those whose landmarks the prior projects into the image most")]
		[SerializeField] private int guidedKeyframes = 10;
		[Tooltip("Pixels a landmark may lie from where the prior projects it before its depth counts: the prior's yaw error (32 px is about 6°) and feature noise")]
		[SerializeField] private float guidedWindowPixels = 32f;
		[Tooltip("Meters the prior's position may be off unless the query says: a point at depth d gets f·e/d more pixels of window, one at infinity none")]
		[SerializeField] private float guidedTranslation = 0.5f;
		[Tooltip("Lowest descriptor cosine a guided match may have")]
		[SerializeField] private float guidedMinCosine = 0.78f;
		[Tooltip("A landmark's best match must beat the runner-up in its window by this much cosine: repeated structure gives near ties, and a mutual check alone can't tell them apart")]
		[SerializeField] private float guidedMinMargin = 0.03f;
		[Tooltip("A view sharing fewer guided matches than this with the query contributes none: so few are chance")]
		[SerializeField] private int guidedMinMatches = 8;

		[Header("Diagnosis")]
		[Tooltip("Keep the frames behind the map (and recent ones besides) for AlignmentDiagnosis to save")]
		[SerializeField] private bool recordFrames = true;
		[Tooltip("Frames kept besides those of keyframes still in the map")]
		[SerializeField] private int recentFrames = 96;

		/// <summary>
		/// Something is waiting on an alignment (relocalizing, a joiner searching): networks
		/// run over <see cref="urgentFramesPerRun"/> frames instead of <see cref="framesPerRun"/>.
		/// </summary>
		public bool Urgent
		{
			get => urgent;
			set
			{
				urgent = value;
				if (extractor != null) extractor.FramesPerRun = value ? urgentFramesPerRun : framesPerRun;
			}
		}

		private bool urgent;

		// matching and retrieval take their pace per call: a query served for another headset
		// is urgent whatever this one's state
		private int MatcherFrames(bool urgent) => urgent ? urgentFramesPerRun : matcherFramesPerRun;
		private int RetrievalCandidatesPerFrame(bool urgent) => urgent ? urgentRetrievalCandidatesPerFrame : retrievalCandidatesPerFrame;

		/// <summary>The pose graph keyframes are anchored to; no mapping without one.</summary>
		public IKeyframeAnchoring Anchoring
		{
			get => Map.Anchoring;
			set => Map.Anchoring = value;
		}

		public VisualMap Map { get; private set; }

		/// <summary>The frames behind the map and recent queries, with what became of each.</summary>
		public VisualFrameLog Frames { get; } = new();
		public bool IsAvailable => capture != null;
		public float TrustDrift => trustDrift;
		/// <summary>Feature localization noise, pixels: what a sighting's angular sigma is made of.</summary>
		public float PixelSigma => pnp.pixelSigma;

		/// <summary>What became of mapping attempts since the start (or since a test reset them).</summary>
		public struct MappingCounts
		{
			public int attempts, noFrame, blurred, notNovel, fewFeatures, stale, added;
			public override string ToString() =>
				$"{added} keyframes of {attempts} attempts: {blurred} blurred, {notNovel} nothing new, {noFrame} no frame, {fewFeatures} too few features, {stale} map changed";
		}

		public MappingCounts Mapping;

		public VisualMapQuality Quality => Map.Quality(singleViewMeshLandmarks, pnp.rangedFraction);

		private ARCameraManager cameraManager;
		private PassthroughCapture capture;
		private CapturedImage image;
		private XFeatExtractor extractor;
		private LighterGlueMatcher matcher;
		private long? lastFrameTimestampNs;
		private float nextMapping;
		// one capture-and-extract at a time (they share the image buffer), and one LighterGlue
		// run at a time: separately, so mapping goes on while a loop query matches
		private bool extractorBusy, matcherBusy;

		// the head's turn rate, frame to frame, for priming captures on the moment it settles
		private Quaternion lastHeadRotation;
		private bool hasHeadRotation;
		private float turnRate, previousTurnRate;

		public event Action<VisualKeyframe> KeyframeAdded = delegate { };

		private void Awake()
		{
			Instance = this;
			Map = new VisualMap(mapCapacity)
			{
				coverageCell = coverageCell, coverageHeadings = coverageHeadings,
				freezeEstablished = FreezeOverride ?? freezeEstablishedLandmarks,
			};
			Frames.enabled = recordFrames;
			Frames.capacity = recentFrames;
			Map.Evicted += k => Frames.Pin(k.frame, false);
			Map.Cleared += Frames.UnpinAll;
		}

		private void Start()
		{
			cameraManager = FindFirstObjectByType<ARCameraManager>();
			if (cameraManager == null || MainXRRig.Instance == null)
			{
				Debug.Log($"[{nameof(VisualLocalizer)}] no AR camera: visual localization unavailable");
				enabled = false;
				return;
			}

			cameraManager.frameReceived += OnFrameReceived;
			capture = new PassthroughCapture(cameraManager);
			image = new CapturedImage();
			try
			{
				extractor = new XFeatExtractor(extractorBackend, framesPerRun);
				matcher = new LighterGlueMatcher(matcherBackend, matcherFramesPerRun);
				Debug.Log($"[{nameof(VisualLocalizer)}] XFeat on {extractorBackend} over {framesPerRun} frames, LighterGlue on {matcherBackend} over {matcherFramesPerRun} ({urgentFramesPerRun} each when urgent)");
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
				capture = null;
				enabled = false;
			}
		}

		private void OnDestroy()
		{
			if (cameraManager) cameraManager.frameReceived -= OnFrameReceived;
			capture?.Dispose();
			image?.Dispose();
			extractor?.Dispose();
			matcher?.Dispose();
			Map?.Dispose();
			if (Instance == this) Instance = null;
		}

		private void OnFrameReceived(ARCameraFrameEventArgs args) => lastFrameTimestampNs = args.timestampNs;

		// the head where the graph has it now, against the trusted keyframes' last placement:
		// a cheap stand-in for the capture's own check, which still decides
		private bool LooksNovel()
		{
			IKeyframeAnchoring anchoring = Anchoring;
			if (anchoring == null || MainXRRig.Instance == null) return false;
			Transform head = MainXRRig.Camera.transform, space = MainXRRig.TrackingSpace;
			Pose inTrackingSpace = new(space.InverseTransformPoint(head.position), Quaternion.Inverse(space.rotation) * head.rotation);
			if (!anchoring.TryAnchor(inTrackingSpace, out ulong node, out Pose cameraInNode)) return false;
			if (!Landmark.TryCamera(anchoring, node, cameraInNode, out Pose camera)) return false;
			return Map.IsNovel(camera, node, trustDrift, keyframeMinDistance, keyframeMinAngle);
		}

		private void TrackTurnRate()
		{
			if (MainXRRig.Instance == null) return;
			Quaternion rotation = MainXRRig.Camera.transform.localRotation;
			previousTurnRate = turnRate;
			turnRate = hasHeadRotation && Time.unscaledDeltaTime > 0 ? Quaternion.Angle(lastHeadRotation, rotation) / Time.unscaledDeltaTime : float.PositiveInfinity;
			lastHeadRotation = rotation;
			hasHeadRotation = true;
		}

		private void Update()
		{
			TrackTurnRate();
			if (capture == null || Anchoring == null || extractorBusy || Time.unscaledTime < nextMapping) return;

			// primed, not polled: a frame is taken the moment the head settles under the blur
			// limit, every frame it stays there; while a turn is still slowing quickly, the next
			// frame is the sharper one
			if (turnRate > maxAngularSpeed) return;
			if (turnRate > calmAngularSpeed && turnRate < previousTurnRate * 0.8f) return;

			// nothing new in view (standing, or retracing a step): decided from the head pose,
			// before an image is acquired and converted on this thread
			if (!LooksNovel())
			{
				nextMapping = Time.unscaledTime + notNovelRetry;
				return;
			}

			nextMapping = Time.unscaledTime + mappingRetry;
			TryAddKeyframe();
		}

		// ---- mapping -----------------------------------------------------------

		private async void TryAddKeyframe()
		{
			if (!await Acquire(Lock.Extractor)) return;
			try
			{
				Mapping.attempts++;
				if (!capture.TryCapture(image, lastFrameTimestampNs, maxAngularSpeed))
				{
					if (image.angularSpeed > maxAngularSpeed) Mapping.blurred++;
					else Mapping.noFrame++;
					return;
				}

				// anchored at capture: the node the head was at then, whatever happens while extracting
				IKeyframeAnchoring anchoring = Anchoring;
				if (anchoring == null || !anchoring.TryAnchor(image.cameraInTrackingSpace, out ulong node, out Pose cameraInNode)) return;
				if (!Landmark.TryCamera(anchoring, node, cameraInNode, out Pose camera)) return;

				Map.RefreshPlacement();
				if (!Map.IsNovel(camera, node, trustDrift, keyframeMinDistance, keyframeMinAngle))
				{
					Mapping.notNovel++;
					nextMapping = Time.unscaledTime + notNovelRetry;
					return;
				}

				// the first view of a place, as opposed to a revisit's (novel only because the views
				// already there are from another stretch of tracking): kept for good
				bool original = Map.IsNovel(camera, node, float.PositiveInfinity, keyframeMinDistance, keyframeMinAngle);

				// everything about the frame is read now: the image buffer is reused
				PinholeCamera intrinsics = image.intrinsics;
				long timestampNs = image.timestampNs;
				int mapRevision = Map.Revision;
				VisualFrameRecord record = Frames.Begin("map", image, node, cameraInNode);
				int frame = record?.sequence ?? 0;

				FeatureSet features = await extractor.ExtractAsync(image.pixels, image.content);
				if (record != null) record.features = features;
				if (features == null || !this) return;
				if (features.Count < minKeyframeFeatures)
				{
					Mapping.fewFeatures++;
					Frames.Note(frame, $"not a keyframe: {features.Count} features < {minKeyframeFeatures}");
					return;
				}

				if (Map.Revision != mapRevision)
				{
					Mapping.stale++;
					Frames.Note(frame, "not a keyframe: the map changed while extracting");
					return;
				}

				VisualKeyframe keyframe = new()
				{
					features = features,
					intrinsics = intrinsics,
					node = node,
					cameraInNode = cameraInNode,
					landmarks = new int[features.Count],
					timestampNs = timestampNs,
					frame = frame,
					original = original,
				};
				Array.Fill(keyframe.landmarks, -1);

				// the node may have moved in a solve meanwhile; the mesh moved with it
				if (!Landmark.TryCamera(anchoring, node, cameraInNode, out keyframe.camera)) return;
				if (useMeshDepth) MeshDepths(keyframe);

				await ExtendTracks(keyframe, mapRevision);
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
			finally
			{
				extractorBusy = false;
			}
		}

		/// <summary>
		/// Matches the new keyframe against trusted neighbors: a match to a feature that
		/// already sights a landmark extends its track, a match to one that doesn't starts a
		/// track there. Then every touched landmark is re-triangulated.
		/// </summary>
		private async Awaitable ExtendTracks(VisualKeyframe keyframe, int mapRevision)
		{
			List<(VisualKeyframe keyframe, int mutual)> neighbors = await Map.RetrieveAsync(keyframe.features, trackNeighbors, minMutual,
				k => Map.IsTrusted(k, keyframe.node, trustDrift), keyframe.camera, neighborDistance, candidatesPerFrame: RetrievalCandidatesPerFrame(urgent));
			if (!this || Map.Revision != mapRevision) return;

			// why no neighbor: none near, or none near and trusted (a glance's view shares too little with them)
			string lonely = null;
			if (neighbors.Count == 0)
			{
				int near = 0, trusted = 0;
				foreach (VisualKeyframe k in Map.Keyframes)
				{
					if (k == keyframe || Vector3.Distance(k.camera.position, keyframe.camera.position) > neighborDistance) continue;
					near++;
					if (Map.IsTrusted(k, keyframe.node, trustDrift)) trusted++;
				}

				lonely = $" (alone: {near} near, {trusted} trusted)";
			}

			List<(VisualKeyframe neighbor, int[] matches)> matched = new();
			foreach ((VisualKeyframe neighbor, int _) in neighbors)
			{
				int[] matches;
				if (lighterGlueForTracks)
				{
					if (!await Acquire(Lock.Matcher)) return;
					try { (matches, _) = await matcher.MatchAsync(keyframe.features, neighbor.features, MatcherFrames(urgent)); }
					finally { matcherBusy = false; }
				}
				else (matches, _) = await MutualNearestMatcher.MatchAsync(keyframe.features, neighbor.features, trackMinCosine);
				if (!this || Map.Revision != mapRevision) return;
				if (matches != null) matched.Add((neighbor, matches));
			}

			// the anchoring's graph may have been cleared while matching
			if (Map.Anchoring == null || !Map.Anchoring.TryGetNodeWorld(keyframe.node, out _))
			{
				Frames.Note(keyframe.frame, "not a keyframe: its node left the graph while matching");
				return;
			}

			Map.Add(keyframe);
			Frames.Pin(keyframe.frame, true);
			HashSet<Landmark> touched = new();
			int extended = 0, started = 0, sighted = 0;
			foreach ((VisualKeyframe neighbor, int[] matches) in matched)
			{
				for (int i = 0; i < matches.Length; i++)
				{
					int j = matches[i];
					if (j < 0) continue;
					if (!Map.TryGetLandmark(neighbor.landmarks[j], out Landmark landmark))
					{
						landmark = Map.CreateLandmark(neighbor, j, pnp.pixelSigma, meshDepthSigma, meshDepthFraction);
						started++;
					}
					else if (landmark.established) sighted++;
					else extended++;

					if (Map.Link(keyframe, i, landmark, pnp.pixelSigma)) touched.Add(landmark);
				}
			}

			// what the mesh places but no neighbor shares starts a single-view track
			if (singleViewMeshLandmarks && keyframe.meshDepth != null)
				for (int i = 0; i < keyframe.features.Count; i++)
					if (keyframe.landmarks[i] < 0 && keyframe.meshDepth[i] > 0)
						touched.Add(Map.CreateLandmark(keyframe, i, pnp.pixelSigma, meshDepthSigma, meshDepthFraction));

			// the fits on a worker, against the node poses as they are now; the unlinking back here
			NodePoseSnapshot nodes;
			using (SnapshotMarker.Auto()) nodes = new NodePoseSnapshot(Map.Anchoring, touched);
			Landmark[] fitting = new Landmark[touched.Count];
			touched.CopyTo(fitting);
			List<Observation>[] rejections = new List<Observation>[fitting.Length];
			await Awaitable.BackgroundThreadAsync();
			try
			{
				for (int i = 0; i < fitting.Length; i++) rejections[i] = fitting[i].Estimate(nodes);
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			if (!this) return;
			int dropped = 0;
			foreach (List<Observation> rejected in rejections)
			{
				if (rejected == null) continue;
				foreach (Observation o in rejected) Map.Unlink(o);
				dropped += rejected.Count;
			}

			// founded: once a third view of its own stretch of tracking has checked it, and the views
			// have ranged it well (or there are enough of them that more won't help), a landmark is
			// fixed for good
			foreach (Landmark l in fitting)
				if (l.observations.Count >= establishedMinViews && float.IsFinite(l.inverseDepthSigma) &&
				    (l.inverseDepthSigma <= establishedDepthSigma * l.inverseDepth || l.observations.Count >= establishedViews))
					l.established = true;

			Mapping.added++;
			// a view no other keyframe shares (a glance, mostly) triangulates nothing until a
			// second view of it comes: take the next sharp frame at once, while it's still in sight
			nextMapping = Time.unscaledTime + (matched.Count == 0 ? 0 : mappingInterval);
			KeyframeAdded.Invoke(keyframe);
			string summary = $"{matched.Count} trusted neighbors{lonely}, {extended} sightings extended tracks, {sighted} sighted established landmarks, {started} started, {dropped} rejected";
			Frames.Note(keyframe.frame, $"keyframe in slot {keyframe.slot}: {summary}");
			Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, this, "[{0}] keyframe {1}: {2}; {3}", nameof(VisualLocalizer), Map.Count, summary, Quality);
		}

		// each feature's ray against the environment mesh, where it is scanned
		private void MeshDepths(VisualKeyframe keyframe)
		{
			int n = keyframe.features.Count;
			int layer = LayerMask.NameToLayer(environmentLayer);
			if (layer < 0) return;
			keyframe.meshDepth = new float[n];

			NativeArray<RaycastCommand> commands = new(n, Allocator.TempJob);
			using NativeArray<RaycastHit> hits = new(n, Allocator.TempJob);
			QueryParameters query = new(1 << layer, false, QueryTriggerInteraction.Ignore, false);
			Vector3[] directions = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				directions[i] = keyframe.camera.rotation * (Vector3)keyframe.intrinsics.Ray(keyframe.features.keypoints[i]);
				commands[i] = new RaycastCommand(keyframe.camera.position, directions[i], query, maxRayDistance);
			}

			RaycastCommand.ScheduleBatch(commands, hits, 32, 1).Complete();
			commands.Dispose();

			for (int i = 0; i < n; i++)
			{
				RaycastHit hit = hits[i];
				if (hit.collider == null || Vector3.Dot(hit.normal, -directions[i]) < minHitCosine) continue;
				keyframe.meshDepth[i] = hit.distance;
			}
		}

		// ---- querying ----------------------------------------------------------

		/// <summary>
		/// Captures and describes the current frame for localization, here or on another
		/// headset. Null when no frame is available or the head is turning too fast.
		/// </summary>
		public async Awaitable<VisualQuery> CaptureQueryAsync()
		{
			if (capture == null || !await Acquire(Lock.Extractor)) return null;
			try
			{
				if (!capture.TryCapture(image, lastFrameTimestampNs, maxAngularSpeed)) return null;
				VisualQuery query = new()
				{
					intrinsics = image.intrinsics,
					camera = image.cameraInWorld,
					cameraInTrackingSpace = image.cameraInTrackingSpace,
					trackingSpace = image.trackingSpace,
					timestampNs = image.timestampNs,
				};

				if (Anchoring != null && Anchoring.TryAnchor(image.cameraInTrackingSpace, out ulong node, out Pose cameraInNode))
				{
					query.node = node;
					query.cameraInNode = cameraInNode;
				}

				VisualFrameRecord record = Frames.Begin("query", image, query.node, query.cameraInNode);
				query.frame = record?.sequence ?? 0;
				query.features = await extractor.ExtractAsync(image.pixels, image.content);
				if (record != null) record.features = query.features;
				return query.features == null ? null : query;
			}
			finally
			{
				extractorBusy = false;
			}
		}

		// what a query's features were matched to, before the solve
		private sealed class Matching
		{
			public readonly List<Correspondence> correspondences = new();
			public int keyframesMatched, bestMutual;
			public string failure;
		}

		/// <summary>
		/// Where the query camera is in this map's frame, as the gravity-aligned transform
		/// from the query's world (the pose in <see cref="VisualQuery.camera"/>) to the map's.
		/// A query with a camera prior (<see cref="LocalizeOptions.guided"/>) is matched by
		/// projection in a Burst job; one without runs LighterGlue against the views retrieval
		/// ranks as sharing the most with it.
		/// </summary>
		public async Awaitable<VisualLocalizationResult> LocalizeAsync(VisualQuery query, LocalizeOptions options = default)
		{
			VisualLocalizationResult result = default;
			if (capture == null || query?.features == null || Anchoring == null)
			{
				result.failure = "unavailable";
				return result;
			}

			// only the attention path takes the matcher: a guided query runs beside one
			bool guided = guidedMatching && options.guided && options.near.HasValue;
			if (!guided && !await Acquire(Lock.Matcher))
			{
				result.failure = "destroyed";
				return result;
			}

			try
			{
				IKeyframeAnchoring anchoring = Anchoring;
				Map.RefreshPlacement();

				Matching matching = guided ? await MatchGuided(query, options, anchoring) : await MatchAttention(query, options, anchoring);
				result.keyframesMatched = matching.keyframesMatched;
				result.bestMutual = matching.bestMutual;
				if (matching.failure != null)
				{
					result.failure = matching.failure;
					return result;
				}

				List<Correspondence> correspondences = Consistent(matching.correspondences, anchoring, out result.anchorNode);
				GravityPnP.Settings settings = pnp;
				if (options.centerPriorSigma > 0) settings.centerPriorSigma = options.centerPriorSigma;
				// RANSAC and Gauss-Newton over a few hundred correspondences: pure math, on a worker
				Pose camera = query.camera;
				PinholeCamera intrinsics = query.intrinsics;
				int seed = (int)(query.timestampNs & 0x7fffffff);
				await Awaitable.BackgroundThreadAsync();
				try
				{
					result.pose = GravityPnP.Solve(correspondences, intrinsics, camera.rotation, camera.position, settings, seed);
				}
				finally
				{
					await Awaitable.MainThreadAsync();
				}
				if (result.pose.success && result.pose.inlierIndices != null)
				{
					List<Vector3> points = new(result.pose.inlierIndices.Length);
					foreach (int i in result.pose.inlierIndices)
						if (correspondences[i].inverseDepth > 1f / 30f) points.Add(correspondences[i].Point);
					result.inlierPoints = points.ToArray();
				}

				Frames.Note(query.frame, $"localized{(guided ? " (guided)" : "")}{(options.untrustedFrom != 0 ? " (loop)" : "")}: {result} anchor={result.anchorNode:X16}");
				return result;
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
				result.failure = e.Message;
				return result;
			}
			finally
			{
				if (!guided) matcherBusy = false;
			}
		}

		/// <summary>
		/// LighterGlue against the views retrieval ranks as sharing the most features with the
		/// query: the whole view against the whole view, for a query with no prior to go by.
		/// </summary>
		private async Awaitable<Matching> MatchAttention(VisualQuery query, LocalizeOptions options, IKeyframeAnchoring anchoring)
		{
			Matching m = new();
			bool urgent = options.urgent || this.urgent;
			Vector3 queryForward = query.camera.forward;
			Func<VisualKeyframe, bool> filter = k =>
				(options.untrustedFrom == 0 || !Map.IsTrusted(k, options.untrustedFrom, trustDrift)) &&
				(options.maxViewAngle <= 0 || Vector3.Angle(k.camera.forward, queryForward) <= options.maxViewAngle);
			List<(VisualKeyframe keyframe, int mutual)> candidates = await Map.RetrieveAsync(query.features, keyframesPerQuery, minMutual, filter,
				options.near, options.nearDistance > 0 ? options.nearDistance : float.PositiveInfinity, candidatesPerFrame: RetrievalCandidatesPerFrame(urgent));
			if (!this)
			{
				m.failure = "destroyed";
				return m;
			}
			if (candidates.Count == 0)
			{
				m.failure = $"no keyframe shares {minMutual} features ({Quality})";
				return m;
			}

			m.bestMutual = candidates[0].mutual;
			if (options.minMutualToMatch > 0) candidates.RemoveAll(c => c.mutual < options.minMutualToMatch);
			if (candidates.Count == 0)
			{
				m.failure = $"no keyframe shares {options.minMutualToMatch} features (best {m.bestMutual})";
				return m;
			}

			// the strongest features come first, so a capped set keeps its indices and the
			// keyframe's landmark links stay valid
			FeatureSet queryFeatures = options.maxMatchFeatures > 0 ? query.features.Strongest(options.maxMatchFeatures) : query.features;

			// one correspondence per query feature and per landmark, the most confident
			Dictionary<int, int> byFeature = new(), byLandmark = new();
			foreach ((VisualKeyframe keyframe, int _) in candidates)
			{
				// only the keyframe's features that sight a usable landmark can become
				// correspondences: the rest would only take the matcher's time and slots
				List<int> sighting = new();
				for (int j = 0; j < keyframe.features.Count; j++)
				{
					if (options.maxMatchFeatures > 0 && sighting.Count >= options.maxMatchFeatures) break;
					if (Map.TryGetLandmark(keyframe.landmarks[j], out Landmark l) && l.IsUsable(singleViewMeshLandmarks)) sighting.Add(j);
				}
				if (sighting.Count < 6) continue;

				(int[] matches, float[] confidence) = await matcher.MatchAsync(queryFeatures, keyframe.features.Subset(sighting), MatcherFrames(urgent));
				if (matches == null || !this) continue;
				m.keyframesMatched++;

				for (int i = 0; i < matches.Length; i++)
				{
					if (matches[i] < 0) continue;
					int j = sighting[matches[i]];
					if (!Map.TryGetLandmark(keyframe.landmarks[j], out Landmark landmark) || !landmark.IsUsable(singleViewMeshLandmarks)) continue;
					if (!landmark.TryPlace(anchoring, out float3 origin, out float3 direction)) continue;

					float2 pixel = query.features.keypoints[i];
					Correspondence c = new()
					{
						pixel = pixel,
						ray = query.intrinsics.Ray(pixel),
						origin = origin,
						direction = direction,
						inverseDepth = landmark.inverseDepth,
						inverseDepthSigma = landmark.inverseDepthSigma,
						confidence = confidence[i],
						node = landmark.node,
					};

					int existing = byFeature.TryGetValue(i, out int f) ? f : byLandmark.TryGetValue(landmark.id, out int l) ? l : -1;
					if (existing >= 0)
					{
						if (m.correspondences[existing].confidence < c.confidence) m.correspondences[existing] = c;
						continue;
					}

					byFeature[i] = byLandmark[landmark.id] = m.correspondences.Count;
					m.correspondences.Add(c);
				}
			}

			if (m.keyframesMatched == 0) m.failure = $"the {candidates.Count} nearest views sight too few landmarks";
			return m;
		}

		// a landmark of a nearby view, projected through the prior: what a guided match looks for
		private struct Target
		{
			public VisualKeyframe keyframe;
			public int feature, landmark;
			public float2 pixel;
			public float window;
			public float3 origin, direction;
			public float inverseDepth, inverseDepthSigma;
			public ulong node;
		}

		/// <summary>
		/// The landmarks of the views that see most of what the prior sees, projected through
		/// it into the query image and matched within the window each may fall in
		/// (<see cref="GuidedMatcher"/>). No retrieval and no network: the prior says which
		/// views can share the scene, and projecting their landmarks says which do.
		/// </summary>
		private async Awaitable<Matching> MatchGuided(VisualQuery query, LocalizeOptions options, IKeyframeAnchoring anchoring)
		{
			Matching m = new();
			Pose prior = options.near.Value;
			float nearDistance = options.nearDistance > 0 ? options.nearDistance : float.PositiveInfinity;
			Vector3 forward = prior.forward;

			// the views nearest the prior and most alike in heading may have seen what it sees
			List<VisualKeyframe> near = new();
			foreach (VisualKeyframe k in Map.Keyframes)
			{
				if (Vector3.Distance(k.camera.position, prior.position) > nearDistance) continue;
				if (options.untrustedFrom != 0 && Map.IsTrusted(k, options.untrustedFrom, trustDrift)) continue;
				if (options.maxViewAngle > 0 && Vector3.Angle(k.camera.forward, forward) > options.maxViewAngle) continue;
				near.Add(k);
			}

			if (near.Count == 0)
			{
				m.failure = $"no view within {nearDistance:F0} m faces this way ({Quality})";
				return m;
			}

			float Likeness(VisualKeyframe k) => Vector3.Distance(prior.position, k.camera.position) + Vector3.Angle(forward, k.camera.forward) / 30f;
			near.Sort((a, b) => Likeness(a).CompareTo(Likeness(b)));
			if (near.Count > VisualMap.MaxRetrievalCandidates) near.RemoveRange(VisualMap.MaxRetrievalCandidates, near.Count - VisualMap.MaxRetrievalCandidates);

			// which of them did: every usable landmark each sights, where the prior projects it
			// into the image, with the window its true position may fall in: the base window for
			// the prior's yaw error and feature noise, plus how far the prior's position error
			// moves a point at its depth (nothing, at infinity), plus how far the landmark's own
			// depth uncertainty moves it, seen from this far off its reference view (parallax)
			quaternion inversePrior = math.conjugate(prior.rotation);
			float3 center = prior.position;
			float translation = options.guidedTranslation > 0 ? options.guidedTranslation : guidedTranslation;
			float maxWindow = XFeatExtractor.Width * 0.5f;
			List<(VisualKeyframe view, List<Target> inView)> views = new();
			foreach (VisualKeyframe k in near)
			{
				List<Target> inView = new();
				for (int j = 0; j < k.features.Count; j++)
				{
					int id = k.landmarks[j];
					if (id < 0 || !Map.TryGetLandmark(id, out Landmark l) || !l.IsUsable(singleViewMeshLandmarks)) continue;
					if (!l.TryPlace(anchoring, out float3 origin, out float3 direction)) continue;

					// the landmark's direction from the prior camera, homogeneous: exact for a
					// point, the bare direction at infinity; its depth is h.z / ρ
					float3 h = math.mul(inversePrior, l.inverseDepth * (origin - center) + direction);
					if (h.z <= 1e-4f * math.length(h) || !query.intrinsics.Project(h, out float2 pixel)) continue;
					float baseline = math.length(origin - center);
					float window = math.min(maxWindow, guidedWindowPixels +
						query.intrinsics.fx * (translation * l.inverseDepth + baseline * l.inverseDepthSigma) / h.z);
					if (pixel.x < -window || pixel.y < -window || pixel.x >= XFeatExtractor.Width + window || pixel.y >= XFeatExtractor.Height + window) continue;

					inView.Add(new Target
					{
						keyframe = k, feature = j, landmark = id, pixel = pixel, window = window,
						origin = origin, direction = direction,
						inverseDepth = l.inverseDepth, inverseDepthSigma = l.inverseDepthSigma, node = l.node,
					});
				}

				if (inView.Count > 0) views.Add((k, inView));
			}

			if (views.Count == 0)
			{
				m.failure = $"none of the {near.Count} nearest views' landmarks project into the view";
				return m;
			}

			// the views that see most of the scene; every landmark once, in their order
			views.Sort((a, b) => b.inView.Count.CompareTo(a.inView.Count));
			if (views.Count > guidedKeyframes) views.RemoveRange(guidedKeyframes, views.Count - guidedKeyframes);
			const int d = FeatureSet.DescriptorSize;
			List<Target> targets = new();
			List<float2> projected = new();
			List<float> windows = new();
			HashSet<int> seen = new();
			foreach ((VisualKeyframe _, List<Target> inView) in views)
				foreach (Target t in inView)
				{
					if (!seen.Add(t.landmark)) continue;
					targets.Add(t);
					projected.Add(t.pixel);
					windows.Add(t.window);
				}

			half[] descriptors = new half[targets.Count * d];
			for (int t = 0; t < targets.Count; t++)
				Array.Copy(targets[t].keyframe.features.descriptors, targets[t].feature * d, descriptors, t * d, d);

			(int[] matches, float[] confidence) = await GuidedMatcher.MatchAsync(query.features, projected.ToArray(), windows.ToArray(), descriptors, guidedMinCosine, guidedMinMargin);
			if (!this || matches == null)
			{
				m.failure = "destroyed";
				return m;
			}

			// a view sharing only a few matches shares them by chance: it contributes none
			Dictionary<VisualKeyframe, int> perView = new();
			for (int t = 0; t < targets.Count; t++)
			{
				if (matches[t] < 0) continue;
				perView.TryGetValue(targets[t].keyframe, out int n);
				perView[targets[t].keyframe] = n + 1;
			}

			foreach ((VisualKeyframe _, int n) in perView)
			{
				m.bestMutual = math.max(m.bestMutual, n);
				if (n >= guidedMinMatches) m.keyframesMatched++;
			}

			if (m.keyframesMatched == 0)
			{
				m.failure = $"the {views.Count} views seeing most of the scene share too few features (best {m.bestMutual} of {targets.Count} landmarks in view)";
				return m;
			}

			// mutual matches: one per query feature and per landmark already
			for (int t = 0; t < targets.Count; t++)
			{
				int i = matches[t];
				Target target = targets[t];
				if (i < 0 || perView[target.keyframe] < guidedMinMatches) continue;
				float2 pixel = query.features.keypoints[i];
				m.correspondences.Add(new Correspondence
				{
					pixel = pixel,
					ray = query.intrinsics.Ray(pixel),
					origin = target.origin,
					direction = target.direction,
					inverseDepth = target.inverseDepth,
					inverseDepthSigma = target.inverseDepthSigma,
					confidence = confidence[t],
					node = target.node,
				});
			}

			return m;
		}

		// ---- diagnosis ---------------------------------------------------------

		/// <summary>
		/// Saves the map and the frames behind it into <paramref name="folder"/>:
		/// settings.json (this component's settings), map.json (keyframes: frame, node,
		/// camera, intrinsics, per-feature landmark ids and mesh depths; landmarks: node,
		/// reference camera, bearing, inverse depth and its sigma, mesh prior, sightings),
		/// and the frame log (frames.json, frames/*.png, frames/*.features).
		/// </summary>
		public async Awaitable ExportAsync(string folder)
		{
			Directory.CreateDirectory(folder);
			string settings = JsonUtility.ToJson(this, true);

			Json j = new();
			j.BeginObject();
			VisualMapQuality q = Quality;
			j.Key("quality");
			j.BeginObject();
			j.Field("keyframes", q.keyframes);
			j.Field("landmarks", q.landmarks);
			j.Field("usable", q.usable);
			j.Field("ranged", q.ranged);
			j.EndObject();

			j.Key("keyframes");
			j.BeginArray();
			foreach (VisualKeyframe k in Map.Keyframes)
			{
				j.BeginObject();
				j.Field("frame", k.frame);
				j.Field("slot", k.slot);
				j.Field("node", k.node);
				j.Field("cameraInNode", k.cameraInNode);
				j.Field("camera", k.camera);
				j.Field("intrinsics", k.intrinsics);
				j.Field("timestampNs", k.timestampNs);
				j.Field("featureCount", k.features.Count);
				j.Key("landmarks");
				j.BeginArray();
				foreach (int id in k.landmarks) j.Value(id);
				j.EndArray();
				j.Key("meshDepth");
				j.BeginArray();
				if (k.meshDepth != null) foreach (float d in k.meshDepth) j.Value(d);
				j.EndArray();
				j.EndObject();
			}

			j.EndArray();

			j.Key("landmarks");
			j.BeginArray();
			foreach (Landmark l in Map.Landmarks)
			{
				j.BeginObject();
				j.Field("id", l.id);
				j.Field("node", l.node);
				j.Field("reference", l.reference);
				j.Field("bearing", l.bearing);
				j.Field("inverseDepth", l.inverseDepth);
				j.Field("inverseDepthSigma", l.inverseDepthSigma);
				j.Field("meshInverseDepth", l.meshInverseDepth);
				j.Field("meshInverseDepthSigma", l.meshInverseDepthSigma);
				j.Field("references", l.references);
				j.Key("observations");
				j.BeginArray();
				foreach (Observation o in l.observations)
				{
					j.BeginObject();
					j.Field("frame", o.keyframe?.frame ?? 0);
					j.Field("feature", o.feature);
					j.Field("node", o.node);
					j.Field("cameraInNode", o.cameraInNode);
					j.Field("ray", o.ray);
					j.Field("angularSigma", o.angularSigma);
					j.EndObject();
				}

				j.EndArray();
				j.EndObject();
			}

			j.EndArray();
			j.EndObject();
			string map = j.ToString();

			await Awaitable.BackgroundThreadAsync();
			try
			{
				File.WriteAllText(Path.Combine(folder, "settings.json"), settings);
				File.WriteAllText(Path.Combine(folder, "map.json"), map);
			}
			finally
			{
				await Awaitable.MainThreadAsync();
			}

			await Frames.ExportAsync(folder);
		}

		/// <summary>
		/// The correspondences whose landmarks hang together: anchored to the node most of
		/// them are, or to nodes within the trust window of it. Landmarks from either side
		/// of a loop not yet closed sit a drift apart; one rigid fit can match only one side.
		/// </summary>
		private List<Correspondence> Consistent(List<Correspondence> correspondences, IKeyframeAnchoring anchoring, out ulong anchor)
		{
			Dictionary<ulong, int> votes = new();
			foreach (Correspondence c in correspondences)
			{
				votes.TryGetValue(c.node, out int v);
				votes[c.node] = v + 1;
			}

			anchor = 0;
			int best = 0;
			foreach ((ulong node, int v) in votes)
				if (v > best)
				{
					best = v;
					anchor = node;
				}

			ulong a = anchor;
			return correspondences.FindAll(c => c.node == a || anchoring.Drift(a, c.node) <= trustDrift);
		}

		/// <summary>
		/// Whether a keyframe from another stretch of tracking (beyond the trust window of
		/// <paramref name="node"/>) lies within <paramref name="distance"/> of the camera: the
		/// only time a loop closure query can find anything.
		/// </summary>
		public bool HasUntrustedKeyframeNear(Vector3 camera, ulong node, float distance)
		{
			if (Anchoring == null) return false;
			Map.RefreshPlacement();
			foreach (VisualKeyframe k in Map.Keyframes)
				if (Vector3.Distance(k.camera.position, camera) <= distance && !Map.IsTrusted(k, node, trustDrift))
					return true;
			return false;
		}

		private static readonly ProfilerMarker SnapshotMarker = new("VisualLocalizer.NodePoseSnapshot");

		// one network at a time: mapping, capture and localization share the extractor's buffers
		private enum Lock { Extractor, Matcher }

		private async Awaitable<bool> Acquire(Lock which)
		{
			while (which == Lock.Extractor ? extractorBusy : matcherBusy)
			{
				await Awaitable.NextFrameAsync();
				if (!this) return false;
			}

			if (which == Lock.Extractor) extractorBusy = true;
			else matcherBusy = true;
			return true;
		}
	}
}
