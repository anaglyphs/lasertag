using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Anaglyph.LaserTag.Maps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Tests
{
	public class SpaceStoreTests
	{
		private string root;
		private SpaceStore store;

		[SetUp]
		public void SetUp()
		{
			root = Path.Combine(Path.GetTempPath(), "lasertag-spaces-" + Guid.NewGuid().ToString("N"));
			store = new SpaceStore(Path.Combine(root, SpaceStore.CatalogFolder));
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}

		private static Space Sample()
		{
			Space space = Space.Create("Living room");
			space.alignment.method = Method.AprilTag;
			space.alignment.tagSizeCm = 12.5f;
			space.alignment.firstTagId = 2;
			space.alignment.secondTagId = 5;
			space.SetTag(2, new Pose(new Vector3(1, 1.5f, -2), Quaternion.Euler(0, 90, 0)));
			space.SetTag(5, new Pose(new Vector3(-1, 1.4f, 3), Quaternion.Euler(0, -45, 0)));
			space.SetAnchor(Space.NewId(), new Pose(new Vector3(0.5f, 0, 0.5f), Quaternion.identity));
			space.SetAnchor(Space.NewId(), new Pose(new Vector3(4, 0, 1), Quaternion.Euler(0, 10, 0)), tagId: 2);
			Map map = Map.Create("Arena");
			map.objects.Add(new MapObjectRecord { prefabId = "Base", pose = new Pose(new Vector3(2, 0, 2), Quaternion.Euler(0, 180, 0)) });
			map.objects.Add(new MapObjectRecord { prefabId = "Flag", pose = new Pose(new Vector3(-2, 0, 2), Quaternion.identity) });
			space.maps.Add(map);
			space.maps.Add(Map.Create("Empty"));
			return space;
		}

		[Test]
		public void SpacesRoundTripThroughTheStore()
		{
			Space first = Sample();
			Space second = Sample();
			second.lastUsed = first.lastUsed + 10;
			Assert.That(store.Save(first), Is.True);
			Assert.That(store.Save(second), Is.True);
			Assert.That(File.Exists(store.SpacePath(first.id) + ".tmp"), Is.False, "the temp file is replaced, not left behind");

			List<Space> loaded = new SpaceStore(store.Directory).LoadAll();
			Assert.That(loaded.Count, Is.EqualTo(2));
			Assert.That(loaded[0].id, Is.EqualTo(second.id), "most recently used first");
			Assert.That(JsonUtility.ToJson(loaded[1]), Is.EqualTo(JsonUtility.ToJson(first)));
			Assert.That(loaded[1].maps[0].objects.Count, Is.EqualTo(2));
			Assert.That(loaded[1].alignment.HasPair, Is.True);

			first.name = "Renamed";
			Assert.That(store.Save(first), Is.True);
			Assert.That(new SpaceStore(store.Directory).LoadAll().Find(s => s.id == first.id).name, Is.EqualTo("Renamed"));

			Assert.That(store.Delete(first.id), Is.True);
			Assert.That(new SpaceStore(store.Directory).LoadAll().Count, Is.EqualTo(1));
		}

		[Test]
		public void PrivateReferencesRoundTripAndAreKeyedByTheFrame()
		{
			Space space = Sample();
			PrivateReferences references = PrivateReferences.Create(space);
			references.tagAnchors.Add(new TagAnchorRef { guid = Space.NewId(), tagId = 2, pose = new Pose(Vector3.one, Quaternion.identity),
				tagSizeCm = 12.5f, tagPose = space.tags[0].pose });
			Assert.That(store.LoadPrivate(space.id), Is.Null);
			Assert.That(store.SavePrivate(references), Is.True);
			PrivateReferences loaded = store.LoadPrivate(space.id);
			Assert.That(loaded, Is.Not.Null);
			Assert.That(loaded.Matches(space), Is.True);
			Assert.That(loaded.tagAnchors.Count, Is.EqualTo(1));
			space.frameId = Space.NewId();
			Assert.That(loaded.Matches(space), Is.False, "a new frame invalidates the device's realizations");
			Assert.That(store.Delete(space.id), Is.True);
			Assert.That(store.LoadPrivate(space.id), Is.Null);
		}

		[Test]
		public void BadEntriesAreDroppedWithoutRejectingTheSpace()
		{
			Space space = Sample();
			// one tag with a negative id, one object with a non-unit rotation, one anchor with a bad guid,
			// an unknown method and a negative tag size; the pair itself stays valid
			TagRef badTag = space.tags[1]; badTag.id = -5; space.tags[1] = badTag;
			MapObjectRecord badObject = space.maps[0].objects[1]; badObject.pose.rotation = new Quaternion(0, 0, 0, 3f); space.maps[0].objects[1] = badObject;
			AnchorRef badAnchor = space.anchors[0]; badAnchor.guid = "not-a-guid"; space.anchors[0] = badAnchor;
			space.alignment.method = (Method)99;
			space.alignment.tagSizeCm = -3f;
			Directory.CreateDirectory(store.Directory);
			File.WriteAllText(store.SpacePath(space.id), JsonUtility.ToJson(space, true));

			// expected in the order Sanitize checks: settings, tags, anchors, then each map's objects
			LogAssert.Expect(LogType.Warning, new Regex("unknown alignment method"));
			LogAssert.Expect(LogType.Warning, new Regex("invalid tag size"));
			LogAssert.Expect(LogType.Warning, new Regex("dropped tag"));
			LogAssert.Expect(LogType.Warning, new Regex("dropped anchor"));
			LogAssert.Expect(LogType.Warning, new Regex("dropped object"));
			List<Space> loaded = store.LoadAll();
			Assert.That(loaded.Count, Is.EqualTo(1));
			Space result = loaded[0];
			Assert.That(result.tags.Count, Is.EqualTo(1));
			Assert.That(result.tags[0].id, Is.EqualTo(2));
			Assert.That(result.anchors.Count, Is.EqualTo(1));
			Assert.That(result.anchors[0].tagId, Is.EqualTo(2));
			Assert.That(result.maps.Count, Is.EqualTo(2));
			Assert.That(result.maps[0].objects.Count, Is.EqualTo(1));
			Assert.That(result.maps[0].objects[0].prefabId, Is.EqualTo("Base"));
			Assert.That(result.alignment.method, Is.EqualTo(Method.Visual));
			Assert.That(result.alignment.tagSizeCm, Is.EqualTo(Space.DefaultTagSizeCm));
			Assert.That(result.alignment.firstTagId, Is.EqualTo(2));
			Assert.That(result.alignment.secondTagId, Is.EqualTo(5));
		}

		[Test]
		public void LegacyMethodsMigrateOnLoad()
		{
			foreach ((int stored, Method expected) in new[] { (2, Method.AprilTag), (3, Method.AprilTag), (4, Method.Visual), (0, Method.MetaSharedAnchor) })
			{
				Space space = Space.Create("Legacy " + stored);
				space.alignment.method = (Method)stored;
				space.Sanitize();
				Assert.That(space.alignment.method, Is.EqualTo(expected), stored.ToString());
			}
		}

		[Test]
		public void OneUnreadableFileDoesNotBlockTheRest()
		{
			Space good = Sample();
			Assert.That(store.Save(good), Is.True);
			File.WriteAllText(store.SpacePath(Space.NewId()), "{ this is not json");
			File.WriteAllText(store.SpacePath(Space.NewId()), JsonUtility.ToJson(Sample()));
			File.WriteAllText(Path.Combine(store.Directory, "notes.json"), "{}");
			File.WriteAllText(store.SpacePath(good.id) + ".tmp", "half written");

			// each skipped file is a warning (their order follows the directory listing), never an error
			List<Space> loaded = store.LoadAll();
			Assert.That(loaded.Count, Is.EqualTo(1));
			Assert.That(loaded[0].id, Is.EqualTo(good.id));
			Assert.That(store.IsEmpty, Is.False);

			good.name = "Still saves";
			Assert.That(store.Save(good), Is.True);
			Assert.That(store.LoadAll()[0].name, Is.EqualTo("Still saves"));
		}
	}
}
