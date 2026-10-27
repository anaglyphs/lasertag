using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode.SyncVariables;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;
using Space = Anaglyph.LaserTag.Maps.Space;

namespace Anaglyph.LaserTag.Tests.PlayMode
{
	/// <summary>
	/// The offline path in the main scene, as the editor runs it: a space is created, each
	/// alignment method activates its provider, a tag registers and unregisters, a placed
	/// object survives a save and a reload, and everything the test made is deleted again.
	/// Whether a method actually aligns depends on the simulated room, so that is not asserted.
	/// </summary>
	public class OfflineSpaceTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private readonly List<string> created = new();
		private string shownBefore;

		[UnityTest]
		public IEnumerator SpacesLoadEditSaveAndAlignOfflineForEveryMethod()
		{
			// the main scene's menus throw in a batch editor without a display; this test is about the map layer
			LogAssert.ignoreFailingMessages = true;
			yield return SceneManager.LoadSceneAsync("MainScene");
			float deadline = Time.realtimeSinceStartup + 30f;
			while ((SpaceManager.Instance == null || SpaceManager.Instance.Space == null) && Time.realtimeSinceStartup < deadline) yield return null;
			SpaceManager manager = SpaceManager.Instance;
			Assert.That(manager, Is.Not.Null, "the map manager is in the main scene");
			Assert.That(manager.Space, Is.Not.Null, "startup shows a space in the editor");
			Assume.That(manager.Role, Is.Not.EqualTo(SpaceRole.Client));
			shownBefore = manager.Space.id;
			ColocationManager colocation = ColocationManager.Instance;
			try
			{
				// a new space starts visual, with one empty map, and is on disk at once
				Assert.That(manager.NewSpace(), Is.True);
				Space space = manager.Space;
				created.Add(space.id);
				Assert.That(space.alignment.method, Is.EqualTo(Method.Visual));
				Assert.That(manager.Map, Is.Not.Null);
				Assert.That(Reload(manager, space.id).maps.Count, Is.EqualTo(1));
				yield return null;
				Assert.That(colocation.ActiveMethod, Is.EqualTo(Method.Visual));
				Assert.That(colocation.VisualProvider.IsRunning, Is.True);

				// two-tag mode: AprilTag without registered tags, the tag provider as the source
				Assert.That(manager.Alignment.SetMethod(Method.AprilTag), Is.True);
				yield return null;
				Assert.That(colocation.ActiveMethod, Is.EqualTo(Method.AprilTag));
				Assert.That(colocation.TagProvider.IsRunning, Is.True);
				Assert.That(manager.Alignment.UsingTwoTagMode, Is.True);
				Assert.That(Reload(manager, space.id).alignment.method, Is.EqualTo(Method.AprilTag));
				Assert.That(manager.Alignment.TagSizeBlocker(), Is.Null);
				Assert.That(manager.Alignment.SetTagSize(14f), Is.True);
				Assert.That(Reload(manager, space.id).alignment.tagSizeCm, Is.EqualTo(14f).Within(.001f));

				// registered tags: the first tag of a free frame defines it, and leaves two-tag mode
				string frameBefore = space.frameId;
				Assert.That(manager.Alignment.TagRegistrationBlocker(), Is.Null, "a space with no references lets its first tag define the frame");
				Assert.That(manager.Alignment.RegisterTag(7, new Pose(new Vector3(1, 1.5f, 2), Quaternion.Euler(0, 90, 0))), Is.True);
				yield return null;
				Assert.That(space.tags.Count, Is.EqualTo(1));
				Assert.That(space.frameId, Is.EqualTo(frameBefore), "registering into a free frame keeps it");
				Assert.That(manager.Alignment.UsingTwoTagMode, Is.False);
				Assert.That(colocation.TagProvider.RegisteredTags.ContainsKey(7), Is.True);
				Assert.That(manager.Alignment.TagSizeBlocker(), Is.Not.Null, "registered tags fix the size they were solved at");
				Assert.That(Reload(manager, space.id).tags.Count, Is.EqualTo(1));
				Assert.That(manager.Alignment.UnregisterTag(7), Is.True);
				yield return null;
				Assert.That(space.tags.Count, Is.Zero);
				Assert.That(space.frameId, Is.Not.EqualTo(frameBefore), "losing the last tag enters two-tag mode: a new frame");
				Assert.That(manager.Alignment.UsingTwoTagMode, Is.True);

				// shared anchors: the anchor provider runs; whether the simulated runtime mints is its own business
				Assert.That(manager.Alignment.SetMethod(Method.MetaSharedAnchor), Is.True);
				yield return null;
				Assert.That(colocation.ActiveMethod, Is.EqualTo(Method.MetaSharedAnchor));
				Assert.That(colocation.AnchorProvider.IsRunning, Is.True);
				Assert.That(colocation.TagProvider.IsRunning, Is.False);
				Assert.That(Reload(manager, space.id).alignment.method, Is.EqualTo(Method.MetaSharedAnchor));

				// back to visual, then edit the map: a placed object is saved and comes back after a reload
				Assert.That(manager.Alignment.SetMethod(Method.Visual), Is.True);
				yield return null;
				MapObject prefab = FirstPrefab(manager);
				Assert.That(prefab, Is.Not.Null, "the object database has a placeable prefab");
				Object.Instantiate(prefab.gameObject, new Vector3(2, 0, 3), Quaternion.Euler(0, 45, 0));
				yield return null;
				Assert.That(manager.Save(), Is.True);
				Assert.That(Reload(manager, space.id).maps[0].objects.Count, Is.EqualTo(1));
				Assert.That(Reload(manager, space.id).maps[0].objects[0].prefabId, Is.EqualTo(prefab.PrefabId));

				Assert.That(manager.NewSpace(), Is.True);
				created.Add(manager.Space.id);
				yield return null;
				Assert.That(MapObject.All.Count, Is.Zero, "another space shows its own, empty map");
				Assert.That(manager.LoadSpace(space.id), Is.True);
				yield return null;
				Assert.That(manager.Space, Is.SameAs(space));
				Assert.That(MapObject.All.Count, Is.EqualTo(1), "the saved object is instantiated again");
				Assert.That(manager.Space.alignment.method, Is.EqualTo(Method.Visual));

				// a second map in the space, and a rename, both land in the file
				Assert.That(manager.NewMap(), Is.True);
				Assert.That(manager.RenameMap("Smoke test map"), Is.True);
				Assert.That(manager.RenameSpace("Smoke test space"), Is.True);
				Space reloaded = Reload(manager, space.id);
				Assert.That(reloaded.name, Is.EqualTo("Smoke test space"));
				Assert.That(reloaded.maps.Count, Is.EqualTo(2));
				Assert.That(reloaded.maps.Exists(m => m.name == "Smoke test map"), Is.True);

				if (manager.Role == SpaceRole.Offline)
				{
					string frame = space.frameId;
					Assert.That(manager.ResetAlignment(), Is.True);
					Assert.That(space.frameId, Is.Not.EqualTo(frame));
					Assert.That(space.HasReferences, Is.False);
				}
			}
			finally
			{
				// the shown space cannot be deleted while a session is up (the editor may auto-host), so leave it first
				if (shownBefore != null && !created.Contains(shownBefore)) manager.LoadSpace(shownBefore);
				foreach (string id in created) manager.DeleteSpace(id);
				LogAssert.ignoreFailingMessages = false;
			}
			foreach (string id in created)
				Assert.That(new SpaceStore(manager.Store.Directory).LoadAll().Exists(s => s.id == id), Is.False, "deleted spaces leave no file");
			Assert.That(manager.Space?.id, Is.EqualTo(shownBefore), "the space shown before the test is shown again");
		}

		private static Space Reload(SpaceManager manager, string id)
		{
			Space space = new SpaceStore(manager.Store.Directory).LoadAll().Find(s => s.id == id);
			Assert.That(space, Is.Not.Null, "the space is on disk");
			return space;
		}

		private static MapObject FirstPrefab(SpaceManager manager)
		{
			var database = (MapObjectDatabase)typeof(SpaceManager).GetField("objectDatabase", Private).GetValue(manager);
			foreach (MapObjectDatabase.Category category in database.Categories)
				foreach (MapObjectDatabase.Entry entry in category.Objects)
					if (entry.Prefab != null && !category.DebugOnly) return entry.Prefab;
			return null;
		}
	}
}
