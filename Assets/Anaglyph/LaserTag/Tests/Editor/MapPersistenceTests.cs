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
		private string directory;
		private MapStore store;
		private MapManager manager;

		[SetUp]
		public void SetUp()
		{
			directory = Path.Combine(Path.GetTempPath(), "lasertag-map-tests-" + Guid.NewGuid().ToString("N"));
			store = new MapStore(directory);
			manager = new MapManager(store);
			manager.Create();
		}
		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(directory)) Directory.Delete(directory, true);
		}
		private static MapObjectEntry Placement(float x) => new()
		{
			prefabId = "Base", pose = new Pose(new Vector3(x, 0, 0), Quaternion.identity)
		};

		[TestCase(ColocationManager.ColocationMethod.MetaSharedAnchor)]
		[TestCase(ColocationManager.ColocationMethod.AprilTag)]
		public void PreferenceSurvivesCloningSavingAndReopening(ColocationManager.ColocationMethod preference)
		{
			manager.SetTags(new[] { new MapTagEntry { id = 7, canonPose = Pose.identity } }, 10f);
			manager.SetPreferredColocationMethod(preference);
			Assert.That(manager.CurrentMap.Clone().preferredColocationMethod, Is.EqualTo(preference));
			Assert.That(manager.Save(), Is.True);
			Assert.That(new MapStore(directory).TryGet(manager.CurrentId, out GameMap loaded), Is.True);
			Assert.That(loaded.preferredColocationMethod, Is.EqualTo(preference));
		}

		[Test]
		public void PreferenceIsVersionedContentEvenWithoutRegisteredTags()
		{
			manager.Save(true);
			string revision = manager.CurrentMap.version;
			Assert.That(manager.SetPreferredColocationMethod(ColocationManager.ColocationMethod.AprilTag), Is.True);
			Assert.That(manager.CurrentMap.version, Is.Not.EqualTo(revision));
			Assert.That(manager.CurrentMap.dirty, Is.True);
			revision = manager.CurrentMap.version;
			Assert.That(manager.SetPreferredColocationMethod(ColocationManager.ColocationMethod.AprilTag), Is.False);
			Assert.That(manager.SetPreferredColocationMethod((ColocationManager.ColocationMethod)99), Is.False);
			Assert.That(manager.CurrentMap.version, Is.EqualTo(revision));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void LegacyMapsInferPreferenceFromRegisteredTags(bool hasTags)
		{
			if (hasTags) manager.SetTags(new[] { new MapTagEntry { id = 7, canonPose = Pose.identity } }, 10f);
			manager.Save();
			string path = Path.Combine(directory, manager.CurrentId + ".json");
			string json = File.ReadAllText(path).Replace("preferredColocationMethod", "unusedLegacyField");
			File.WriteAllText(path, json);
			Assert.That(new MapStore(directory).TryGet(manager.CurrentId, out GameMap loaded), Is.True);
			Assert.That(loaded.preferredColocationMethod, Is.EqualTo(hasTags
				? ColocationManager.ColocationMethod.AprilTag : ColocationManager.ColocationMethod.MetaSharedAnchor));
		}

		[Test]
		public void DocumentAndCatalogReadsAreDetached()
		{
			manager.SetObjects(new[] { Placement(1) });
			GameMap snapshot = manager.CurrentMap;
			snapshot.objects.Clear();
			Assert.That(manager.CurrentMap.objects.Count, Is.EqualTo(1));
			Assert.That(manager.Save(), Is.True);
			store.TryGet(manager.CurrentId, out GameMap loaded);
			loaded.objects.Clear();
			store.TryGet(manager.CurrentId, out GameMap again);
			Assert.That(again.objects.Count, Is.EqualTo(1));
			manager.Load(again);
			again.objects.Clear();
			Assert.That(manager.CurrentMap.objects.Count, Is.EqualTo(1));
		}

		[Test]
		public void SaveAndAnchorMaintenanceDoNotMintContentVersions()
		{
			manager.SetObjects(new[] { Placement(1) });
			manager.Save(true);
			string revision = manager.CurrentMap.version;
			manager.SetAnchors(new[] { new MapAnchorEntry { guid = Guid.NewGuid().ToString("N"), tagId = 4, canonPose = Pose.identity } });
			manager.Save();
			manager.MarkUsed();
			manager.Save();
			Assert.That(manager.CurrentMap.version, Is.EqualTo(revision));
			Assert.That(manager.CurrentMap.dirty, Is.False);
		}

		[Test]
		public void IdenticalContentDoesNotCreateAnotherRevision()
		{
			manager.SetObjects(new[] { Placement(1) });
			string revision = manager.CurrentMap.version;
			Assert.That(manager.SetObjects(new[] { Placement(1) }), Is.False);
			manager.Save();
			manager.Save();
			Assert.That(manager.CurrentMap.version, Is.EqualTo(revision));
			manager.SetObjects(new[] { Placement(2) });
			Assert.That(manager.CurrentMap.version, Is.Not.EqualTo(revision));
		}

		[Test]
		public void JoiningTheAlreadyLoadedMapPreservesLocalEditsAndAcceptsEmptyContent()
		{
			manager.SetObjects(new[] { Placement(3) });
			manager.SetTags(new[] { new MapTagEntry { id = 7, canonPose = Pose.identity } }, 16f);
			GameMap received = manager.CurrentMap;
			received.objects.Clear();
			received.version = Guid.NewGuid().ToString("N");
			Assert.That(manager.TryAdopt(received), Is.True);
			Assert.That(manager.CurrentMap.objects, Is.Empty);
			Assert.That(manager.CurrentMap.dirty, Is.False);
			GameMap fork = store.Maps.Single(map => map.id != received.id);
			Assert.That(fork.objects[0].pose.position.x, Is.EqualTo(3f));
			Assert.That(fork.tags[0].id, Is.EqualTo(7));
			Assert.That(fork.tagSizeCm, Is.EqualTo(16f));
			Assert.That(fork.dirty, Is.True);
			var reopened = new MapStore(directory);
			Assert.That(reopened.TryGet(received.id, out GameMap empty), Is.True);
			Assert.That(empty.objects, Is.Empty);
		}

		[Test]
		public void JoiningAnUnloadedConflictingMapAlsoPreservesItsEdits()
		{
			manager.SetObjects(new[] { Placement(4) });
			manager.Save();
			GameMap received = manager.CurrentMap;
			received.version = Guid.NewGuid().ToString("N");
			received.objects.Clear();
			manager.Create();
			Assert.That(manager.TryAdopt(received), Is.True);
			Assert.That(store.Maps.Single(map => map.id != received.id).objects.Count, Is.EqualTo(1));
		}

		[Test]
		public void FailedAdoptionCanRetryWithoutDuplicatingThePreservedFork()
		{
			manager.SetObjects(new[] { Placement(8) });
			manager.Save();
			GameMap incoming = manager.CurrentMap;
			string localVersion = incoming.version;
			incoming.version = Guid.NewGuid().ToString("N");
			incoming.objects.Clear();
			string blockedPath = Path.Combine(directory, manager.CurrentId + ".json.tmp");
			Directory.CreateDirectory(blockedPath);
			LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(manager.TryAdopt(incoming), Is.False);
			Assert.That(manager.CurrentMap.version, Is.EqualTo(localVersion));
			Assert.That(store.Maps.Count, Is.EqualTo(2));
			Directory.Delete(blockedPath);
			Assert.That(manager.TryAdopt(incoming), Is.True);
			Assert.That(store.Maps.Count, Is.EqualTo(2));
			Assert.That(manager.CurrentMap.objects, Is.Empty);
		}

		[Test]
		public void FailedSaveKeepsDirtyDocumentAndCommittedCatalog()
		{
			manager.Rename("Before");
			Assert.That(manager.Save(true), Is.True);
			manager.Rename("After");
			Directory.CreateDirectory(Path.Combine(directory, manager.CurrentId + ".json.tmp"));
			LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(manager.Save(true), Is.False);
			Assert.That(manager.CurrentMap.dirty, Is.True);
			store.TryGet(manager.CurrentId, out GameMap saved);
			Assert.That(saved.name, Is.EqualTo("Before"));
		}

		[Test]
		public void FailedDeleteDoesNotReleaseAnchorsOrRemoveCatalogEntry()
		{
			manager.SetAnchors(new[] { new MapAnchorEntry { guid = Guid.NewGuid().ToString("N"), tagId = -1 } });
			manager.Save();
			Directory.CreateDirectory(Path.Combine(directory, manager.CurrentId + ".json.tmp"));
			List<string> orphans = new();
			int changed = 0;
			store.Changed += () => changed++;
			LogAssert.Expect(LogType.Exception, new Regex("[\\s\\S]*"));
			Assert.That(store.Delete(manager.CurrentId, orphans), Is.False);
			Assert.That(orphans, Is.Empty);
			Assert.That(changed, Is.Zero);
			Assert.That(store.TryGet(manager.CurrentId, out _), Is.True);
		}

		[Test]
		public void DeletingOneOfTwoMapsKeepsSharedAnchorSave()
		{
			string anchor = Guid.NewGuid().ToString("N");
			manager.SetAnchors(new[] { new MapAnchorEntry { guid = anchor, tagId = -1 } });
			manager.Save();
			string first = manager.CurrentId;
			GameMap second = manager.CurrentMap;
			second.id = Guid.NewGuid().ToString("N");
			store.Save(second);
			List<string> orphans = new();
			Assert.That(store.Delete(first, orphans), Is.True);
			Assert.That(orphans, Is.Empty);
			Assert.That(store.Delete(second.id, orphans), Is.True);
			Assert.That(orphans, Is.EquivalentTo(new[] { anchor }));
		}

		[Test]
		public void LegacyFlatJsonAndRecoveryBackupRemainReadable()
		{
			manager.SetObjects(new[] { Placement(5) });
			manager.Save();
			string path = Path.Combine(directory, manager.CurrentId + ".json");
			string legacy = File.ReadAllText(path).Replace("\"dirty\"", "\"baseVersion\": \"unused-legacy-value\", \"dirty\"");
			File.WriteAllText(path, legacy);
			var legacyStore = new MapStore(directory);
			Assert.That(legacyStore.TryGet(manager.CurrentId, out GameMap loaded), Is.True);
			Assert.That(loaded.objects[0].pose.position.x, Is.EqualTo(5f));
			manager.Rename("Updated");
			manager.Save();
			File.WriteAllText(path, "broken JSON");
			LogAssert.Expect(LogType.Warning, new Regex("Recovered map"));
			Assert.That(new MapStore(directory).TryGet(manager.CurrentId, out loaded), Is.True);
			Assert.That(loaded.objects.Count, Is.EqualTo(1));
		}
	}
}
