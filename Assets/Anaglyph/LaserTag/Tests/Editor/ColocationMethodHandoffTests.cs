using System;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Tests
{
	public class ColocationMethodHandoffTests
	{
		private static MapSpace Established()
		{ var space = MapSpace.Create("Room"); space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1); return space; }
		private static ReferenceAlignmentTransition Transition(bool creates = true, bool bootstrap = false) =>
			new(Guid.NewGuid(), bootstrap ? MapSpace.Create("New") : Established(), Method.MetaSharedAnchor, Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, creates);
		private static bool Observe(ReferenceAlignmentTransition t, double now, bool source = true, int generation = 1, float error = 0) =>
			t.Observe(now, generation, source, true, true, Pose.identity, error, 0);
		[Test] public void CandidateNeedsFreshStableEvidenceAndLossOfSourceInvalidatesIt()
		{
			var t = Transition(); t.SetCandidate(Guid.NewGuid()); Assert.That(Observe(t, 0), Is.False);
			Assert.That(Observe(t, .7), Is.True); Assert.That(Observe(t, .8, source: false), Is.False);
			Assert.That(Observe(t, .9), Is.False); Assert.That(Observe(t, 1.6), Is.True);
		}
		[Test] public void TrackingOrCandidateRevisionChangeRequiresNewEvidence()
		{
			var t = Transition(); t.SetCandidate(Guid.NewGuid()); Observe(t, 0); Assert.That(Observe(t, 1), Is.True);
			Assert.That(Observe(t, 2, generation: 2), Is.False); Assert.That(Observe(t, 3, generation: 2), Is.True);
			t.SetCandidate(Guid.NewGuid()); Assert.That(t.Ready, Is.False); Assert.That(Observe(t, 4, generation: 2), Is.False);
		}
		[Test] public void BadFitCannotBecomePermanentAndOrdinaryHandoffCannotHideOffset()
		{
			var t = Transition(); t.SetCandidate(Guid.NewGuid()); Observe(t, 0);
			Assert.That(Observe(t, 1, error: .2f), Is.False);
			Assert.That(t.Observe(2, 1, true, true, true, new Pose(Vector3.right, Quaternion.identity), 0, 0), Is.False);
		}
		[Test] public void ConfiguredTargetRecoveryDoesNotRequireUnavailableOldSource()
		{
			var t = Transition(creates: false); t.SetCandidate(Guid.NewGuid()); Observe(t, 0, false);
			Assert.That(Observe(t, 1, false), Is.True);
			var setup = Transition(creates: true); setup.SetCandidate(Guid.NewGuid()); Observe(setup, 0, false);
			Assert.That(Observe(setup, 1, false), Is.False);
		}
		[Test] public void FirstReferenceMayBootstrapButProvisionalCannotAuthorizeLaterReferences()
		{
			var t = Transition(bootstrap: true); t.SetCandidate(Guid.NewGuid()); Observe(t, 0, false); Assert.That(Observe(t, 1, false), Is.True);
			foreach (var provisional in new[] { Method.SystemDetermined, Method.TwoAprilTags })
			{
				Assert.That(MapPolicy.CanAuthorReferences(true, provisional, true, true), Is.False);
				Assert.That(MapPolicy.CanAuthorReferences(false, provisional, true, false), Is.False);
				Assert.That(MapPolicy.CanAuthorReferences(false, provisional, true, true), Is.True);
			}
		}
		[Test] public void ImportProjectsAnchorAndTagTargetsThroughTheSameOffsetAndKeepsPrivateUuid()
		{
			var owner = new GameObject("Space adapter test"); owner.SetActive(false);
			try
			{
				var manager = owner.AddComponent<ColocationManager>(); var tags = owner.AddComponent<AprilTagColocationConstraintProvider>();
				var anchors = owner.AddComponent<SpatialAnchorColocationConstraintProvider>(); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
				typeof(ColocationManager).GetField("aprilTagColocationProvider", flags).SetValue(manager, tags);
				typeof(ColocationManager).GetField("spatialAnchorColocationProvider", flags).SetValue(manager, anchors);
				var adapterType = typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceColocationAdapter", true);
				var adapter = Activator.CreateInstance(adapterType, new object[] { manager });
				var space = MapSpaceReconciler.Rebase(Established(), Guid.NewGuid().ToString("N"), new Pose(Vector3.right * 5, Quaternion.Euler(0, 90, 0)));
				space.SetTag(7, Pose.identity); string privateId = Guid.NewGuid().ToString("N");
				space.localAnchors.Add(new() { guid = privateId, tagId = 7, canonPose = Pose.identity, tagCanonPose = Pose.identity, tagSizeCm = 10 });
				adapterType.GetMethod("Inject").Invoke(adapter, new object[] { space });
				Assert.That(Vector3.Distance(tags.RegisteredTags[7].position, space.canonicalFromStorage.position), Is.LessThan(.0001f));
				var realized = new List<TaggedAnchorConstraintData>(); tags.GetLocalAnchorConstraints(realized);
				Assert.That(realized.Count, Is.EqualTo(1)); Assert.That(realized[0].guid.ToString("N"), Is.EqualTo(privateId));
				Assert.That(realized[0].canonPose, Is.EqualTo(tags.RegisteredTags[7]));
			}
			finally { UnityEngine.Object.DestroyImmediate(owner); }
		}
		[Test] public void FitRejectsDegeneratePositionsButCanUseOneOrientedAnchor()
		{
			var constraints = new List<ColocationConstraint> { new(Pose.identity, Pose.identity), new(Pose.identity, Pose.identity) };
			Assert.That(ColocationFit.TryEvaluate(constraints, out _, out _, out _), Is.False);
			constraints.Clear(); constraints.Add(new(Pose.identity, new Pose(Vector3.one, Quaternion.Euler(0, 60, 0)), true));
			Assert.That(ColocationFit.TryEvaluate(constraints, out var fit, out var residual, out _), Is.True);
			Assert.That(residual, Is.LessThan(.001f)); Assert.That(fit.position, Is.EqualTo(Vector3.one));
		}
		private sealed class Source : IColocationConstraintProvider
		{
			private readonly List<string> events; private readonly string name;
			public Source(List<string> events, string name) { this.events = events; this.name = name; }
			public bool IsAvailable => true; public bool IsRunning { get; private set; }
			public void StartProviding() { IsRunning = true; events.Add(name + ":start"); }
			public void StopProviding() { IsRunning = false; events.Add(name + ":stop"); }
			public void GetColocationConstraints(List<ColocationConstraint> result) { }
		}
		[Test] public void ValidatedHandoffStartsTargetBeforeRetiringSourceWithoutResettingAlignment()
		{
			var owner = new GameObject("Handoff test"); owner.SetActive(false);
			try
			{
				var solver = owner.AddComponent<Colocator>(); List<string> events = new(); var old = new Source(events, "old"); var next = new Source(events, "new");
				solver.SetProvider(old); old.StartProviding(); events.Clear();
				typeof(Colocator).GetMethod("SetState", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(solver, new object[] { ColocationAlignmentState.Localized });
				solver.Handoff(next); Assert.That(events, Is.EqualTo(new[] { "new:start", "old:stop" }));
				Assert.That(solver.Provider, Is.SameAs(next)); Assert.That(solver.AlignmentState, Is.EqualTo(ColocationAlignmentState.Localized));
			}
			finally { UnityEngine.Object.DestroyImmediate(owner); }
		}
	}
}
