using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Anaglyph.LaserTag.Tests
{
	public class MapPersistenceTests
	{
		private string directory, mapDirectory, spaceDirectory;
		private MapStore maps;
		private MapSpaceStore spaces;
		private MapCatalogJournal catalog;
		private MapSpace space;
		private GameMap map;
		[SetUp] public void SetUp()
		{
			directory = Path.Combine(Path.GetTempPath(), "lasertag-space-tests-" + Guid.NewGuid().ToString("N"));
			mapDirectory = Path.Combine(directory, "maps"); spaceDirectory = Path.Combine(directory, "spaces");
			maps = new(mapDirectory); spaces = new(spaceDirectory); catalog = new(maps, spaces, directory);
			space = MapSpace.Create("Room"); map = new() { id = Id(), version = Id(), storageFrameId = space.storageFrameId, name = "Layout", objects = new() { Placement(3) } };
			space.mapIds.Add(map.id);
		}
		[TearDown] public void TearDown() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
		private static string Id() => Guid.NewGuid().ToString("N");
		private static MapObjectEntry Placement(float x) => new() { prefabId = "Base", pose = new(new Vector3(x, 0, 0), Quaternion.identity) };
		private void Save() => Assert.That(catalog.Commit(space, new[] { map }), Is.True);
		private string MapPath => Path.Combine(mapDirectory, map.id + ".json");
		private static void SamePose(Pose a, Pose b) { Assert.That(Vector3.Distance(a.position, b.position), Is.LessThan(.0001f)); Assert.That(Quaternion.Angle(a.rotation, b.rotation), Is.LessThan(.01f)); }

		[Test] public void LayoutFilesContainNoReferencesAndSpaceFilesContainOnlyMapIds()
		{
			space.SetTag(7, Pose.identity); space.SetAnchorWithTag(Id(), Pose.identity, -1); Save();
			string layout = File.ReadAllText(MapPath), room = File.ReadAllText(Path.Combine(spaceDirectory, space.id + ".json"));
			Assert.That(layout, Does.Not.Contain("anchors").And.Not.Contain("tags").And.Not.Contain("preferredColocationMethod"));
			Assert.That(room, Does.Contain(map.id).And.Not.Contain("prefabId"));
			Assert.That(new MapStore(mapDirectory).TryGet(map.id, out var reopened), Is.True);
			Assert.That(reopened.objects.Count, Is.EqualTo(1));
		}
		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag)]
		[TestCase(ColocationManager.ColocationMethod.TwoAprilTags)]
		[TestCase(ColocationManager.ColocationMethod.SystemDetermined)]
		public void SpaceConfigurationSurvivesSeparateJsonRoundTrip(ColocationManager.ColocationMethod method)
		{
			space.preferredColocationMethod = method; space.tagSizeCm = 18; space.firstTagId = 7; space.secondTagId = 42;
			space.SetTag(7, Pose.identity); Save();
			Assert.That(new MapSpaceStore(spaceDirectory).TryGet(space.id, out var reopened), Is.True);
			Assert.That(reopened.preferredColocationMethod, Is.EqualTo(method)); Assert.That(reopened.tagSizeCm, Is.EqualTo(18));
			Assert.That(reopened.tags.Count, Is.EqualTo(1)); Assert.That(reopened.secondTagId, Is.EqualTo(42));
		}
		[Test] public void StoreAndManagerReadsAreDetached()
		{
			Save(); var manager = new MapWorkingCopy(maps); manager.Load(map); manager.CurrentMap.objects.Clear();
			Assert.That(manager.CurrentMap.objects.Count, Is.EqualTo(1));
			spaces.TryGet(space.id, out var read); read.mapIds.Clear(); spaces.TryGet(space.id, out read);
			Assert.That(read.mapIds, Does.Contain(map.id));
		}
		[Test] public void RebaseChangesOneSpaceFileAndProjectsObjectsTagsAndAnchorsTogether()
		{
			space.SetTag(7, Placement(3).pose); string anchor = Id(); space.SetAnchorWithTag(anchor, Placement(3).pose, -1); Save();
			string original = File.ReadAllText(MapPath); var offset = new Pose(new Vector3(8, 1, -2), Quaternion.Euler(0, 80, 0));
			var rebased = MapSpaceReconciler.Rebase(space, Id(), offset); Assert.That(spaces.Save(rebased), Is.True);
			Assert.That(File.ReadAllText(MapPath), Is.EqualTo(original)); Assert.That(rebased.tags[0].canonPose, Is.EqualTo(space.tags[0].canonPose));
			var projected = rebased.Frame.ToCanonical(map.objects[0].pose);
			SamePose(projected, rebased.Frame.ToCanonical(rebased.tags[0].canonPose)); SamePose(projected, rebased.Frame.ToCanonical(rebased.anchors[0].canonPose));
			SamePose(map.objects[0].pose, rebased.Frame.ToStorage(projected));
			Assert.That(rebased.anchors[0].guid, Is.EqualTo(anchor)); Assert.That(rebased.storageFrameId, Is.EqualTo(space.storageFrameId));
		}
		[Test] public void UndoRestoresPreviousOffsetAndFrameAfterMultipleRebases()
		{
			var first = MapSpaceReconciler.Rebase(space, Id(), new Pose(Vector3.right, Quaternion.Euler(0, 20, 0)));
			var second = MapSpaceReconciler.Rebase(first, Id(), new Pose(Vector3.forward * 4, Quaternion.Euler(0, -30, 0)));
			var undo = MapSpaceReconciler.Undo(second); SamePose(undo.canonicalFromStorage, first.canonicalFromStorage);
			Assert.That(undo.canonicalFrameId, Is.EqualTo(first.canonicalFrameId));
			Assert.That(MapSpaceReconciler.TryKnownOffset(second, first.canonicalFrameId, out var remembered), Is.True); SamePose(remembered, first.canonicalFromStorage);
		}
		[Test] public void SameFrameRebaseIsIdempotentAndRejectsTiltOrInvalidRotations()
		{
			var rebased = MapSpaceReconciler.Rebase(space, Id(), new Pose(Vector3.one, Quaternion.Euler(0, 40, 0)));
			Assert.That(MapSpaceReconciler.Rebase(rebased, rebased.canonicalFrameId, rebased.canonicalFromStorage).frameRevision, Is.EqualTo(rebased.frameRevision));
			Assert.Throws<ArgumentException>(() => MapSpaceReconciler.Rebase(space, Id(), new Pose(Vector3.zero, Quaternion.Euler(20, 0, 0))));
			Assert.Throws<ArgumentException>(() => MapSpaceReconciler.Rebase(space, Id(), new Pose(Vector3.zero, new Quaternion())));
		}
		[Test] public void AdoptionRetainsConflictingLocalDefinitionsAndStoresOneForeignAssociation()
		{
			space.SetTag(7, Placement(3).pose); space.SetAnchorWithTag(Id(), Placement(3).pose, -1);
			var remote = MapSpace.Create("Host"); remote.SetTag(7, Pose.identity); remote.tagSizeCm = 22;
			var offset = new Pose(Vector3.right * 8, Quaternion.Euler(0, 90, 0));
			var adopted = MapSpaceReconciler.ImportReferences(space, remote, offset);
			Assert.That(adopted.name, Is.EqualTo("Room")); Assert.That(adopted.retainedReferences.Count, Is.EqualTo(1));
			Assert.That(adopted.retainedReferences[0].tags[0].canonPose, Is.EqualTo(Placement(3).pose));
			SamePose(adopted.Frame.ToCanonical(adopted.tags[0].canonPose), Pose.identity);
			var again = MapSpaceReconciler.ImportReferences(adopted, remote, offset);
			Assert.That(again.id, Is.EqualTo(space.id)); Assert.That(again.associations.Count, Is.EqualTo(1)); Assert.That(again.retainedReferences.Count, Is.EqualTo(1));
		}
		[Test] public void AdoptionReplacesLocalPendingAnchorSetupWithTheHostsAlignmentIntent()
		{
			space.hasPendingSetup = true; space.initializationPending = true;
			space.pendingSetupMethod = ColocationManager.ColocationMethod.MetaSharedAnchor;
			var remote = MapSpace.Create("Host"); remote.SetTag(7, Pose.identity);
			remote.preferredColocationMethod = ColocationManager.ColocationMethod.AprilTag;
			var adopted = MapSpaceReconciler.ImportReferences(space, remote, Pose.identity);
			Assert.That(adopted.hasPendingSetup, Is.False);
			Assert.That(adopted.initializationPending, Is.False);
			Assert.That(adopted.preferredColocationMethod, Is.EqualTo(remote.preferredColocationMethod));
			Assert.That(adopted.id, Is.EqualTo(space.id));
			remote.hasPendingSetup = true; remote.pendingSetupMethod = ColocationManager.ColocationMethod.AprilTag;
			remote.pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget;
			adopted = MapSpaceReconciler.ImportReferences(space, remote, Pose.identity);
			Assert.That(adopted.hasPendingSetup, Is.True);
			Assert.That(adopted.pendingSetupMethod, Is.EqualTo(remote.pendingSetupMethod));
			Assert.That(adopted.pendingSetupIntent, Is.EqualTo(remote.pendingSetupIntent));
		}
		[Test] public void MapImportInverseProjectsOnlyIncomingLayoutAndForksLocalEdits()
		{
			map.dirty = true; var remote = map.Clone(); remote.storageFrameId = space.canonicalFrameId; remote.version = Id(); remote.objects.Clear();
			var writes = MapSpaceReconciler.PrepareMapImport(space, remote, map);
			Assert.That(writes.Count, Is.EqualTo(2)); Assert.That(writes[0].objects.Count, Is.EqualTo(1)); Assert.That(writes[0].dirty, Is.True);
			Assert.That(writes[1].objects, Is.Empty); Assert.That(writes[1].dirty, Is.False); Assert.That(writes[1].id, Is.EqualTo(map.id));
			Assert.That(space.mapIds.Count, Is.EqualTo(2));
		}
		[Test] public void JournalRecoversFailedMultiFileImportWithoutDuplicatingFork()
		{
			Save(); map.dirty = true; maps.Save(map); var remote = map.Clone(); remote.version = Id(); remote.objects.Clear();
			var writes = MapSpaceReconciler.PrepareMapImport(space, remote, map);
			Directory.CreateDirectory(MapPath + ".tmp"); LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(catalog.Commit(space, writes), Is.False); Assert.That(catalog.HasPending, Is.True);
			Directory.Delete(MapPath + ".tmp"); var reopenedMaps = new MapStore(mapDirectory); var reopenedSpaces = new MapSpaceStore(spaceDirectory);
			var recovered = new MapCatalogJournal(reopenedMaps, reopenedSpaces, directory); Assert.That(recovered.Recover(), Is.True);
			Assert.That(reopenedMaps.Maps.Count, Is.EqualTo(2)); Assert.That(recovered.Recover(), Is.True);
			reopenedMaps.TryGet(map.id, out var result); Assert.That(result.objects, Is.Empty);
		}
		[Test] public void FailedJournalWriteRetriesTheSameCatalogOperation()
		{
			Directory.CreateDirectory(directory);
			string blocked = Path.Combine(directory, "pending-catalog-operation.json.tmp"); Directory.CreateDirectory(blocked);
			LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(catalog.Commit(space, new[] { map }), Is.False); Assert.That(catalog.HasPending, Is.True);
			Assert.That(maps.Maps, Is.Empty);
			Directory.Delete(blocked); Assert.That(catalog.Recover(), Is.True);
			Assert.That(maps.Maps.Count, Is.EqualTo(1)); Assert.That(spaces.Spaces.Count, Is.EqualTo(1));
		}
		[Test] public void SpaceDeletionKeepsAnAnchorOwnedByAnotherSpace()
		{
			string anchor = Id(); space.SetAnchorWithTag(anchor, Pose.identity, -1); Save();
			var fork = MapSpace.Create("Other"); fork.SetAnchorWithTag(anchor, Pose.identity, -1); Assert.That(spaces.Save(fork), Is.True);
			space.mapIds.Clear(); Assert.That(catalog.Commit(space, null, new[] { map.id }, true), Is.True);
			Assert.That(maps.Maps, Is.Empty); Assert.That(spaces.IsAnchorReferenced(anchor), Is.True);
			Assert.That(spaces.Delete(fork.id), Is.True); Assert.That(spaces.IsAnchorReferenced(anchor), Is.False);
		}
		[Test] public void RestartKeepsSetupIntentWithoutPersistingValidationEvidence()
		{
			space.hasPendingSetup = true; space.pendingSetupMethod = ColocationManager.ColocationMethod.AprilTag;
			space.pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget; Save();
			Assert.That(new MapSpaceStore(spaceDirectory).TryGet(space.id, out var loaded), Is.True);
			Assert.That(loaded.hasPendingSetup, Is.True); Assert.That(loaded.pendingSetupMethod, Is.EqualTo(ColocationManager.ColocationMethod.AprilTag));
			Assert.That(File.ReadAllText(Path.Combine(spaceDirectory, space.id + ".json")), Does.Not.Contain("TrackingGeneration").And.Not.Contain("Phase"));
		}

		[Test] public void ReferenceCommitPreservesMapsAndNamesEditedWhileSetupIsPending()
		{
			var candidate = space.Clone(); candidate.SetTag(9, Pose.identity);
			string added = Id(); space.mapIds.Add(added); space.name = "Renamed during setup";
			var committed = space.ApplyReferenceCandidate(candidate);
			Assert.That(committed.mapIds, Does.Contain(added)); Assert.That(committed.name, Is.EqualTo(space.name));
			Assert.That(committed.tags[0].id, Is.EqualTo(9)); Assert.That(space.tags, Is.Empty);
			candidate.frameRevision++;
			Assert.Throws<InvalidOperationException>(() => space.ApplyReferenceCandidate(candidate));
		}

		[Test] public void MembershipPrunesMissingInvalidAndDuplicateEntriesAfterRecovery()
		{
			Save(); space.mapIds.Add(map.id); space.mapIds.Add(Id()); space.mapIds.Add("../bad"); Assert.That(spaces.Save(space), Is.True);
			Assert.That(spaces.PruneMembership(maps), Is.True); spaces.TryGet(space.id, out var pruned);
			Assert.That(pruned.mapIds, Is.EqualTo(new[] { map.id })); Assert.That(File.Exists(MapPath), Is.True);
		}
		[Test] public void MembershipRecoveryUsesBackupBeforePruningAndLeavesOrphanFilesAlone()
		{
			Save(); map.name = "Changed"; maps.Save(map); File.WriteAllText(MapPath, "broken JSON");
			var orphan = map.Clone(); orphan.id = Id(); maps.Save(orphan);
			var reopened = new MapStore(mapDirectory); Assert.That(spaces.PruneMembership(reopened), Is.True);
			spaces.TryGet(space.id, out var pruned); Assert.That(pruned.mapIds, Does.Contain(map.id)); Assert.That(reopened.TryGet(orphan.id, out _), Is.True);
		}
		[Test] public void OneLayoutCannotAcquireASecondParent()
		{
			Save(); var other = MapSpace.Create("Other"); other.storageFrameId = space.storageFrameId; other.mapIds.Add(map.id);
			Assert.That(catalog.Commit(other, new[] { map }), Is.False);
		}
		[Test] public void DeletingLayoutDoesNotDeleteSpaceReferences()
		{
			string anchor = Id(); space.SetAnchorWithTag(anchor, Pose.identity, -1); Save(); space.mapIds.Clear();
			Assert.That(catalog.Commit(space, deletions: new[] { map.id }), Is.True);
			Assert.That(maps.TryGet(map.id, out _), Is.False); Assert.That(spaces.IsAnchorReferenced(anchor), Is.True);
		}
		[Test] public void LegacyJsonIsNotAdoptedIntoTheNewCatalog()
		{
			Save(); var json = File.ReadAllText(MapPath).Replace("schemaVersion", "legacyVersion"); File.WriteAllText(MapPath, json);
			Assert.That(new MapStore(mapDirectory).Read(map.id, out _), Is.EqualTo(DocumentReadStatus.Invalid));
		}
		[Test] public void FailedSaveKeepsDirtyDocumentAndLastCommittedCatalog()
		{
			Save(); var manager = new MapWorkingCopy(maps); manager.Load(map); manager.Rename("Changed");
			Directory.CreateDirectory(MapPath + ".tmp"); LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(manager.Save(true), Is.False); Assert.That(manager.CurrentMap.dirty, Is.True);
			maps.TryGet(map.id, out var previous); Assert.That(previous.name, Is.EqualTo("Layout"));
		}
		[Test] public void UnchangedSaveAndSmallRoundTripErrorDoNotCreateContentRevisions()
		{
			Save(); var manager = new MapWorkingCopy(maps); manager.Load(map); var slight = Placement(3.000001f);
			Assert.That(manager.SetObjects(new[] { slight }), Is.False); Assert.That(manager.Save(), Is.True);
			Assert.That(File.Exists(MapPath + ".bak"), Is.False); Assert.That(manager.CurrentMap.version, Is.EqualTo(map.version));
		}
		[Test] public void PrivateTagAnchorsRequireMatchingDefinitionAndSize()
		{
			space.SetTag(7, Placement(3).pose); var privateAnchor = new MapAnchorEntry { guid = Id(), tagId = 7, canonPose = Placement(3.04f).pose, tagCanonPose = Placement(3).pose, tagSizeCm = 10 };
			Assert.That(space.IsCompatibleLocalAnchor(privateAnchor), Is.True); space.SetTag(7, Pose.identity);
			Assert.That(space.IsCompatibleLocalAnchor(privateAnchor), Is.False); space.SetTag(7, Placement(3).pose); space.tagSizeCm = 12;
			Assert.That(space.IsCompatibleLocalAnchor(privateAnchor), Is.False);
		}
	}
}
