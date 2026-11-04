using System;
using System.Collections.Generic;
using Anaglyph.Debugging;
using Anaglyph.XR.VisualLocalization;
using UnityEngine;
using Utilities.XR;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// What visual alignment is working from, drawn in debug mode. White rays from the newest
	/// view through its features. Dots: yellow for locked keyframes' landmarks around the head,
	/// green for those a new view has just tracked, cyan for those of any view it has just
	/// aligned to. Lines: white for the graph's knots and the walk
	/// between them, green for matches between knots.
	/// </summary>
	public sealed class VisualAlignmentDebugView : IDisposable
	{
		private const float RefreshSeconds = 0.25f, LockedRadius = 6f, MatchedSeconds = 2f;
		private const float DotSize = 0.02f, KnotAxisLength = 0.2f, FeatureRayLength = 3f;
		// all the rays meet at the camera, where they'd cover the wearer's eye
		private const float FeatureRayStart = 0.3f;

		private readonly VisualLocalizer localizer;
		private readonly KeyframeGraph graph;
		private readonly Dictionary<int, float> matchedAt = new(), alignedAt = new();
		private readonly HashSet<int> placedLocked = new();
		private readonly List<int> expired = new();
		private readonly List<Vector3> lockedDots = new(), matchedDots = new(), alignedDots = new();
		private readonly List<Vector3> featureRays = new(), walkLines = new(), matchLines = new();
		private VisualKeyframe newest;
		private float nextRefresh;

		public VisualAlignmentDebugView(VisualLocalizer localizer, KeyframeGraph graph)
		{
			this.localizer = localizer;
			this.graph = graph;
			localizer.KeyframeExtracted += OnKeyframeExtracted;
			localizer.KeyframeAdded += OnKeyframeAdded;
			localizer.Map.Cleared += OnMapCleared;
			graph.LandmarkMatched += OnLandmarkAligned;
		}

		public void Dispose()
		{
			graph.LandmarkMatched -= OnLandmarkAligned;
			if (!localizer) return;
			localizer.KeyframeExtracted -= OnKeyframeExtracted;
			localizer.KeyframeAdded -= OnKeyframeAdded;
			localizer.Map.Cleared -= OnMapCleared;
		}

		public void Draw(Vector3 head)
		{
			if (Time.unscaledTime >= nextRefresh)
			{
				nextRefresh = Time.unscaledTime + RefreshSeconds;
				Refresh(head);
			}

			XRGizmos.DrawLineSegments(walkLines, Color.white);
			XRGizmos.DrawLineSegments(matchLines, Color.green);
			XRGizmos.DrawLineSegments(featureRays, Color.white);
			XRGizmos.DrawPointSet(lockedDots, Color.yellow, DotSize);
			XRGizmos.DrawPointSet(matchedDots, Color.green, DotSize);
			XRGizmos.DrawPointSet(alignedDots, Color.cyan, DotSize);
		}

		private void OnKeyframeExtracted(VisualKeyframe keyframe) => newest = keyframe;

		private void OnKeyframeAdded(VisualKeyframe keyframe)
		{
			if (!AnaglyphDebugging.DebugMode) return;
			foreach (int landmark in keyframe.landmarks)
				if (landmark >= 0) OnLandmarkMatched(landmark);
		}

		private void OnLandmarkMatched(int landmark) => matchedAt[landmark] = Time.unscaledTime;

		private void OnLandmarkAligned(int landmark) => alignedAt[landmark] = Time.unscaledTime;

		private void OnMapCleared()
		{
			newest = null;
			matchedAt.Clear();
			alignedAt.Clear();
		}

		private void Refresh(Vector3 head)
		{
			walkLines.Clear();
			matchLines.Clear();
			graph.AddDebugLines(walkLines, matchLines, KnotAxisLength);

			ForgetExpired(matchedAt);
			ForgetExpired(alignedAt);
			RefreshLocked(head);
			RefreshAligned();
			// RefreshNewest();
		}

		private void ForgetExpired(Dictionary<int, float> landmarkTimes)
		{
			expired.Clear();
			foreach ((int landmark, float at) in landmarkTimes)
				if (Time.unscaledTime - at > MatchedSeconds) expired.Add(landmark);
			foreach (int landmark in expired) landmarkTimes.Remove(landmark);
		}

		private void RefreshAligned()
		{
			alignedDots.Clear();
			VisualMap map = localizer.Map;
			foreach ((int id, float _) in alignedAt)
				if (map.TryGetLandmark(id, out Landmark landmark) && TryPlace(landmark, out Vector3 point))
					alignedDots.Add(point);
		}

		private void RefreshLocked(Vector3 head)
		{
			lockedDots.Clear();
			matchedDots.Clear();
			placedLocked.Clear();
			VisualMap map = localizer.Map;
			foreach (VisualKeyframe keyframe in map.Keyframes)
			{
				if (!keyframe.locked || Vector3.Distance(keyframe.camera.position, head) > LockedRadius) continue;
				foreach (int id in keyframe.landmarks)
				{
					if (!map.TryGetLandmark(id, out Landmark landmark) || !graph.IsRanged(landmark) || !TryPlace(landmark, out Vector3 point) || !placedLocked.Add(id)) continue;
					if (!alignedAt.ContainsKey(id)) (matchedAt.ContainsKey(id) ? matchedDots : lockedDots).Add(point);
				}
			}
		}

		private bool TryPlace(Landmark landmark, out Vector3 point)
		{
			point = default;
			if (!landmark.TryPlace(localizer.Anchoring, out var origin, out var direction)) return false;
			point = origin + direction / landmark.inverseDepth;
			return true;
		}
	}
}
