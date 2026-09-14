using Anaglyph.LaserTag.Maps;
using NUnit.Framework;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Tests
{
	public class MapLifecycleTests
	{
		[TestCase(MapPhase.Local, true, true)]
		[TestCase(MapPhase.Hosting, true, true)]
		[TestCase(MapPhase.SwitchingMap, false, true)]
		[TestCase(MapPhase.AwaitingSessionMap, false, false)]
		[TestCase(MapPhase.FollowingSession, false, false)]
		[TestCase(MapPhase.RestoringLocalMap, false, false)]
		[TestCase(MapPhase.Stopped, false, false)]
		public void MapChangesAndSceneCaptureRespectAuthorityAndLifecycle(MapPhase phase, bool manage, bool capture)
		{
			Assert.That(MapPolicy.CanManageMaps(true, phase), Is.EqualTo(manage));
			Assert.That(MapPolicy.CanCaptureScene(true, phase), Is.EqualTo(capture));
			Assert.That(MapPolicy.CanManageMaps(false, phase), Is.False);
			Assert.That(MapPolicy.CanCaptureScene(false, phase), Is.False);
		}

		[TestCase(false, true, false, true)]
		[TestCase(false, false, true, true)]
		[TestCase(false, false, false, false)]
		[TestCase(true, true, false, false)]
		[TestCase(true, false, true, false)]
		[TestCase(true, true, true, false)]
		public void EditingRequiresAReadyFrameOrOperatorAccessAndACompletedMapChange(
			bool changingMap, bool frameReady, bool operatorCanEditSpace, bool expected)
		{
			Assert.That(MapPolicy.CanEditMap(changingMap, frameReady, operatorCanEditSpace), Is.EqualTo(expected));
		}

		[Test]
		public void NewMapsNeedASpaceAndAuthorityButAllowAnotherBlankLayout()
		{
			Assert.That(MapPolicy.NewMapBlocker(true, false, true), Is.Null);
			Assert.That(MapPolicy.NewMapBlocker(true, false, false), Is.EqualTo("space.load-first"));
			Assert.That(MapPolicy.NewMapBlocker(true, true, true), Is.EqualTo("blocker.not-during-a-round"));
			Assert.That(MapPolicy.NewMapBlocker(false, true, false), Is.EqualTo("blocker.only-the-host-can-change-the-map"));
		}

		[TestCase(false, true, true, false, "blocker.only-the-host-can-change-the-map")]
		[TestCase(true, true, true, false, "blocker.not-during-a-round")]
		[TestCase(true, false, true, false, "blocker.already-loaded")]
		[TestCase(true, false, false, false, "blocker.map-is-missing")]
		[TestCase(true, false, false, true, null)]
		public void MapChangeBlockersPreserveTheirPriority(
			bool canManage, bool roundInProgress, bool alreadyLoaded, bool exists, string expected)
		{
			Assert.That(MapPolicy.ChangeMapBlocker(canManage, roundInProgress, alreadyLoaded, exists), Is.EqualTo(expected));
		}

		[Test]
		public void FollowersCannotRenameOrDeleteTheActiveSessionMap()
		{
			Assert.That(MapPolicy.RenameBlocker(false), Is.EqualTo("blocker.only-the-host-can-rename-the-map"));
			Assert.That(MapPolicy.RenameBlocker(true), Is.Null);
			Assert.That(MapPolicy.DeleteMapBlocker("active", "active", true),
				Is.EqualTo("blocker.cannot-delete-the-active-session-map"));
			Assert.That(MapPolicy.DeleteMapBlocker("other", "active", true), Is.Null);
			Assert.That(MapPolicy.DeleteMapBlocker("active", "active", false), Is.Null);
			Assert.That(MapPolicy.DeleteMapBlocker(null, null, true), Is.EqualTo("blocker.no-map-selected"));
		}

		[Test]
		public void OnlyTheOperatorCanSelectASpaceFoundElsewhere()
		{
			Assert.That(MapPolicy.SpaceChangeBlocker(true, false, false, false, MapPresence.Elsewhere),
				Is.EqualTo("blocker.map-belongs-to-another-room"));
			Assert.That(MapPolicy.SpaceChangeBlocker(true, false, false, true, MapPresence.Elsewhere), Is.Null);
			Assert.That(MapPolicy.SpaceChangeBlocker(true, false, false, false, MapPresence.Unknown), Is.Null);
			Assert.That(MapPolicy.SpaceChangeBlocker(false, false, false, true, MapPresence.Elsewhere), Is.EqualTo("space.busy"));
			Assert.That(MapPolicy.SpaceChangeBlocker(true, true, false, true, MapPresence.Unknown), Is.EqualTo("space.busy"));
			Assert.That(MapPolicy.SpaceChangeBlocker(true, false, true, true, MapPresence.Unknown), Is.EqualTo("space.busy"));
		}

		[TestCase(false, false, false, false)]
		[TestCase(true, true, false, false)]
		[TestCase(true, false, true, false)]
		[TestCase(true, false, false, true)]
		public void SpaceDeletionWaitsForManagementAlignmentAndSessionAvailability(
			bool canManage, bool roundInProgress, bool changingAlignment, bool activeSessionSpace)
		{
			Assert.That(MapPolicy.DeleteSpaceBlocker(canManage, roundInProgress, changingAlignment, activeSessionSpace),
				Is.EqualTo("space.busy"));
			Assert.That(MapPolicy.DeleteSpaceBlocker(true, false, false, false), Is.Null);
		}

		[TestCase(Method.MetaSharedAnchor)]
		[TestCase(Method.AprilTag)]
		[TestCase(Method.SystemDetermined)]
		[TestCase(Method.TwoAprilTags)]
		public void AlignmentSelectionCanStartBeforeTargetReferencesAreReady(Method method)
		{
			Assert.That(MapPolicy.ColocationMethodBlocker(method, true, false, false, false), Is.Null);
			Assert.That(MapPolicy.ColocationMethodBlocker(method, false, false, false, false), Is.EqualTo("space.load-first"));
			Assert.That(MapPolicy.ColocationMethodBlocker(method, true, true, false, false),
				Is.EqualTo("blocker.wait-until-the-round-ends"));
		}

		[Test]
		public void UnknownAlignmentMethodIsRejectedBeforeOtherBlockers()
		{
			Assert.That(MapPolicy.ColocationMethodBlocker((Method)255, false, true, true, false),
				Is.EqualTo("blocker.unknown-colocation-method"));
		}

		[TestCase(Method.MetaSharedAnchor, true)]
		[TestCase(Method.AprilTag, true)]
		[TestCase(Method.SystemDetermined, false)]
		[TestCase(Method.TwoAprilTags, false)]
		public void ExistingReferencesRequireAlignmentToASavedReferenceMethod(Method method, bool canAuthor)
		{
			Assert.That(MapPolicy.CanAuthorReferences(true, method, true, true), Is.EqualTo(canAuthor));
			Assert.That(MapPolicy.CanAuthorReferences(true, method, false, true), Is.False);
		}

		[Test]
		public void FirstReferencesRequireAnInitializationGrant()
		{
			Assert.That(MapPolicy.CanAuthorReferences(false, Method.SystemDetermined, false, true), Is.True);
			Assert.That(MapPolicy.CanAuthorReferences(false, Method.SystemDetermined, true, false), Is.False);
		}

		[Test]
		public void TagRemovalRemainsAvailableWithoutAlignmentButWaitsForReferenceChanges()
		{
			Assert.That(MapPolicy.TagRegistrationBlocker(false), Is.EqualTo("alignment.align-before-reference-setup"));
			Assert.That(MapPolicy.TagRegistrationBlocker(true), Is.Null);
			Assert.That(MapPolicy.TagRemovalBlocker(true, false), Is.Null);
			Assert.That(MapPolicy.TagRemovalBlocker(true, true), Is.EqualTo("space.busy"));
			Assert.That(MapPolicy.TagRemovalBlocker(false, true), Is.EqualTo("space.load-first"));
		}

		[Test]
		public void TagSizeCanChangeDuringSetupOnlyWhileThereAreNoRegisteredTags()
		{
			Assert.That(MapPolicy.TagSizeBlocker(true, false), Is.Null);
			Assert.That(MapPolicy.TagSizeBlocker(true, true),
				Is.EqualTo("blocker.unregister-this-map-s-tags-to-change-their-size"));
			Assert.That(MapPolicy.TagSizeBlocker(false, true), Is.EqualTo("space.load-first"));
		}

		[Test]
		public void InitialMethodUsesExistingReferencesWithoutRedefiningTheMap()
		{
			Assert.That(ColocationManager.CompatibleMethod(Method.AprilTag, false, true, true), Is.EqualTo(Method.MetaSharedAnchor));
			Assert.That(ColocationManager.CompatibleMethod(Method.MetaSharedAnchor, true, true, false), Is.EqualTo(Method.AprilTag));
			Assert.That(ColocationManager.CompatibleMethod(Method.AprilTag, false, false, false), Is.EqualTo(Method.AprilTag));
			Assert.That(ColocationManager.CompatibleMethod(Method.MetaSharedAnchor, true, true, true), Is.EqualTo(Method.MetaSharedAnchor));
		}

		[Test]
		public void SessionClientsWaitForTheirFirstSnapshotThenFollowTheSession()
		{
			var lifecycle = new MapLifecycle();
			lifecycle.EnterSession(authority: false);
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.AwaitingSessionMap));
			lifecycle.FollowSession();
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.FollowingSession));
			lifecycle.EnterSession(authority: true);
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.Hosting));
		}

		[Test]
		public void HostFinishesAMapSwitchWhenItsTimeoutExpires()
		{
			var lifecycle = new MapLifecycle();
			lifecycle.EnterSession(authority: true);
			lifecycle.BeginSwitch(now: 10f);
			Assert.That(MapPolicy.CanManageMaps(true, lifecycle.Phase), Is.False);
			Assert.That(lifecycle.SwitchTimedOut(now: 29f, timeout: 20f), Is.False);
			Assert.That(lifecycle.SwitchTimedOut(now: 30f, timeout: 20f), Is.True);
			lifecycle.FinishSwitch();
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.Hosting));
			Assert.That(lifecycle.SwitchTimedOut(now: 40f, timeout: 20f), Is.False);
			Assert.That(MapPolicy.CanManageMaps(true, lifecycle.Phase), Is.True);
		}

		[Test]
		public void PromotedHostTakesOverThePendingMapChangeWithANewTimeout()
		{
			var lifecycle = new MapLifecycle();
			lifecycle.EnterSession(authority: false);
			lifecycle.BeginHosting(changingMap: true, now: 100f);
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.SwitchingMap));
			Assert.That(lifecycle.SwitchTimedOut(now: 119f, timeout: 20f), Is.False);
			Assert.That(lifecycle.SwitchTimedOut(now: 120f, timeout: 20f), Is.True);
			lifecycle.BeginHosting(changingMap: false, now: 121f);
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.Hosting));
		}

		[Test]
		public void FinishingASwitchCannotRestartARestoringOrStoppedSession()
		{
			var lifecycle = new MapLifecycle();
			int operation = lifecycle.BeginRestore();
			lifecycle.FinishSwitch();
			Assert.That(lifecycle.IsCurrentRestore(operation), Is.True);
			lifecycle.Stop();
			lifecycle.FinishSwitch();
			Assert.That(lifecycle.Phase, Is.EqualTo(MapPhase.Stopped));
		}

		[Test]
		public void OldDisconnectCannotRestoreOverANewSessionOrNewerDisconnect()
		{
			var lifecycle = new MapLifecycle();
			int oldOperation = lifecycle.BeginRestore();
			Assert.That(lifecycle.IsCurrentRestore(oldOperation), Is.True);
			lifecycle.EnterSession(authority: false);
			Assert.That(lifecycle.IsCurrentRestore(oldOperation), Is.False);
			int newOperation = lifecycle.BeginRestore();
			Assert.That(lifecycle.IsCurrentRestore(oldOperation), Is.False);
			Assert.That(lifecycle.IsCurrentRestore(newOperation), Is.True);
			lifecycle.Stop();
			Assert.That(lifecycle.IsCurrentRestore(newOperation), Is.False);
		}

		[Test]
		public void LocalActivationAndIncomingSnapshotsInvalidateAnOldRestore()
		{
			var lifecycle = new MapLifecycle();
			int operation = lifecycle.BeginRestore();
			lifecycle.EnterLocal();
			Assert.That(lifecycle.IsCurrentRestore(operation), Is.False);
			operation = lifecycle.BeginRestore();
			lifecycle.FollowSession();
			Assert.That(lifecycle.IsCurrentRestore(operation), Is.False);
		}
	}
}
