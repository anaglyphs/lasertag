using System;
using System.Collections.Generic;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>An immutable set of targets with independently retained, live observations. Never mints references.</summary>
	internal sealed class MapSpaceReferenceObservation : IColocationConstraintProvider, IDisposable
	{
		private readonly struct AnchorTarget
		{
			public readonly Guid id;
			public readonly AnchorLease lease;
			public readonly Pose target;
			public readonly int tagId;
			public AnchorTarget(Guid id, AnchorLease lease, Pose target, int tagId) { this.id = id; this.lease = lease; this.target = target; this.tagId = tagId; }
		}
		private readonly MapSpace space;
		private readonly ColocationManager.ColocationMethod method;
		private readonly AnchorRegistry registry;
		private readonly AprilTagColocationConstraintProvider tagProvider;
		private readonly List<AnchorTarget> anchors = new();
		private readonly Dictionary<int, (Pose pose, float time)> seen = new();
		private readonly HashSet<int> observedTags = new();
		private readonly Dictionary<int, int> observationCounts = new();
		private readonly HashSet<int> confirmedTags = new();
		public bool HasRepeatedObservation(int id)
		{
			CheckTrackingGeneration();
			return confirmedTags.Contains(id);
		}
		public void ResetObservations() { seen.Clear(); observationCounts.Clear(); confirmedTags.Clear(); }
		public void RetainObservationsFrom(MapSpaceReferenceObservation previous)
		{
			if (previous == null || previous.method != method || previous.space.canonicalFrameId != space.canonicalFrameId ||
				Mathf.Abs(previous.space.tagSizeCm - space.tagSizeCm) > .001f) return;
			CheckTrackingGeneration(); previous.CheckTrackingGeneration();
			foreach (var tag in space.tags)
				if (previous.space.TryGetTag(tag.id, out var old) &&
					MapSpaceFrame.Near(space.Frame.ToCanonical(tag.canonPose), previous.space.Frame.ToCanonical(old.canonPose), .001f, .1f))
				{
					if (previous.seen.TryGetValue(tag.id, out var observation)) seen[tag.id] = observation;
					if (previous.observationCounts.TryGetValue(tag.id, out int count)) observationCounts[tag.id] = count;
					if (previous.confirmedTags.Contains(tag.id)) confirmedTags.Add(tag.id);
				}
		}
		private readonly HashSet<Guid> acquired = new();
		private readonly List<TaggedAnchorConstraintData> realized = new();
		private int observers;
		private int trackingGeneration = -1;
		private void CheckTrackingGeneration()
		{
			int current = ColocationManager.Instance != null ? ColocationManager.Instance.TrackingGeneration : 0;
			if (current == trackingGeneration) return;
			trackingGeneration = current; ResetObservations();
		}
		private bool solver;
		private bool disposed;
		public bool IsAvailable => registry != null && registry.IsAvailable;
		public bool IsRunning { get; private set; }
		public MapSpaceReferenceObservation(MapSpace space, ColocationManager.ColocationMethod method,
			AnchorRegistry registry, AprilTagColocationConstraintProvider tags)
		{ this.space = space.Clone(); this.method = method; this.registry = registry; tagProvider = tags; }
		public IDisposable Observe()
		{
			observers++; UpdateRunning(); return new ObservationLease(this);
		}
		private sealed class ObservationLease : IDisposable
		{
			private MapSpaceReferenceObservation owner;
			public ObservationLease(MapSpaceReferenceObservation owner) => this.owner = owner;
			public void Dispose() { if (owner == null) return; owner.observers--; owner.UpdateRunning(); owner = null; }
		}
		public void StartProviding() { solver = true; UpdateRunning(); }
		public void StopProviding() { solver = false; UpdateRunning(); }
		private void UpdateRunning()
		{
			bool run = !disposed && (solver || observers > 0);
			if (run == IsRunning) return;
			IsRunning = run;
			if (run)
			{
				AcquireSavedAnchors();
				if (method == ColocationManager.ColocationMethod.AprilTag && tagProvider != null)
				{ tagProvider.TagObserved += OnTag; tagProvider.SetDetectionRequest(this, true); }
			}
			else
			{
				if (tagProvider != null) { tagProvider.TagObserved -= OnTag; tagProvider.SetDetectionRequest(this, false); }
				foreach (var anchor in anchors) anchor.lease?.Dispose();
				anchors.Clear(); acquired.Clear(); ResetObservations();
			}
		}
		private void AcquireSavedAnchors()
		{
			if (!IsAvailable) return;
			IEnumerable<MapAnchorEntry> source = method == ColocationManager.ColocationMethod.AprilTag ? space.localAnchors : space.anchors;
			foreach (var entry in source)
			{
				if (method == ColocationManager.ColocationMethod.AprilTag && !space.IsCompatibleLocalAnchor(entry)) continue;
				if (Guid.TryParse(entry.guid, out Guid guid) && !acquired.Contains(guid))
				{
					var lease = registry.Acquire(new SerializableGuid(guid), AnchorSource.Any);
					anchors.Add(new(guid, lease, space.Frame.ToCanonical(entry.canonPose), entry.tagId)); acquired.Add(guid);
				}
			}
		}
		private void OnTag(int id, Pose pose)
		{
			CheckTrackingGeneration();
			if (!MainXRRig.Instance || !space.TryGetTag(id, out var tag) || Mathf.Abs(tagProvider.TagSizeCm - space.tagSizeCm) > .001f) return;
			Pose rig = new(MainXRRig.TrackingSpace.position, MainXRRig.TrackingSpace.rotation);
			observationCounts.TryGetValue(id, out int count);
			// Detector throughput and looking between tags must not discard good evidence.
			// Contradictory samples, changed targets and tracking loss still invalidate it;
			// the live fit below has its own freshness requirement.
			if (MapSpaceFrame.Near(pose, space.Frame.ToCanonical(tag.canonPose), .04f, 8f))
			{
				observationCounts[id] = ++count;
				if (count >= 12) confirmedTags.Add(id);
			}
			else { observationCounts[id] = 0; confirmedTags.Remove(id); }
			seen[id] = (MapSpaceFrame.Compose(MapSpaceFrame.Inverse(rig), pose), Time.unscaledTime);
		}
		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning || !IsAvailable) return;
			AcquireSavedAnchors();
			CheckTrackingGeneration();
			if (method == ColocationManager.ColocationMethod.AprilTag && tagProvider != null && registry != null)
			{
				realized.Clear(); tagProvider.GetLocalAnchorConstraints(realized);
				foreach (var entry in realized)
				{
					if (!space.TryGetTag(entry.tagId, out var tag) || !tagProvider.RegisteredTags.TryGetValue(entry.tagId, out var liveTag) ||
						Mathf.Abs(tagProvider.TagSizeCm - space.tagSizeCm) > .001f || !MapSpaceFrame.Near(space.Frame.ToCanonical(tag.canonPose), liveTag, .001f, .1f)) continue;
					int index = anchors.FindIndex(a => a.id == entry.guid);
					if (index >= 0) { var existing = anchors[index]; anchors[index] = new(entry.guid, existing.lease, entry.canonPose, entry.tagId); }
					else if (acquired.Add(entry.guid)) anchors.Add(new(entry.guid, registry.Acquire(new SerializableGuid(entry.guid), AnchorSource.Any), entry.canonPose, entry.tagId));
				}
			}
			observedTags.Clear();
			foreach (var entry in anchors)
			{
				var handle = entry.lease?.Handle;
				if (handle == null || handle.state != AnchorHandle.State.Active || !handle.anchor || handle.anchor.trackingState != TrackingState.Tracking) continue;
				var t = handle.anchor.transform;
				results.Add(new(new Pose(t.position, t.rotation), entry.target, true));
				if (entry.tagId >= 0) observedTags.Add(entry.tagId);
			}
			if (!MainXRRig.Instance) return;
			Pose rig = new(MainXRRig.TrackingSpace.position, MainXRRig.TrackingSpace.rotation);
			foreach (var tag in space.tags)
				if (method == ColocationManager.ColocationMethod.AprilTag && !observedTags.Contains(tag.id) && seen.TryGetValue(tag.id, out var observation) &&
					Time.unscaledTime - observation.time < .25f)
					results.Add(new(MapSpaceFrame.Compose(rig, observation.pose), space.Frame.ToCanonical(tag.canonPose)));
		}
		public void Dispose() { disposed = true; UpdateRunning(); }
	}
}
