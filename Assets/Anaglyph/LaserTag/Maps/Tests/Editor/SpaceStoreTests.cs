using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Anaglyph.LaserTag.Maps;
using Anaglyph.XR.VisualLocalization;
using NUnit.Framework;
using Unity.Mathematics;
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

		[Test]
		public void MigratesAV2CatalogOnce()
		{
			string v2 = Path.Combine(root, CatalogV2Migrator.V2Folder);
			Directory.CreateDirectory(Path.Combine(v2, "spaces"));
			Directory.CreateDirectory(Path.Combine(v2, "maps"));
			string spaceId = Space.NewId(), frameId = Space.NewId(), storageFrameId = Space.NewId();
			string mapId = Space.NewId(), otherMapId = Space.NewId(), anchorId = Space.NewId(), localAnchorId = Space.NewId();
			Pose offset = new(new Vector3(1, 0, 2), Quaternion.Euler(0, 90, 0));
			string spaceJson = V2SpaceJson(spaceId, "Garage", storageFrameId, frameId, offset, new[] { mapId, otherMapId }, anchorId, localAnchorId);
			File.WriteAllText(Path.Combine(v2, "spaces", spaceId + ".json"), spaceJson);
			File.WriteAllText(Path.Combine(v2, "maps", mapId + ".json"), V2MapJson(mapId, "Garage layout", storageFrameId, "Base", new Vector3(1, 0, 0)));
			File.WriteAllText(Path.Combine(v2, "maps", otherMapId + ".json"), V2MapJson(otherMapId, "Foreign", Space.NewId(), "Base", Vector3.zero));
			// a space whose primary is corrupt but whose backup reads
			string backedUpId = Space.NewId();
			File.WriteAllText(Path.Combine(v2, "spaces", backedUpId + ".json"), "{ corrupt");
			File.WriteAllText(Path.Combine(v2, "spaces", backedUpId + ".json.bak"),
				V2SpaceJson(backedUpId, "Backup", storageFrameId, storageFrameId, Pose.identity, Array.Empty<string>(), null, null));
			File.WriteAllBytes(Path.Combine(v2, "spaces", SpaceStore.VocabularyFile), new byte[] { 1, 2, 3, 4 });
			WriteVisualMap(Path.Combine(v2, "spaces", spaceId + ".vmap"), storageFrameId, new Pose(new Vector3(0, 1, 0), Quaternion.identity));

			Assert.That(CatalogV2Migrator.NeedsMigration(v2, store), Is.True);
			Assert.That(CatalogV2Migrator.Migrate(v2, store), Is.EqualTo(2));
			Assert.That(CatalogV2Migrator.NeedsMigration(v2, store), Is.False);
			Assert.That(File.Exists(Path.Combine(v2, "spaces", spaceId + ".json")), Is.True, "the old catalog is left for rollback");

			List<Space> spaces = store.LoadAll();
			Assert.That(spaces.Count, Is.EqualTo(2));
			Space space = spaces.Find(s => s.id == spaceId);
			Assert.That(space.name, Is.EqualTo("Garage"));
			Assert.That(space.frameId, Is.EqualTo(frameId));
			Assert.That(space.alignment.method, Is.EqualTo(Method.Visual), "scan registration migrates to visual");
			Assert.That(space.alignment.tagSizeCm, Is.EqualTo(9.5f).Within(.001f));
			Assert.That(space.maps.Count, Is.EqualTo(1), "a map in another frame is not embedded");
			Assert.That(space.maps[0].id, Is.EqualTo(mapId));
			Assert.That(space.maps[0].name, Is.EqualTo("Garage layout"));
			Assert.That(space.maps[0].objects.Count, Is.EqualTo(1));
			Vector3 expected = offset.position + offset.rotation * new Vector3(1, 0, 0);
			Assert.That(Vector3.Distance(space.maps[0].objects[0].pose.position, expected), Is.LessThan(.001f), "poses pass through canonicalFromStorage");
			Assert.That(space.tags.Count, Is.EqualTo(1));
			Assert.That(Vector3.Distance(space.tags[0].pose.position, offset.position + offset.rotation * new Vector3(3, 1, 0)), Is.LessThan(.001f));
			Assert.That(space.anchors.Count, Is.EqualTo(1));
			Assert.That(space.anchors[0].guid, Is.EqualTo(anchorId));
			Assert.That(JsonUtility.ToJson(space), Does.Not.Contain("retainedReferences").And.Not.Contain("associations").And.Not.Contain("knownFrames"));

			PrivateReferences references = store.LoadPrivate(spaceId);
			Assert.That(references, Is.Not.Null);
			Assert.That(references.Matches(space), Is.True);
			Assert.That(references.tagAnchors.Count, Is.EqualTo(1));
			Assert.That(references.tagAnchors[0].guid, Is.EqualTo(localAnchorId));
			Assert.That(references.tagAnchors[0].tagId, Is.EqualTo(7));

			Assert.That(File.ReadAllBytes(store.VocabularyPath), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
			Assert.That(store.HasVisualMap(spaceId), Is.True);
			using (FileStream f = new(store.VisualMapPath(spaceId), FileMode.Open, FileAccess.Read))
			{
				VisualMapData data = VisualMapFile.Read(f);
				Assert.That(data.frameId, Is.EqualTo(frameId), "the visual map follows the space into its aligned frame");
				Assert.That(Vector3.Distance(data.keyframes[0].camera.position, offset.position + offset.rotation * new Vector3(0, 1, 0)), Is.LessThan(.001f));
			}
			Assert.That(spaces.Find(s => s.id == backedUpId).name, Is.EqualTo("Backup"));

			// a second run after the user deleted everything must not resurrect the old catalog
			foreach (Space s in spaces) store.Delete(s.id);
			Assert.That(store.IsEmpty, Is.True);
			Assert.That(CatalogV2Migrator.NeedsMigration(v2, store), Is.False);
		}

		private static string V2SpaceJson(string id, string name, string storageFrameId, string canonicalFrameId, Pose offset,
			string[] mapIds, string anchorId, string localAnchorId)
		{
			string maps = string.Join(",", Array.ConvertAll(mapIds, m => $"\"{m}\""));
			string anchors = anchorId == null ? "" : $"{{\"guid\":\"{anchorId}\",\"canonPose\":{PoseJson(new Pose(new Vector3(2, 0, 2), Quaternion.identity))},\"tagId\":-1,\"tagSizeCm\":0,\"tagCanonPose\":{PoseJson(Pose.identity)}}}";
			string localAnchors = localAnchorId == null ? "" : $"{{\"guid\":\"{localAnchorId}\",\"canonPose\":{PoseJson(new Pose(new Vector3(3, 1, 0.1f), Quaternion.identity))},\"tagId\":7,\"tagSizeCm\":9.5,\"tagCanonPose\":{PoseJson(new Pose(new Vector3(3, 1, 0), Quaternion.identity))}}}";
			return "{\"schemaVersion\":1," +
				$"\"id\":\"{id}\",\"name\":\"{name}\",\"version\":\"{Space.NewId()}\",\"storageFrameId\":\"{storageFrameId}\",\"canonicalFrameId\":\"{canonicalFrameId}\"," +
				$"\"canonicalFromStorage\":{PoseJson(offset)},\"frameRevision\":1,\"lastUsed\":638000000000000000,\"lastEdited\":0,\"dirty\":false," +
				"\"automaticallyCreated\":false,\"initializationPending\":false,\"hasPendingSetup\":false,\"pendingSetupMethod\":1,\"pendingSetupIntent\":0," +
				$"\"mapIds\":[{maps}],\"tags\":[{{\"id\":7,\"canonPose\":{PoseJson(new Pose(new Vector3(3, 1, 0), Quaternion.identity))}}}]," +
				$"\"anchors\":[{anchors}],\"localAnchors\":[{localAnchors}]," +
				$"\"retainedReferences\":[{{\"sourceId\":\"{Space.NewId()}\",\"version\":\"{Space.NewId()}\",\"tagSizeCm\":9.5,\"tags\":[],\"anchors\":[]}}]," +
				$"\"associations\":[{{\"remoteSpaceId\":\"{Space.NewId()}\",\"frameId\":\"{canonicalFrameId}\"}}]," +
				$"\"knownFrames\":[{{\"frameId\":\"{canonicalFrameId}\",\"canonicalFromStorage\":{PoseJson(offset)}}}]," +
				$"\"referenceSourceId\":\"{id}\",\"referenceVersion\":\"{Space.NewId()}\",\"referenceDirty\":false,\"tagSizeCm\":9.5," +
				"\"preferredColocationMethod\":4,\"firstTagId\":-1,\"secondTagId\":-1,\"previousCanonicalFrameId\":\"\"," +
				$"\"previousOffset\":{PoseJson(Pose.identity)},\"visualMapSaved\":0}}";
		}

		private static string V2MapJson(string id, string name, string storageFrameId, string prefabId, Vector3 position) =>
			$"{{\"schemaVersion\":1,\"id\":\"{id}\",\"name\":\"{name}\",\"storageFrameId\":\"{storageFrameId}\",\"version\":\"{Space.NewId()}\"," +
			$"\"dirty\":false,\"lastUsed\":638000000000000000,\"lastEdited\":0,\"objects\":[{{\"prefabId\":\"{prefabId}\",\"pose\":{PoseJson(new Pose(position, Quaternion.identity))}}}]}}";

		private static string PoseJson(Pose pose) =>
			$"{{\"position\":{{\"x\":{F(pose.position.x)},\"y\":{F(pose.position.y)},\"z\":{F(pose.position.z)}}}," +
			$"\"rotation\":{{\"x\":{F(pose.rotation.x)},\"y\":{F(pose.rotation.y)},\"z\":{F(pose.rotation.z)},\"w\":{F(pose.rotation.w)}}}}}";

		private static string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

		private static void WriteVisualMap(string path, string frameId, Pose camera)
		{
			VisualMapData data = new() { frameId = frameId, savedTicks = 1 };
			data.keyframes.Add(new VisualMapData.Keyframe
			{
				camera = camera, intrinsics = new PinholeCamera(500, 500, 320, 240), timestampNs = 1, original = true, count = 0,
				keypoints = Array.Empty<float2>(), scores = Array.Empty<float>(), descriptors = Array.Empty<half>(), landmarks = Array.Empty<int>()
			});
			data.landmarks.Add(new VisualMapData.Landmark { id = 1, origin = new float3(0, 1, 0), direction = new float3(0, 0, 1), inverseDepth = .5f, inverseDepthSigma = .1f });
			using FileStream f = new(path, FileMode.Create, FileAccess.Write);
			VisualMapFile.Write(data, f);
		}
	}
}
