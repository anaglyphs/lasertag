using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Visual relocalization and loop closure against a map of tracked landmarks.
	///
	/// Mapping is <see cref="VisualMapper"/>'s, and the camera and networks are
	/// <see cref="InferenceQueue"/>'s.
	///
	/// Localizing: a query frame retrieves the keyframes
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

		[SerializeField] private VisualMapper.Settings mapperSettings = new();
		[SerializeField] private InferenceQueue.Settings inferenceSettings = new();

		private const int EnoughCorrespondences = 50, MinSightingsWhenSearching = 20;

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
		[Tooltip("Milliseconds a frame may spend projecting the nearby views' landmarks through the prior before the rest waits for the next frame: tens of thousands of them, on this thread")]
		[SerializeField] private float guidedProjectionBudgetMs = 1.5f;

		[Header("Diagnosis")]
		[Tooltip("Keep the frames behind the map (and recent ones besides) for AlignmentDiagnosis to save. Editor and development builds only")]
		[SerializeField] private bool recordFrames = true;
		[Tooltip("Frames kept besides those of keyframes still in the map")]
		[SerializeField] private int recentFrames = 96;

		/// <inheritdoc cref="InferenceQueue.Urgent"/>
		public bool Urgent
		{
			get => queue.Urgent;
			set => queue.Urgent = value;
		}

		/// <summary>The pose graph keyframes are anchored to; no mapping without one.</summary>
		public IKeyframeAnchoring Anchoring
		{
			get => Map.Anchoring;
			set => Map.Anchoring = value;
		}

		public VisualMap Map { get; private set; }

		/// <summary>The frames behind the map and recent queries, with what became of each.</summary>
		public VisualFrameLog Frames { get; } = new();
		public bool IsAvailable => queue.IsAvailable;
		public float TrustDrift => mapperSettings.trustDrift;
		/// <summary>Feature localization noise, pixels: what a sighting's angular sigma is made of.</summary>
		public float PixelSigma => pnp.pixelSigma;

		/// <summary>What became of mapping attempts since the start (or since a test reset them).</summary>
		public VisualMapper.Counts Mapping
		{
			get => mapper.Mapping;
			set => mapper.Mapping = value;
		}

		public VisualMapQuality Quality => Map.Quality(pnp.rangedFraction);

		private InferenceQueue queue;
		private VisualMapper mapper;

		public event Action<VisualKeyframe> KeyframeAdded = delegate { };
		/// <summary>A new view with its features, before its tracks are built over the next frames and it joins the map: all a measurement of it needs.</summary>
		public event Action<VisualKeyframe> KeyframeExtracted = delegate { };
		internal void RaiseKeyframeExtracted(VisualKeyframe keyframe) => KeyframeExtracted.Invoke(keyframe);
		internal void RaiseKeyframeAdded(VisualKeyframe keyframe) => KeyframeAdded.Invoke(keyframe);
		internal int MinMutual => minMutual;

		private void Awake()
		{
			Instance = this;
			Map = new VisualMap(mapperSettings.mapCapacity)
			{
				coverageCell = mapperSettings.coverageCell, coverageHeadings = mapperSettings.coverageHeadings,
				freezeEstablished = FreezeOverride ?? mapperSettings.freezeEstablishedLandmarks,
			};
			queue = new InferenceQueue(this, inferenceSettings);
			mapper = new VisualMapper(this, mapperSettings, queue);
			// release builds keep no frames: every keyframe's image would stay on the managed heap
			Frames.enabled = recordFrames && Debug.isDebugBuild;
			Frames.capacity = recentFrames;
			Map.Evicted += k => Frames.Pin(k.frame, false);
			Map.Cleared += Frames.UnpinAll;
		}

		private void Start()
		{
			ARCameraManager cameraManager = FindFirstObjectByType<ARCameraManager>();
			if (cameraManager == null || MainXRRig.Instance == null)
			{
				Debug.Log($"[{nameof(VisualLocalizer)}] no AR camera: visual localization unavailable");
				enabled = false;
				return;
			}

			if (!queue.TryStart(cameraManager)) enabled = false;
		}

		private void OnDestroy()
		{
			queue?.Dispose();
			Map?.Dispose();
			if (Instance == this) Instance = null;
		}

		private void Update() => mapper.Tick();

		// ---- querying ----------------------------------------------------------

		/// <summary>
		/// Captures and describes the current frame for localization, here or on another
		/// headset. Null when no frame is available or the head is turning too fast.
		/// </summary>
		public async Awaitable<VisualQuery> CaptureQueryAsync()
		{
			if (!queue.IsAvailable || !await queue.AcquireExtractor()) return null;
			try
			{
				if (!queue.TryCapture(mapperSettings.maxAngularSpeed)) return null;
				CapturedImage image = queue.Image;
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
				query.features = await queue.ExtractAsync(image.pixels, image.content);
				if (record != null) record.features = query.features;
				return query.features == null ? null : query;
			}
			finally
			{
				queue.ReleaseExtractor();
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
		public Awaitable<VisualLocalizationResult> LocalizeAsync(VisualQuery query, LocalizeOptions options = default) =>
			LocalizeAsync(new[] { query }, options);

		/// <summary>
		/// <see cref="LocalizeAsync(VisualQuery, LocalizeOptions)"/> over several frames of one
		/// stretch of tracking, each with its camera in the same query world, the first the one
		/// the prior and the result's center describe: matched by projection through each
		/// camera and solved together, so the baseline between them pins translation as one
		/// view can't. Without a prior only the first frame is used.
		/// </summary>
		public async Awaitable<VisualLocalizationResult> LocalizeAsync(IReadOnlyList<VisualQuery> frames, LocalizeOptions options = default)
		{
			VisualLocalizationResult result = default;
			VisualQuery query = frames != null && frames.Count > 0 ? frames[0] : null;
			if (!queue.IsAvailable || query?.features == null || Anchoring == null)
			{
				result.failure = "unavailable";
				return result;
			}

			// only the attention path takes the matcher: a guided query runs beside one
			bool guided = guidedMatching && options.guided && options.near.HasValue;
			if (!guided && !await queue.AcquireMatcher())
			{
				result.failure = "destroyed";
				return result;
			}

			try
			{
				IKeyframeAnchoring anchoring = Anchoring;
				Map.RefreshPlacement();

				Matching matching;
				int cameraCount = 1;
				if (guided)
				{
					matching = new();
					HashSet<VisualKeyframe> contributing = new();
					for (int c = 0; c < frames.Count; c++)
					{
						VisualQuery frame = frames[c];
						if (frame?.features == null) continue;
						Matching one = await MatchGuided(frame, c == 0 ? options.near.Value : frame.camera, c, options, anchoring, contributing);
						if (!this)
						{
							result.failure = "destroyed";
							return result;
						}

						matching.correspondences.AddRange(one.correspondences);
						matching.bestMutual = math.max(matching.bestMutual, one.bestMutual);
						if (c == 0) matching.failure = one.failure;
						else Frames.Note(frame.frame, $"frame {c + 1} of {frames.Count} in one solve: {one.correspondences.Count} matches{(one.failure != null ? $" ({one.failure})" : "")}");
					}

					matching.keyframesMatched = contributing.Count;
					// the later frames can carry a solve the first found too little for
					if (matching.correspondences.Count >= 6) matching.failure = null;
					else matching.failure ??= "too few matches";
					cameraCount = frames.Count;
				}
				else matching = await MatchAttention(query, options, anchoring);

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
				Pose[] cameras = new Pose[cameraCount];
				for (int c = 0; c < cameraCount; c++) cameras[c] = frames[c]?.camera ?? query.camera;
				PinholeCamera intrinsics = query.intrinsics;
				int seed = (int)(query.timestampNs & 0x7fffffff);
				int views = matching.keyframesMatched;
				await Awaitable.BackgroundThreadAsync();
				try
				{
					result.pose = GravityPnP.Solve(correspondences, intrinsics, cameras, settings, seed);
					// plus the matched views' own placement error, which all their landmarks share
					GravityPnP.AddViewUncertainty(ref result.pose, settings, views);
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

				Frames.Note(query.frame, $"localized{(guided ? " (guided)" : "")}{(cameraCount > 1 ? $" ({cameraCount} frames)" : "")}{(options.untrustedFrom != 0 ? " (loop)" : "")}: {result} anchor={result.anchorNode:X16}");
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
				if (!guided) queue.ReleaseMatcher();
			}
		}

		/// <summary>
		/// LighterGlue against the views retrieval ranks as sharing the most features with the
		/// query: the whole view against the whole view, for a query with no prior to go by.
		/// </summary>
		private async Awaitable<Matching> MatchAttention(VisualQuery query, LocalizeOptions options, IKeyframeAnchoring anchoring)
		{
			Matching m = new();
			bool urgent = options.urgent || queue.Urgent;
			Vector3 queryForward = query.camera.forward;
			Func<VisualKeyframe, bool> filter = k =>
				(options.untrustedFrom == 0 || IsLoopTarget(k, options.untrustedFrom, options.minLoopWalkDrift)) &&
				(!options.lockedOnly || k.locked) &&
				(options.maxViewAngle <= 0 || Vector3.Angle(k.camera.forward, queryForward) <= options.maxViewAngle);
			List<(VisualKeyframe keyframe, int mutual)> candidates = await Map.RetrieveAsync(query.features, options.maxViews > 0 ? VisualMap.MaxRetrievalCandidates : keyframesPerQuery, minMutual, filter,
				options.near, options.nearDistance > 0 ? options.nearDistance : float.PositiveInfinity, candidatesPerFrame: queue.RetrievalCandidatesPerFrame(urgent));
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
				if (options.maxViews > 0 && (m.keyframesMatched >= options.maxViews || m.correspondences.Count >= EnoughCorrespondences)) break;

				// only the keyframe's features that sight a usable landmark can become
				// correspondences: the rest would only take the matcher's time and slots
				List<int> sighting = new();
				for (int j = 0; j < keyframe.features.Count; j++)
				{
					if (options.maxMatchFeatures > 0 && sighting.Count >= options.maxMatchFeatures) break;
					if (Map.TryGetLandmark(keyframe.landmarks[j], out Landmark l) && l.IsUsable) sighting.Add(j);
				}
				if (sighting.Count < (options.maxViews > 0 ? MinSightingsWhenSearching : 6)) continue;

				(int[] matches, float[] confidence) = await queue.MatchAsync(queryFeatures, keyframe.features.Subset(sighting), urgent);
				if (matches == null || !this) continue;
				m.keyframesMatched++;

				for (int i = 0; i < matches.Length; i++)
				{
					if (matches[i] < 0) continue;
					int j = sighting[matches[i]];
					if (!Map.TryGetLandmark(keyframe.landmarks[j], out Landmark landmark) || !landmark.IsUsable) continue;
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
		/// views can share the scene, and projecting their landmarks says which do. The
		/// correspondences name <paramref name="camera"/>, and the views that contributed join
		/// <paramref name="contributing"/>.
		/// </summary>
		private async Awaitable<Matching> MatchGuided(VisualQuery query, Pose prior, int camera, LocalizeOptions options, IKeyframeAnchoring anchoring,
			HashSet<VisualKeyframe> contributing)
		{
			Matching m = new();
			float nearDistance = options.nearDistance > 0 ? options.nearDistance : float.PositiveInfinity;
			Vector3 forward = prior.forward;

			// the views nearest the prior and most alike in heading may have seen what it sees
			List<VisualKeyframe> near = new();
			foreach (VisualKeyframe k in Map.Keyframes)
			{
				if (Vector3.Distance(k.camera.position, prior.position) > nearDistance) continue;
				if (options.untrustedFrom != 0 && !IsLoopTarget(k, options.untrustedFrom, options.minLoopWalkDrift)) continue;
				if (options.lockedOnly && !k.locked) continue;
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
			System.Diagnostics.Stopwatch slice = System.Diagnostics.Stopwatch.StartNew();
			foreach (VisualKeyframe k in near)
			{
				// spread over frames past the budget: what the views sight is read afresh by id,
				// so a landmark released meanwhile is only skipped
				if (slice.Elapsed.TotalMilliseconds > guidedProjectionBudgetMs)
				{
					await Awaitable.NextFrameAsync();
					if (!this)
					{
						m.failure = "destroyed";
						return m;
					}

					slice.Restart();
				}

				List<Target> inView = new();
				for (int j = 0; j < k.features.Count; j++)
				{
					int id = k.landmarks[j];
					if (id < 0 || !Map.TryGetLandmark(id, out Landmark l) || !l.IsUsable) continue;
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

			foreach ((VisualKeyframe view, int n) in perView)
			{
				m.bestMutual = math.max(m.bestMutual, n);
				if (n < guidedMinMatches) continue;
				m.keyframesMatched++;
				contributing?.Add(view);
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
					camera = camera,
				});
			}

			return m;
		}

		/// <inheritdoc cref="VisualMapExport.ExportAsync"/>
		public Awaitable ExportAsync(string folder) => VisualMapExport.ExportAsync(Map, Quality, Frames, JsonUtility.ToJson(this, true), folder);

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
			return correspondences.FindAll(c => c.node == a || anchoring.Drift(a, c.node) <= mapperSettings.trustDrift);
		}

		/// <summary>
		/// Whether a keyframe from another stretch of tracking (beyond the trust window of
		/// <paramref name="node"/>) lies within <paramref name="distance"/> of the camera: the
		/// only time a loop closure query can find anything. With <paramref name="minWalkDrift"/>,
		/// as <see cref="LocalizeOptions.minLoopWalkDrift"/>.
		/// </summary>
		public bool HasUntrustedKeyframeNear(Vector3 camera, ulong node, float distance, float minWalkDrift = 0, bool lockedOnly = false)
		{
			if (Anchoring == null) return false;
			Map.RefreshPlacement();
			foreach (VisualKeyframe k in Map.Keyframes)
				if ((!lockedOnly || k.locked) && Vector3.Distance(k.camera.position, camera) <= distance && IsLoopTarget(k, node, minWalkDrift))
					return true;
			return false;
		}

		// another stretch of tracking: beyond the trust window, and walked away from rather
		// than only stepped away from by a correction
		private bool IsLoopTarget(VisualKeyframe k, ulong node, float minWalkDrift) =>
			Anchoring != null && !Map.IsTrusted(k, node, mapperSettings.trustDrift) && (minWalkDrift <= 0 || Anchoring.WalkDrift(node, k.node) >= minWalkDrift);

	}
}
