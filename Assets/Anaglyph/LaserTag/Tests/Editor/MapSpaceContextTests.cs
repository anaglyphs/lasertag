using System;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Permissions;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Tests
{
	public class MapSpaceContextTests
	{
		[TestCase(CapabilitySupport.Supported, Method.MetaSharedAnchor)]
		[TestCase(CapabilitySupport.Unsupported, Method.AprilTag)]
		[TestCase(CapabilitySupport.Unknown, Method.MetaSharedAnchor)]
		public void NoMatchingSpaceUsesSharingCapabilityForInitialAlignment(CapabilitySupport sharing, Method expected)
		{
			Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.NoMatches, sharing, true), Is.EqualTo(expected));
		}

		[Test]
		public void InterruptedQueriesNeverChooseAStartupMethod()
		{
			foreach (CapabilitySupport sharing in Enum.GetValues(typeof(CapabilitySupport)))
			{
				Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Canceled, sharing, false), Is.Null);
				Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Indeterminate, sharing, true), Is.Null);
				Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Matches, sharing, true), Is.Null);
			}
		}

		[Test]
		public void ConfirmedLackOfAnchorSupportCanStartTagsWithoutWaitingForAnImpossibleProbe()
		{
			Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Indeterminate, CapabilitySupport.Unsupported, false), Is.EqualTo(Method.AprilTag));
			Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Indeterminate, CapabilitySupport.Unknown, false), Is.Null);
			Assert.That(MapSpaceStartup.InitialMethod(SpaceProbeOutcome.Indeterminate, CapabilitySupport.Supported, false), Is.Null);
		}

		[Test]
		public void AprilTagFallbackPersistsSetupIntentAndReusesTheDraftFrameAndLayoutIds()
		{
			var space = MapSpace.Create("Managed room"); space.automaticallyCreated = space.initializationPending = true;
			string layout = Guid.NewGuid().ToString("N"); space.mapIds.Add(layout);
			string id = space.id, frame = space.canonicalFrameId, originalVersion = space.version;
			Assert.That(MapSpaceStartup.ConfigureDraft(space, Method.AprilTag), Is.True);
			var reopened = JsonUtility.FromJson<MapSpace>(JsonUtility.ToJson(space));
			Assert.That(reopened.preferredColocationMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(reopened.hasPendingSetup, Is.True);
			Assert.That(reopened.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(reopened.pendingSetupIntent, Is.EqualTo(ReferenceTransitionIntent.ActivateTarget));
			Assert.That(reopened.version, Is.Not.EqualTo(originalVersion), "Session peers need a new document revision for the fallback.");
			string revision = reopened.version;
			Assert.That(MapSpaceStartup.ConfigureDraft(reopened, Method.AprilTag), Is.True);
			Assert.That(reopened.version, Is.EqualTo(revision), "Retrying the same configuration is idempotent.");
			Assert.That(reopened.id, Is.EqualTo(id)); Assert.That(reopened.canonicalFrameId, Is.EqualTo(frame));
			Assert.That(reopened.mapIds, Is.EqualTo(new[] { layout })); Assert.That(reopened.HasReferenceBasedData, Is.False);
		}

		[Test]
		public void FallbackCannotReconfigureAnEstablishedOrOperatorCreatedSpace()
		{
			var space = MapSpace.Create("Room"); space.initializationPending = true;
			Assert.That(MapSpaceStartup.ConfigureDraft(space, Method.AprilTag), Is.False);
			space.automaticallyCreated = true; space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
			Assert.That(MapSpaceStartup.ConfigureDraft(space, Method.AprilTag), Is.False);
			space.anchors.Clear(); space.retainedReferences.Add(new() { tags = new() { new() { id = 7, canonPose = Pose.identity } } });
			Assert.That(MapSpaceStartup.ConfigureDraft(space, Method.AprilTag), Is.False);
			Assert.That(space.preferredColocationMethod, Is.EqualTo(Method.MetaSharedAnchor));
		}

		[Test]
		public void OnlyAutomaticInitializationRequiresOfflineMintingToConfirmSharing()
		{
			var owner = new GameObject("Startup sharing policy test"); owner.SetActive(false);
			try
			{
				var anchors = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
				var manager = owner.AddComponent<ColocationManager>(); manager.ManagedSelection = true;
				typeof(ColocationManager).GetField("spatialAnchorColocationProvider", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(manager, anchors);
				var space = MapSpace.Create("Room"); space.automaticallyCreated = space.initializationPending = true;
				manager.ConfigureSpace(space, () => true); Assert.That(anchors.RequireSharingForMint, Is.True);
				space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
				manager.ConfigureSpace(space, () => true); Assert.That(anchors.RequireSharingForMint, Is.False);
				manager.ConfigureSpace(MapSpace.Create("Operator room"), () => true); Assert.That(anchors.RequireSharingForMint, Is.False);
			}
			finally { UnityEngine.Object.DestroyImmediate(owner); }
		}

		[Test]
		public void ReturningFromSystemSettingsAllowsSharingToBeTriedAgain()
		{
			var owner = new GameObject("Sharing retry test"); owner.SetActive(false);
			try
			{
				var registry = owner.AddComponent<AnchorRegistry>();
				typeof(AnchorRegistry).GetMethod("ObserveSharingResult", BindingFlags.NonPublic | BindingFlags.Instance)
					.Invoke(registry, new object[] { new XRResultStatus(-1000169004) });
				Assert.That(registry.SharingDenied, Is.True);
				typeof(AnchorRegistry).GetMethod("OnApplicationFocus", BindingFlags.NonPublic | BindingFlags.Instance)
					.Invoke(registry, new object[] { true });
				Assert.That(registry.SharingDenied, Is.False);
			}
			finally { UnityEngine.Object.DestroyImmediate(owner); }
		}

		[Test]
		public void FirstTagNeedsOneAssignedAuthorButDoesNotNeedAnAnchorMinter()
		{
			Assert.That(MapPolicy.CanInitializeTags(false, false, false, false), Is.True, "Standalone tag setup.");
			Assert.That(MapPolicy.CanInitializeTags(true, true, false, false), Is.True, "A tracked headset host can initiate setup without a Meta account.");
			Assert.That(MapPolicy.CanInitializeTags(true, false, false, false), Is.False, "Clients wait for an initialization grant.");
			Assert.That(MapPolicy.CanInitializeTags(true, false, true, true), Is.True, "An operator's assigned headset can register the first tag.");
			Assert.That(MapPolicy.CanInitializeTags(true, true, true, false), Is.False, "Even authority cannot replace the assigned author's frame.");
			Assert.That(MapPolicy.CanAuthorReferences(true, Method.SystemDetermined, true, true), Is.False);
			Assert.That(MapPolicy.CanAuthorReferences(true, Method.TwoAprilTags, true, true), Is.False);
		}

		[TestCase(-1000169004, true)] // Cloud storage disabled.
		[TestCase(-1000259003, true)] // Insufficient permission.
		[TestCase(-1000169002, false)] // Network timeout.
		[TestCase(-1000169003, false)] // Failed network request.
		[TestCase(-1000169001, false)] // Failed localization.
		public void SharingDenialsAreDistinctFromTransientOperationFailures(int nativeCode, bool denied)
		{
			var owner = new GameObject("Sharing capability test"); owner.SetActive(false);
			try
			{
				var registry = owner.AddComponent<AnchorRegistry>();
				var observe = typeof(AnchorRegistry).GetMethod("ObserveSharingResult", BindingFlags.NonPublic | BindingFlags.Instance);
				observe.Invoke(registry, new object[] { new XRResultStatus(nativeCode) });
				Assert.That(registry.SharingDenied, Is.EqualTo(denied));
			}
			finally { UnityEngine.Object.DestroyImmediate(owner); }
		}

		private static SpaceProbeResult Classify(List<MapSpace> spaces, HashSet<Guid> localized, Dictionary<string, int> results) =>
			(SpaceProbeResult)typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceDiscovery")
				.GetMethod("Classify", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { spaces, localized, results });

		[Test]
		public void ProbingKeepsOverlappingSpacesAndSelectsMostRecentlyUsedMatch()
		{
			Guid shared = Guid.NewGuid(), other = Guid.NewGuid();
			var a = MapSpace.Create("A"); a.lastUsed = 1; a.SetAnchorWithTag(shared.ToString("N"), Pose.identity, -1);
			var b = MapSpace.Create("B"); b.lastUsed = 9; b.SetAnchorWithTag(shared.ToString("N"), Pose.identity, -1);
			var c = MapSpace.Create("C"); c.lastUsed = 100; c.SetAnchorWithTag(other.ToString("N"), Pose.identity, -1);
			var unknown = MapSpace.Create("Unknown");
			Dictionary<string, int> results = new();
			var probe = Classify(new() { a, c, unknown, b }, new() { shared }, results);
			Assert.That(probe.outcome, Is.EqualTo(SpaceProbeOutcome.Matches)); Assert.That(probe.best.id, Is.EqualTo(b.id));
			Assert.That(results[a.id], Is.EqualTo(1)); Assert.That(results[b.id], Is.EqualTo(1));
			Assert.That(results[c.id], Is.Zero); Assert.That(results.ContainsKey(unknown.id), Is.False);
		}

		[Test]
		public void FailedProbeDoesNotBecomeAHealthyNoMatchOrReplacePreviousResults()
		{
			var space = MapSpace.Create("Room"); Dictionary<string, int> results = new() { [space.id] = 1 };
			Assert.That(Classify(new() { space }, null, results).outcome, Is.EqualTo(SpaceProbeOutcome.Indeterminate));
			Assert.That(results[space.id], Is.EqualTo(1));
			Assert.That(Classify(new() { space }, new(), results).outcome, Is.EqualTo(SpaceProbeOutcome.NoMatches));
			Assert.That(results.ContainsKey(space.id), Is.False, "A space without UUIDs remains unknown.");
		}

		[Test]
		public void ScanPacketsFromThePreviousVisitCannotApplyAfterReturningToTheSameFrame()
		{
			var a = new MapSpaceScanContext { space = Guid.NewGuid(), frame = Guid.NewGuid(), scan = Guid.NewGuid() };
			var b = new MapSpaceScanContext { space = Guid.NewGuid(), frame = Guid.NewGuid(), scan = Guid.NewGuid() };
			var returnToA = a; returnToA.scan = Guid.NewGuid();
			Assert.That(a.Matches(a), Is.True); Assert.That(a.Matches(b), Is.False);
			Assert.That(a.Matches(returnToA), Is.False); Assert.That(default(MapSpaceScanContext).Matches(default), Is.False);
			var rebase = a; rebase.frame = Guid.NewGuid(); Assert.That(a.Matches(rebase), Is.False);
		}

		[Test]
		public void RememberedFrameOffsetCannotHideConflictingImmutableAnchorTargets()
		{
			string anchor = Guid.NewGuid().ToString("N"); var local = MapSpace.Create("Local");
			local.SetAnchorWithTag(anchor, new Pose(Vector3.right, Quaternion.identity), -1);
			Pose offset = new(Vector3.forward * 3, Quaternion.Euler(0, 90, 0));
			var remote = MapSpace.Create("Host"); remote.SetAnchorWithTag(anchor, MapSpaceFrame.Compose(offset, local.anchors[0].canonPose), -1);
			Assert.That(MapSpaceReconciler.HasFrameContradiction(local, remote, offset), Is.False);
			remote.SetAnchorWithTag(anchor, Pose.identity, -1);
			Assert.That(MapSpaceReconciler.HasFrameContradiction(local, remote, offset), Is.True);
		}
	}
}
