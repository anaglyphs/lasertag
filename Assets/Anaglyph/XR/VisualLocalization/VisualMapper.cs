using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// Whenever the pose graph tracks the headset, new views of the passthrough camera become
	/// keyframes anchored to the current node. Each is matched to the keyframes near it whose
	/// nodes are within the drift window, consecutive views of one stretch of tracking, and the
	/// matches extend landmark tracks, whose inverse depth is triangulated from the sightings.
	/// Views from across a longer walk are never triangulated together: that is what a loop
	/// closure is for.
	/// </summary>
	public sealed class VisualMapper
	{
		[Serializable]
		public sealed class Settings
		{
			[Header("Mapping")]
			[Tooltip("Seconds after a keyframe before the next is tried")]
			public float mappingInterval = 0.5f;
			[Tooltip("Seconds before trying again after a frame that couldn't be a keyframe (blurred, or nothing new): soon, so the calm moments of a moving head are caught")]
			public float mappingRetry = 0f;
			[Tooltip("A head turning slower than this (°/s) is still enough to capture at once; between it and the blur limit, capture waits while the turn is still slowing quickly, for the stillest frame of the settle")]
			public float calmAngularSpeed = 15f;
			[Tooltip("Seconds before looking again when the view is nothing new: standing still, no image is converted every frame only to be dropped")]
			public float notNovelRetry = 0.1f;
			public float keyframeMinDistance = 0.5f;
			public float keyframeMinAngle = 20f;
			[Tooltip("Head rotation above this (°/s) blurs the passthrough image too much to map or query")]
			public float maxAngularSpeed = 40f;
			public int minKeyframeFeatures = 100;
			public int mapCapacity = 256;
			[Tooltip("When the map is full, keyframes are given up from the most crowded bucket of place and heading, so every place and direction keeps a view. Cell side at the finest level, meters")]
			public float coverageCell = 1f;
			[Tooltip("A view is locked, a reference revisits measure against, while no view walked away from has seen within this far of it, meters...")]
			public float lockRadius = 4f;
			[Tooltip("...turned less than this, degrees")]
			public float lockAngle = 60f;
			[Tooltip("...where walked away from is this much expected drift, meters")]
			public float lockMinWalkDrift = 0.034f;
			[Tooltip("Heading bins at the finest level; each coarsening halves them and doubles the cell")]
			public int coverageHeadings = 8;

			[Header("Tracks")]
			[Tooltip("A landmark triangulated from the views that founded it is fixed from then on: later views sight it but never refit it")]
			public bool freezeEstablishedLandmarks = true;
			[Tooltip("A landmark is established once its depth sigma is within this fraction of its depth...")]
			public float establishedDepthSigma = 0.1f;
			[Tooltip("...or once this many views have sighted it, whichever comes first")]
			public int establishedViews = 6;
			[Tooltip("Views a landmark must have survived before it's established: two always agree along their epipolar line, so a mismatched track takes a third view to show")]
			public int establishedMinViews = 3;
			[Tooltip("Expected drift (m) between two keyframes' nodes up to which their views are triangulated together; beyond it they can only be loop-closed")]
			public float trustDrift = 0.031f;
			public int trackNeighbors = 3;
			public float neighborDistance = 4f;
			[Tooltip("Lowest descriptor cosine a mutual nearest neighbor match may have")]
			public float trackMinCosine = 0.82f;
		}

		/// <summary>What became of mapping attempts since the start (or since a test reset them).</summary>
		public struct Counts
		{
			public int attempts, noFrame, blurred, notNovel, fewFeatures, stale, added;
			public float extractSeconds, busySeconds;
			public override string ToString() =>
				$"{added} keyframes of {attempts} attempts: {blurred} blurred, {notNovel} nothing new, {noFrame} no frame, {fewFeatures} too few features, {stale} map changed; {(added > 0 ? extractSeconds / added : 0):F2} s extracting and {(added > 0 ? busySeconds / added : 0):F2} s in all per keyframe";
		}

		public Counts Mapping;

		private readonly VisualLocalizer owner;
		private readonly Settings settings;
		private readonly InferenceQueue queue;
		private float nextMapping;
		// one keyframe at a time from capture until it is in the map. The extractor is let go
		// once its features are out: matching them into the map takes up to a second more, which
		// a query's capture shouldn't wait behind
		private bool mappingBusy;

		// the head's turn rate, frame to frame, for priming captures on the moment it settles
		private Quaternion lastHeadRotation;
		private bool hasHeadRotation;
		private float turnRate, previousTurnRate;

		private static readonly ProfilerMarker SnapshotMarker = new("VisualLocalizer.NodePoseSnapshot");

		public VisualMapper(VisualLocalizer owner, Settings settings, InferenceQueue queue)
		{
			this.owner = owner;
			this.settings = settings;
			this.queue = queue;
		}

		private VisualMap Map => owner.Map;
		private VisualFrameLog Frames => owner.Frames;
		private IKeyframeAnchoring Anchoring => owner.Anchoring;

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
			return Map.IsNovel(camera, node, settings.trustDrift, settings.keyframeMinDistance, settings.keyframeMinAngle);
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

		public void Tick()
		{
			TrackTurnRate();
			if (!queue.IsAvailable || Anchoring == null || queue.ExtractorBusy || mappingBusy || Time.unscaledTime < nextMapping) return;

			// primed, not polled: a frame is taken the moment the head settles under the blur
			// limit, every frame it stays there; while a turn is still slowing quickly, the next
			// frame is the sharper one
			if (turnRate > settings.maxAngularSpeed) return;
			if (turnRate > settings.calmAngularSpeed && turnRate < previousTurnRate * 0.8f) return;

			// nothing new in view (standing, or retracing a step): decided from the head pose,
			// before an image is acquired and converted on this thread
			if (!LooksNovel())
			{
				nextMapping = Time.unscaledTime + settings.notNovelRetry;
				return;
			}

			nextMapping = Time.unscaledTime + settings.mappingRetry;
			TryAddKeyframe();
		}

		private async void TryAddKeyframe()
		{
			float attemptStart = Time.realtimeSinceStartup;
			if (!await queue.AcquireExtractor()) return;
			CapturedImage image = queue.Image;
			mappingBusy = true;
			bool extracting = true;
			try
			{
				Mapping.attempts++;
				if (!queue.TryCapture(settings.maxAngularSpeed))
				{
					if (image.angularSpeed > settings.maxAngularSpeed) Mapping.blurred++;
					else Mapping.noFrame++;
					return;
				}

				// anchored at capture: the node the head was at then, whatever happens while extracting
				IKeyframeAnchoring anchoring = Anchoring;
				if (anchoring == null || !anchoring.TryAnchor(image.cameraInTrackingSpace, out ulong node, out Pose cameraInNode)) return;
				if (!Landmark.TryCamera(anchoring, node, cameraInNode, out Pose camera)) return;

				Map.RefreshPlacement();
				if (!Map.IsNovel(camera, node, settings.trustDrift, settings.keyframeMinDistance, settings.keyframeMinAngle))
				{
					Mapping.notNovel++;
					nextMapping = Time.unscaledTime + settings.notNovelRetry;
					return;
				}

				// the first view of a place, as opposed to a revisit's (novel only because the views
				// already there are from another stretch of tracking): kept for good
				bool original = Map.IsNovel(camera, node, float.PositiveInfinity, settings.keyframeMinDistance, settings.keyframeMinAngle);

				bool locked = Map.IsFirstView(camera, node, settings.lockMinWalkDrift, settings.lockRadius, settings.lockAngle);

				// everything about the frame is read now: the image buffer is reused
				PinholeCamera intrinsics = image.intrinsics;
				long timestampNs = image.timestampNs;
				int mapRevision = Map.Revision;
				VisualFrameRecord record = Frames.Begin("map", image, node, cameraInNode);
				int frame = record?.sequence ?? 0;

				float extractStart = Time.realtimeSinceStartup;
				FeatureSet features = await queue.ExtractAsync(image.pixels, image.content);
				Mapping.extractSeconds += Time.realtimeSinceStartup - extractStart;
				queue.ReleaseExtractor();
				extracting = false;
				if (record != null) record.features = features;
				if (features == null || !owner) return;
				if (features.Count < settings.minKeyframeFeatures)
				{
					Mapping.fewFeatures++;
					Frames.Note(frame, $"not a keyframe: {features.Count} features < {settings.minKeyframeFeatures}");
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
					locked = locked,
					owner = Map.LocalOwner,
				};
				Array.Fill(keyframe.landmarks, -1);

				// the node may have moved in a solve meanwhile
				if (!Landmark.TryCamera(anchoring, node, cameraInNode, out keyframe.camera)) return;

				owner.RaiseKeyframeExtracted(keyframe);
				await ExtendTracks(keyframe, mapRevision);
			}
			catch (Exception e)
			{
				Debug.LogException(e, owner);
			}
			finally
			{
				if (extracting) queue.ReleaseExtractor();
				mappingBusy = false;
				Mapping.busySeconds += Time.realtimeSinceStartup - attemptStart;
			}
		}

		/// <summary>
		/// Matches the new keyframe against trusted neighbors: a match to a feature that
		/// already sights a landmark extends its track, a match to one that doesn't starts a
		/// track there. Then every touched landmark is re-triangulated.
		/// </summary>
		private async Awaitable ExtendTracks(VisualKeyframe keyframe, int mapRevision)
		{
			List<(VisualKeyframe keyframe, int mutual)> neighbors = await Map.RetrieveAsync(keyframe.features, settings.trackNeighbors, owner.MinMutual,
				k => Map.IsTrusted(k, keyframe.node, settings.trustDrift), keyframe.camera, settings.neighborDistance, candidatesPerFrame: queue.RetrievalCandidatesPerFrame(queue.Urgent));
			if (!owner || Map.Revision != mapRevision) return;

			// why no neighbor: none near, or none near and trusted (a glance's view shares too little with them)
			string lonely = null;
			if (neighbors.Count == 0)
			{
				int near = 0, trusted = 0;
				foreach (VisualKeyframe k in Map.Keyframes)
				{
					if (k == keyframe || Vector3.Distance(k.camera.position, keyframe.camera.position) > settings.neighborDistance) continue;
					near++;
					if (Map.IsTrusted(k, keyframe.node, settings.trustDrift)) trusted++;
				}

				lonely = $" (alone: {near} near, {trusted} trusted)";
			}

			List<(VisualKeyframe neighbor, int[] matches)> matched = new();
			foreach ((VisualKeyframe neighbor, int _) in neighbors)
			{
				(int[] matches, _) = await MutualNearestMatcher.MatchAsync(keyframe.features, neighbor.features, settings.trackMinCosine);
				if (!owner || Map.Revision != mapRevision) return;
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
						landmark = Map.CreateLandmark(neighbor, j, owner.PixelSigma);
						started++;
					}
					else if (landmark.established) sighted++;
					else extended++;

					if (Map.Link(keyframe, i, landmark, owner.PixelSigma)) touched.Add(landmark);
				}
			}

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

			if (!owner) return;
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
				if (l.observations.Count >= settings.establishedMinViews && float.IsFinite(l.inverseDepthSigma) &&
				    (l.inverseDepthSigma <= settings.establishedDepthSigma * l.inverseDepth || l.observations.Count >= settings.establishedViews))
					l.established = true;

			Mapping.added++;
			// a view no other keyframe shares (a glance, mostly) triangulates nothing until a
			// second view of it comes: take the next sharp frame at once, while it's still in sight
			nextMapping = Time.unscaledTime + (matched.Count == 0 ? 0 : settings.mappingInterval);
			owner.RaiseKeyframeAdded(keyframe);
			string summary = $"{matched.Count} trusted neighbors{lonely}, {extended} sightings extended tracks, {sighted} sighted established landmarks, {started} started, {dropped} rejected";
			Frames.Note(keyframe.frame, $"keyframe in slot {keyframe.slot}: {summary}");
			Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, owner, "[{0}] keyframe {1}: {2}; {3}", nameof(VisualLocalizer), Map.Count, summary, owner.Quality);
		}
	}
}
