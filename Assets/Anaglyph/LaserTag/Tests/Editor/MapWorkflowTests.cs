using System;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;

namespace Anaglyph.LaserTag.Tests
{
	public class MapWorkflowTests
	{
		[Test]
		public void AnchorOnlyMapCannotBootstrapANewFrameThroughAnEmptyTagProvider()
		{
			Assert.That(MapPolicy.CanBootstrapFrame(false, 1, false, true, false), Is.False);
			Assert.That(MapPolicy.CanBootstrapFrame(false, 1, false, true, true), Is.False);
			Assert.That(MapPolicy.CanBootstrapFrame(false, 0, false, true, false), Is.False);
		}

		[Test]
		public void BlankMapsCanRegisterTheirFirstTagButRegisteredTagsMustBeLocalized()
		{
			Assert.That(MapPolicy.CanBootstrapFrame(false, 0, true, true, false), Is.True);
			Assert.That(MapPolicy.CanBootstrapFrame(false, 0, true, true, true), Is.True);
			Assert.That(MapPolicy.CanBootstrapFrame(true, 0, false, true, true), Is.False);
			// Placing the first object must not prevent an offline author minting its first anchor.
			Assert.That(MapPolicy.CanBootstrapFrame(false, 0, false, false, true), Is.True);
		}

		[TestCase(MapPhase.Hosting)]
		[TestCase(MapPhase.FollowingSession)]
		public void AnySessionPeerCanRequestAMethodChangeBetweenRounds(MapPhase phase)
		{
			Assert.That(Policy(phase).ColocationMethodBlocker(true), Is.Null);
			Assert.That(Policy(phase, playing: true).ColocationMethodBlocker(false), Is.Not.Null);
			Assert.That(Policy(phase, holding: true).ColocationMethodBlocker(false), Is.Not.Null);
			Assert.That(Policy(phase, hasTags: false).ColocationMethodBlocker(true), Is.Not.Null);
			// An unaligned client can request a working alternative rather than being locked out.
			Assert.That(Policy(phase, aligned: false).ColocationMethodBlocker(false), Is.Null);
		}

		[TestCase(MapPhase.Local)]
		[TestCase(MapPhase.Hosting)]
		[TestCase(MapPhase.FollowingSession)]
		public void PreferenceCanBeSavedBeforeTagsExistOrAlignmentCompletes(MapPhase phase)
		{
			Assert.That(Policy(phase, hasTags: false, aligned: false).ColocationPreferenceBlocker, Is.Null);
			Assert.That(Policy(phase, playing: true).ColocationPreferenceBlocker, Is.Not.Null);
			Assert.That(Policy(phase, holding: true).ColocationPreferenceBlocker, Is.Not.Null);
			Assert.That(Policy(phase, hasTags: false, empty: true).ColocationMethodBlocker(true), Is.Null);
		}

		[Test]
		public void InitialMethodUsesExistingReferencesWithoutRedefiningTheMap()
		{
			Assert.That(ColocationManager.CompatibleMethod(ColocationManager.ColocationMethod.AprilTag,
				false, true, true), Is.EqualTo(ColocationManager.ColocationMethod.MetaSharedAnchor));
			Assert.That(ColocationManager.CompatibleMethod(ColocationManager.ColocationMethod.MetaSharedAnchor,
				true, true, false), Is.EqualTo(ColocationManager.ColocationMethod.AprilTag));
			Assert.That(ColocationManager.CompatibleMethod(ColocationManager.ColocationMethod.AprilTag,
				false, false, false), Is.EqualTo(ColocationManager.ColocationMethod.AprilTag));
			Assert.That(ColocationManager.CompatibleMethod(ColocationManager.ColocationMethod.MetaSharedAnchor,
				true, true, true), Is.EqualTo(ColocationManager.ColocationMethod.MetaSharedAnchor));
		}

		private static MapPolicy Policy(MapPhase phase, bool aligned = true, bool hasMap = true,
			bool empty = false, bool hasTags = true, bool holding = false,
			bool playing = false, bool usesTags = true) => new(
			phase: phase, hasMap: hasMap, empty: empty, hasTags: hasTags,
			frameAgrees: aligned, sessionHolding: holding,
			roundInProgress: playing, sessionUsesTags: usesTags);

		// A client's replicated scene may be incomplete even when its document is complete.
		// Only local/authority scenes may replace persisted object placements.
		[TestCase(MapPhase.Local, true, true, false)]
		[TestCase(MapPhase.Hosting, true, true, true)]
		[TestCase(MapPhase.SwitchingMap, false, true, true)]
		[TestCase(MapPhase.AwaitingSessionMap, false, false, false)]
		[TestCase(MapPhase.AdoptingSessionMap, false, false, false)]
		[TestCase(MapPhase.FollowingSession, true, false, false)]
		[TestCase(MapPhase.RestoringLocalMap, false, false, false)]
		[TestCase(MapPhase.Stopped, false, false, false)]
		public void LifecycleControlsEditingCaptureAndPublication(
			MapPhase phase, bool edit, bool capture, bool publish)
		{
			MapPolicy policy = Policy(phase);
			Assert.That(policy.EditBlocker == null, Is.EqualTo(edit));
			Assert.That(policy.CanCaptureScene, Is.EqualTo(capture));
			Assert.That(policy.PublishesContent, Is.EqualTo(publish));
		}

		[Test]
		public void FollowersCanEditAndRecordDeviceAnchorsButCannotManageTheSession()
		{
			MapPolicy policy = Policy(MapPhase.FollowingSession);
			Assert.That(policy.EditBlocker, Is.Null);
			Assert.That(policy.CanRecordReferences, Is.True);
			Assert.That(policy.CanCreateMap, Is.False);
			Assert.That(policy.RenameBlocker, Is.Not.Null);
			Assert.That(policy.NewMapBlocker, Is.Not.Null);
			Assert.That(policy.ChangeMapBlocker(new GameMap(), false, MapPresence.Unknown), Is.Not.Null);
			Assert.That(policy.DeleteMapBlocker(isCurrent: true), Is.Not.Null);
			Assert.That(policy.DeleteMapBlocker(isCurrent: false), Is.Null);
		}

		[Test]
		public void BlankLocalWorldAllowsFirstObjectAndFirstTag()
		{
			MapPolicy policy = Policy(MapPhase.Local, aligned: false, hasMap: false, empty: true, hasTags: false);
			Assert.That(policy.CanCreateMap, Is.True);
			Assert.That(policy.EditBlocker, Is.Null);
			Assert.That(policy.TagRegistrationBlocker, Is.Null);
			Assert.That(policy.FrameIsTrusted, Is.False);
			Assert.That(policy.NewMapBlocker, Is.Not.Null);
		}

		[TestCase(MapPhase.Local)]
		[TestCase(MapPhase.Hosting)]
		[TestCase(MapPhase.FollowingSession)]
		public void LostAlignmentBlocksAuthoringButAllowsRemovingAMovedTag(MapPhase phase)
		{
			MapPolicy policy = Policy(phase, aligned: false);
			Assert.That(policy.EditBlocker, Is.Not.Null);
			Assert.That(policy.TagRegistrationBlocker, Is.Not.Null);
			Assert.That(policy.TagRemovalBlocker, Is.Null);
			Assert.That(policy.FrameIsTrusted, Is.False);
		}

		[TestCase(MapPhase.AwaitingSessionMap)]
		[TestCase(MapPhase.AdoptingSessionMap)]
		[TestCase(MapPhase.RestoringLocalMap)]
		[TestCase(MapPhase.SwitchingMap)]
		[TestCase(MapPhase.Stopped)]
		public void TransitionBlocksAllTagMutationsEvenWhenAligned(MapPhase phase)
		{
			MapPolicy policy = Policy(phase, hasTags: false);
			Assert.That(policy.TagRegistrationBlocker, Is.Not.Null);
			Assert.That(policy.TagRemovalBlocker, Is.Not.Null);
			Assert.That(policy.TagSizeBlocker, Is.Not.Null);
			Assert.That(policy.RenameBlocker, Is.Not.Null);
			Assert.That(policy.FrameIsTrusted, Is.False);
		}

		[Test]
		public void HostCanEscapeASwitchThatWillNeverAlign()
		{
			var workflow = new MapWorkflow();
			workflow.EnterSession(authority: true);
			workflow.BeginSwitch(now: 10f);
			var target = new GameMap();
			target.tags.Add(new MapTagEntry { id = 1 });
			MapPolicy switching = Policy(workflow.Phase, aligned: false);
			Assert.That(switching.ChangeMapBlocker(target, false, MapPresence.Unknown), Is.Null);
			Assert.That(switching.EditBlocker, Is.Not.Null);
			Assert.That(workflow.SwitchTimedOut(now: 29f, timeout: 20f), Is.False);
			Assert.That(workflow.SwitchTimedOut(now: 30f, timeout: 20f), Is.True);
			workflow.FinishSwitch();
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.Hosting));
			Assert.That(Policy(workflow.Phase, aligned: false).EditBlocker, Is.Not.Null);
		}

		[Test]
		public void PromotedHostTakesOverThePendingMapChangeWithANewTimeout()
		{
			var workflow = new MapWorkflow();
			workflow.EnterSession(authority: false);
			workflow.BeginHosting(changingMap: true, now: 100f);
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.SwitchingMap));
			Assert.That(workflow.SwitchTimedOut(now: 119f, timeout: 20f), Is.False);
			Assert.That(workflow.SwitchTimedOut(now: 120f, timeout: 20f), Is.True);
			workflow.FinishSwitch();
			Assert.That(Policy(workflow.Phase).EditBlocker, Is.Null);
		}

		[Test]
		public void ChangingMapsRespectsRoundsAndRoomPresenceButAllowsDifferentMethods()
		{
			var target = new GameMap();
			Assert.That(Policy(MapPhase.Hosting, playing: true).ChangeMapBlocker(target, false, MapPresence.Unknown),
				Is.EqualTo("Not during a round"));
			Assert.That(Policy(MapPhase.Hosting).ChangeMapBlocker(target, false, MapPresence.Unknown),
				Is.Null);
			Assert.That(Policy(MapPhase.Local).ChangeMapBlocker(target, false, MapPresence.Unknown), Is.Null);
			Assert.That(Policy(MapPhase.Local).ChangeMapBlocker(target, false, MapPresence.Elsewhere), Is.Not.Null);
		}

		[Test]
		public void FirstTagPromptWaitsUntilTheMapChangeIsComplete()
		{
			Assert.That(Policy(MapPhase.Hosting, hasTags: false).NeedsFirstTag, Is.True);
			Assert.That(Policy(MapPhase.FollowingSession, hasTags: false).NeedsFirstTag, Is.True);
			Assert.That(Policy(MapPhase.SwitchingMap, hasTags: false).NeedsFirstTag, Is.False);
			MapPolicy held = Policy(MapPhase.FollowingSession, hasTags: false, holding: true);
			Assert.That(held.NeedsFirstTag, Is.False);
			Assert.That(held.EditBlocker, Is.Not.Null);
			Assert.That(held.FrameIsTrusted, Is.False);
			Assert.That(Policy(MapPhase.Local, hasTags: false).NeedsFirstTag, Is.False);
		}

		[Test]
		public void AdoptionRemainsPendingUntilExplicitlyCompleted()
		{
			var workflow = new MapWorkflow();
			workflow.EnterSession(authority: false);
			var incoming = new GameMap();
			workflow.BeginAdoption(incoming, changesFrame: true);
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.AdoptingSessionMap));
			Assert.That(workflow.IncomingMap, Is.SameAs(incoming));
			Assert.That(workflow.AdoptionChangesFrame, Is.True);
			Assert.That(Policy(workflow.Phase).CanRecordReferences, Is.False);

			workflow.FinishAdoption();
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.FollowingSession));
			Assert.That(workflow.IncomingMap, Is.Null);
			Assert.That(workflow.AdoptionChangesFrame, Is.False);
		}

		[Test]
		public void RetryingContentAdoptionKeepsTheEstablishedFrameUntilTheHostChangesMaps()
		{
			var workflow = new MapWorkflow();
			workflow.EnterSession(authority: false);
			workflow.BeginAdoption(new GameMap(), changesFrame: true);
			Assert.That(workflow.HasSessionFrame, Is.False);
			workflow.FinishAdoption();
			workflow.BeginAdoption(new GameMap(), changesFrame: false);
			Assert.That(workflow.HasSessionFrame, Is.True);

			// A newer revision can supersede a failed write without rebasing the same map.
			workflow.BeginAdoption(new GameMap(), changesFrame: !workflow.HasSessionFrame);
			Assert.That(workflow.AdoptionChangesFrame, Is.False);
			workflow.AwaitSessionMap();
			Assert.That(workflow.HasSessionFrame, Is.False);
			Assert.That(workflow.IncomingMap, Is.Null);
		}

		[Test]
		public void NewSessionOrDisconnectDiscardsObsoleteAdoption()
		{
			var workflow = new MapWorkflow();
			workflow.BeginAdoption(new GameMap(), changesFrame: true);
			workflow.BeginRestore();
			Assert.That(workflow.IncomingMap, Is.Null);
			workflow.BeginAdoption(new GameMap(), changesFrame: false);
			workflow.EnterSession(authority: true);
			Assert.That(workflow.IncomingMap, Is.Null);
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.Hosting));
		}

		[Test]
		public void OldDisconnectCannotRestoreOverANewSessionOrNewerDisconnect()
		{
			var workflow = new MapWorkflow();
			int oldOperation = workflow.BeginRestore();
			Assert.That(workflow.IsCurrentRestore(oldOperation), Is.True);
			workflow.EnterSession(authority: false);
			Assert.That(workflow.IsCurrentRestore(oldOperation), Is.False);
			int newOperation = workflow.BeginRestore();
			Assert.That(workflow.IsCurrentRestore(oldOperation), Is.False);
			Assert.That(workflow.IsCurrentRestore(newOperation), Is.True);
			workflow.Stop();
			Assert.That(workflow.IsCurrentRestore(newOperation), Is.False);
		}

		[Test]
		public void InvalidAdoptionDoesNotChangeTheCurrentWorkflow()
		{
			var workflow = new MapWorkflow();
			Assert.Throws<ArgumentNullException>(() => workflow.BeginAdoption(null, changesFrame: false));
			Assert.That(workflow.Phase, Is.EqualTo(MapPhase.Local));
			Assert.Throws<InvalidOperationException>(() => workflow.FinishAdoption());
		}
	}
}
