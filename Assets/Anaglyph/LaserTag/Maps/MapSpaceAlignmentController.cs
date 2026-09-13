using System;
using System.Collections.Generic;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>One rig writer, with independently retained source and candidate observations.</summary>
	internal sealed class MapSpaceAlignmentController : IDisposable
	{
		private readonly ColocationManager manager;
		private readonly Func<bool> trackingReady;
		private readonly List<ColocationConstraint> constraints = new();
		private MapSpaceReferenceObservation active, candidate, retiring;
		private bool sourceConfigured;
		private IDisposable retiringLease;
		private bool handingOver;
		private double handoffStarted;
		public bool RequireNewTagObservations { get; set; } = true;
		private IDisposable candidateLease;
		private MapSpace candidateSpace;
		private bool committed, reported;
		private double lastReport;
		private MapSpace sourceSpace;
		private readonly List<int> authoredTags = new();
		public ReferenceAlignmentTransition Transition { get; private set; }
		public bool Busy => Transition != null && Transition.Phase is not (ReferenceTransitionPhase.Completed or ReferenceTransitionPhase.Canceled);
		public bool AuthoringAllowed => Busy && Transition.CreatesReferences && trackingReady() &&
			(Transition.Bootstrap || manager.ReferenceAligned);
		public event Action<Guid, Guid> Validated = delegate { };
		public event Action Changed = delegate { };
		public MapSpaceAlignmentController(ColocationManager manager, Func<bool> trackingReady)
		{ this.manager = manager; this.trackingReady = trackingReady; }
		private MapSpaceReferenceObservation Observer(MapSpace space, ColocationManager.ColocationMethod method) =>
			new(space, method, AnchorRegistry.Instance, manager.TagProvider);
		public void Activate(MapSpace space, ColocationManager.ColocationMethod method, bool preserve = false)
		{
			Cancel();
			var previous = active;
			active = ColocationManager.UsesSavedReferences(method) && HasConfiguration(space, method) ? Observer(space, method) : null;
			sourceConfigured = true;
			manager.ActivateSource(active ?? manager.GetProvider(method), method, preserve);
			previous?.Dispose();
			RefreshServices();
		}
		public static bool HasConfiguration(MapSpace space, ColocationManager.ColocationMethod method) => space != null &&
			(method == ColocationManager.ColocationMethod.AprilTag ? space.tags.Count > 0 :
			method == ColocationManager.ColocationMethod.MetaSharedAnchor ? space.anchors.Count > 0 : true);
		private void RefreshServices()
		{
			bool Needs(ColocationManager.ColocationMethod method) => sourceConfigured && manager.ActiveMethod == method ||
				Busy && (Transition.Target == method || handingOver && Transition.Source == method);
			// Keep both services through validation/handoff, then release the unused one.
			// Independent observation and editor detection leases still control the tracker.
			if (Needs(ColocationManager.ColocationMethod.AprilTag)) manager.TagProvider?.StartProviding();
			else manager.TagProvider?.StopProviding();
			if (Needs(ColocationManager.ColocationMethod.MetaSharedAnchor)) manager.AnchorProvider?.StartProviding();
			else manager.AnchorProvider?.StopProviding();
		}
		public void Begin(Guid operation, MapSpace source, ColocationManager.ColocationMethod target,
			ReferenceTransitionIntent intent, bool createsReferences)
		{
			if (Transition != null && Transition.Operation == operation && Transition.Phase != ReferenceTransitionPhase.Canceled)
			{
				if (Busy && Transition.CreatesReferences && !createsReferences)
				{
					// Accepted registrations are durable before the target takes over. Keep the
					// source and native observations alive while validating that saved target.
					Transition.UseSavedTarget(); authoredTags.Clear(); reported = false;
					manager.IsSettingUpTags = false; Changed.Invoke();
				}
				return;
			}
			Cancel(); committed = reported = false;
			sourceSpace = source.Clone();
			Transition = new(operation, source, manager.ActiveMethod, target, intent, createsReferences);
			// A provisional provider cannot authorize edits to an already established space.
			manager.IsSettingUpTags = createsReferences && target == ColocationManager.ColocationMethod.AprilTag;
			RefreshServices(); Changed.Invoke();
		}
		public void SetCandidate(Guid revision, MapSpace space)
		{
			if (!Busy || handingOver || Transition.Revision == revision) return;
			var previous = candidate;
			candidate = null;
			candidateSpace = space.Clone(); Transition.SetCandidate(revision); reported = false; authoredTags.Clear();
			if (Transition.CreatesReferences && Transition.Target == ColocationManager.ColocationMethod.AprilTag)
				foreach (var tag in space.tags)
					if (!sourceSpace.TryGetTag(tag.id, out var old) || !MapSpaceFrame.Near(old.canonPose, tag.canonPose, .001f, .1f)) authoredTags.Add(tag.id);
			if (HasConfiguration(space, Transition.Target))
			{ candidate = Observer(space, Transition.Target); candidate.RetainObservationsFrom(previous); }
			candidateLease?.Dispose(); previous?.Dispose();
			candidateLease = candidate?.Observe();
			Changed.Invoke();
		}
		public void Tick()
		{
			if (!Busy) return;
			if (handingOver) { ConfirmHandoff(); return; }
			if (candidate == null) return;
			if (!trackingReady() || Transition.CreatesReferences && !Transition.Bootstrap && !committed && !manager.ReferenceAligned)
			{ candidate.ResetObservations(); Transition.ResetEvidence(); reported = false; return; }
			constraints.Clear(); candidate.GetColocationConstraints(constraints);
			bool fit = ColocationFit.TryEvaluate(constraints, out Pose delta, out float error, out float angle);
			if (RequireNewTagObservations && !committed) foreach (int id in authoredTags) if (!candidate.HasRepeatedObservation(id)) fit = false;
			bool ready = Transition.Observe(Time.realtimeSinceStartupAsDouble, manager.TrackingGeneration,
				manager.ReferenceAligned, trackingReady(), fit, delta, error, angle, committed);
			if (!ready) { reported = false; return; }
			if (!reported || Time.realtimeSinceStartupAsDouble - lastReport > 1) { reported = true; lastReport = Time.realtimeSinceStartupAsDouble; Validated.Invoke(Transition.Operation, Transition.Revision); }
			if (committed && Busy) Finish();
		}
		public void Commit(bool requireLocalHandoff = true)
		{
			if (!Busy) return;
			committed = true;
			if (Transition.Intent == ReferenceTransitionIntent.SetupOnly && Transition.Source != Transition.Target) { Finish(); return; }
			if (!requireLocalHandoff)
			{
				// An operator has no physical rig to validate. Its assigned headset supplied
				// the evidence; other headsets still complete their own local handoff.
				var previous = active; active = candidate; candidate = null;
				sourceConfigured = true;
				manager.ActivateSource(active ?? manager.GetProvider(Transition.Target), Transition.Target, false);
				previous?.Dispose(); Complete(); return;
			}
			Transition.HandingOver();
			if (Transition.Ready) Finish(); else Changed.Invoke();
		}
		private void Finish()
		{
			if (Transition.Intent == ReferenceTransitionIntent.SetupOnly && Transition.Source != Transition.Target)
			{ Complete(); return; }
			if (handingOver || candidate == null) return;
			retiring = active; retiringLease = retiring?.Observe();
			active = candidate; candidate = null; handingOver = true; handoffStarted = Time.realtimeSinceStartupAsDouble;
			Transition.HandingOver(); manager.ActivateSource(active, Transition.Target, manager.ReferenceAligned); Changed.Invoke();
		}
		private void ConfirmHandoff()
		{
			constraints.Clear(); active.GetColocationConstraints(constraints);
			bool usable = trackingReady() && ColocationFit.TryEvaluate(constraints, out var delta, out float residual, out float angle) &&
				residual <= .05f && angle <= 5 && MapSpaceFrame.Near(delta, Pose.identity, .08f, 5f);
			if (usable && manager.ReferenceAligned && Time.realtimeSinceStartupAsDouble - handoffStarted >= .2)
			{ Complete(); return; }
			if (!usable && retiring != null)
			{
				// Local fallback preserves the committed session selection. Retry the target with fresh evidence.
				candidate = active; active = retiring; retiring = null;
				manager.ActivateSource(active, Transition.Source, false);
				retiringLease?.Dispose(); retiringLease = null; handingOver = false; Transition.ResetEvidence();
			}
		}
		private void Complete()
		{
			retiringLease?.Dispose(); retiringLease = null; retiring?.Dispose(); retiring = null; handingOver = false;
			Transition.Complete(); manager.IsSettingUpTags = false; ReleaseCandidate(); RefreshServices(); Changed.Invoke();
		}

		public void Cancel()
		{
			if (Busy) Transition.Cancel();
			retiringLease?.Dispose(); retiringLease = null; retiring?.Dispose(); retiring = null; handingOver = false;
			ReleaseCandidate(); manager.IsSettingUpTags = false; RefreshServices(); Changed.Invoke();
		}
		private void ReleaseCandidate() { candidateLease?.Dispose(); candidateLease = null; candidate?.Dispose(); candidate = null; candidateSpace = null; }
		public void Dispose()
		{ sourceConfigured = false; Cancel(); active?.Dispose(); active = null; }
	}
}
