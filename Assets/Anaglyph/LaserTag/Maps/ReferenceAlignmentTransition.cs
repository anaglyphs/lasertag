using System;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public enum ReferenceTransitionPhase : byte { None, Preparing, Validating, Persisting, HandingOver, Completed, Canceled }
	public enum ReferenceTransitionIntent : byte { SetupOnly, ActivateTarget }

	/// <summary>Runtime evidence is deliberately separate from durable space configuration.</summary>
	public sealed class ReferenceAlignmentTransition
	{
		public Guid Operation { get; }
		public Guid Revision { get; private set; }
		public string FrameId { get; }
		public long FrameRevision { get; }
		public ColocationManager.ColocationMethod Source { get; }
		public ColocationManager.ColocationMethod Target { get; }
		public ReferenceTransitionIntent Intent { get; }
		public bool CreatesReferences { get; private set; }
		public bool Bootstrap { get; }
		public ReferenceTransitionPhase Phase { get; private set; } = ReferenceTransitionPhase.Preparing;
		public bool Ready { get; private set; }
		private double stableSince = -1;
		private int trackingGeneration = -1;
		private Pose lastFit;
		public ReferenceAlignmentTransition(Guid operation, MapSpace space, ColocationManager.ColocationMethod source,
			ColocationManager.ColocationMethod target, ReferenceTransitionIntent intent, bool createsReferences)
		{
			Operation = operation; FrameId = space.canonicalFrameId; FrameRevision = space.frameRevision;
			Source = source; Target = target; Intent = intent; CreatesReferences = createsReferences;
			Bootstrap = !space.HasReferenceBasedData;
		}
		public void SetCandidate(Guid revision)
		{ Revision = revision; ResetEvidence(); Phase = ReferenceTransitionPhase.Validating; }
		public void UseSavedTarget() { CreatesReferences = false; ResetEvidence(); }
		public void ResetEvidence() { Ready = false; stableSince = -1; }
		public bool Observe(double now, int generation, bool sourceAligned, bool trackingReady, bool fitValid, Pose fit,
			float residual, float angularResidual, bool committedRecovery = false)
		{
			bool requiresSource = CreatesReferences && !Bootstrap && !committedRecovery;
			bool agrees = !sourceAligned || MapSpaceFrame.Near(fit, Pose.identity, .08f, 5f);
			if (!trackingReady || (requiresSource && !sourceAligned) || !fitValid || !agrees || residual > .05f || angularResidual > 5f)
			{ ResetEvidence(); return false; }
			if (trackingGeneration != generation || stableSince < 0 || !MapSpaceFrame.Near(fit, lastFit, .02f, 1f))
			{ stableSince = now; Ready = false; }
			trackingGeneration = generation; lastFit = fit;
			Ready = now - stableSince >= .6;
			return Ready;
		}
		public void Persisting() => Phase = ReferenceTransitionPhase.Persisting;
		public void HandingOver() => Phase = ReferenceTransitionPhase.HandingOver;
		public void Complete() => Phase = ReferenceTransitionPhase.Completed;
		public void Cancel() { ResetEvidence(); Phase = ReferenceTransitionPhase.Canceled; }
	}
}
