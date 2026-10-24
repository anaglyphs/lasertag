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
	}

	public struct VisualLocalizationResult
	{
		public GravityPnPResult pose;
		public int keyframesMatched;
		public int bestMutual;
		/// <summary>The node the matched landmarks are anchored to (those of other nodes too far from it in drift are left out).</summary>
		public ulong anchorNode;
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

		[Header("Localization")]
		[SerializeField] private int keyframesPerQuery = 2;
		[SerializeField] private int minMutual = 12;
		[SerializeField] private GravityPnP.Settings pnp = GravityPnP.Settings.Default;

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
				int frames = value ? urgentFramesPerRun : framesPerRun;
				if (extractor != null) extractor.FramesPerRun = frames;
				if (matcher != null) matcher.FramesPerRun = value ? urgentFramesPerRun : matcherFramesPerRun;
			}
		}

		private bool urgent;

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
			Map = new VisualMap(mapCapacity) { coverageCell = coverageCell, coverageHeadings = coverageHeadings };
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

				// everything about the frame is read now: the image buffer is reused
				PinholeCamera intrinsics = image.intrinsics;
				long timestampNs = image.timestampNs;
				int mapRevision = Map.Revision;
				VisualFrameRecord record = Frames.Begin("map", image, node, cameraInNode);
				int frame = record?.sequence ?? 0;

				FeatureSet features = await extractor.ExtractAsync(image.pixels);
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
				k => Map.IsTrusted(k, keyframe.node, trustDrift), keyframe.camera, neighborDistance);
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
					try { (matches, _) = await matcher.MatchAsync(keyframe.features, neighbor.features); }
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
			int extended = 0, started = 0;
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

			Mapping.added++;
			// a view no other keyframe shares (a glance, mostly) triangulates nothing until a
			// second view of it comes: take the next sharp frame at once, while it's still in sight
			nextMapping = Time.unscaledTime + (matched.Count == 0 ? 0 : mappingInterval);
			KeyframeAdded.Invoke(keyframe);
			string summary = $"{matched.Count} trusted neighbors{lonely}, {extended} sightings extended tracks, {started} started, {dropped} rejected";
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
				query.features = await extractor.ExtractAsync(image.pixels);
				if (record != null) record.features = query.features;
				return query.features == null ? null : query;
			}
			finally
			{
				extractorBusy = false;
			}
		}

		/// <summary>
		/// Where the query camera is in this map's frame, as the gravity-aligned transform
		/// from the query's world (the pose in <see cref="VisualQuery.camera"/>) to the map's.
		/// </summary>
		public async Awaitable<VisualLocalizationResult> LocalizeAsync(VisualQuery query, LocalizeOptions options = default)
		{
			VisualLocalizationResult result = default;
			if (capture == null || query?.features == null || Anchoring == null)
			{
				result.failure = "unavailable";
				return result;
			}

			if (!await Acquire(Lock.Matcher))
			{
				result.failure = "destroyed";
				return result;
			}

			try
			{
				IKeyframeAnchoring anchoring = Anchoring;
				Map.RefreshPlacement();

				Vector3 queryForward = query.camera.forward;
				Func<VisualKeyframe, bool> filter = k =>
					(options.untrustedFrom == 0 || !Map.IsTrusted(k, options.untrustedFrom, trustDrift)) &&
					(options.maxViewAngle <= 0 || Vector3.Angle(k.camera.forward, queryForward) <= options.maxViewAngle);
				List<(VisualKeyframe keyframe, int mutual)> candidates = await Map.RetrieveAsync(query.features, keyframesPerQuery, minMutual, filter,
					options.near, options.nearDistance > 0 ? options.nearDistance : float.PositiveInfinity);
				if (!this)
				{
					result.failure = "destroyed";
					return result;
				}
				if (candidates.Count == 0)
				{
					result.failure = $"no keyframe shares {minMutual} features ({Quality})";
					return result;
				}

				result.bestMutual = candidates[0].mutual;
				if (options.minMutualToMatch > 0) candidates.RemoveAll(c => c.mutual < options.minMutualToMatch);
				if (candidates.Count == 0)
				{
					result.failure = $"no keyframe shares {options.minMutualToMatch} features (best {result.bestMutual})";
					return result;
				}

				// the strongest features come first, so a capped set keeps its indices and the
				// keyframe's landmark links stay valid
				FeatureSet queryFeatures = options.maxMatchFeatures > 0 ? query.features.Strongest(options.maxMatchFeatures) : query.features;

				// one correspondence per query feature and per landmark, the most confident
				List<Correspondence> correspondences = new();
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

					(int[] matches, float[] confidence) = await matcher.MatchAsync(queryFeatures, keyframe.features.Subset(sighting));
					if (matches == null || !this) continue;
					result.keyframesMatched++;

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
							if (correspondences[existing].confidence < c.confidence) correspondences[existing] = c;
							continue;
						}

						byFeature[i] = byLandmark[landmark.id] = correspondences.Count;
						correspondences.Add(c);
					}
				}

				if (result.keyframesMatched == 0)
				{
					result.failure = $"the {candidates.Count} nearest views sight too few landmarks";
					return result;
				}

				correspondences = Consistent(correspondences, anchoring, out result.anchorNode);
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
				Frames.Note(query.frame, $"localized{(options.untrustedFrom != 0 ? " (loop)" : "")}: {result} anchor={result.anchorNode:X16}");
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
				matcherBusy = false;
			}
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
