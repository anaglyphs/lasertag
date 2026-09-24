using System.Collections.Generic;
using System.Linq;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.LaserTag.Weapons.Bullets;
using Anaglyph.Netcode;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class WandSpellTests
	{
		private const string Folder = "Assets/Anaglyph/LaserTag/Weapons/Wand/";

		[Test]
		public void PartialRevisionsAndFinalResultCastEachOccurrenceOnce()
		{
			var tracker = new SpokenSpellTracker();
			var casts = new List<Wand.Spell>();
			tracker.Process("fireball", false, casts.Add);
			tracker.Process("fireball", false, casts.Add);
			tracker.Process("fire", false, casts.Add);
			tracker.Process("fireball lightning", false, casts.Add);
			tracker.Process("fireball lightning", true, casts.Add);
			CollectionAssert.AreEqual(new[] { Wand.Spell.Fireball, Wand.Spell.Lightning }, casts);
		}

		[Test]
		public void RepeatedSpellAndNextUtteranceCanBothCast()
		{
			var tracker = new SpokenSpellTracker();
			var casts = new List<Wand.Spell>();
			tracker.Process("shield", false, casts.Add);
			tracker.Process("shield shield", true, casts.Add);
			tracker.Process("shield", true, casts.Add);
			CollectionAssert.AreEqual(Enumerable.Repeat(Wand.Spell.Shield, 3), casts);
		}

		[Test]
		public void OnlyWholeSpellWordsCastAndResetClearsPreviousSpeech()
		{
			var tracker = new SpokenSpellTracker();
			var casts = new List<Wand.Spell>();
			tracker.Process("fireballs shielding [unk]", false, casts.Add);
			Assert.That(casts, Is.Empty);
			tracker.Process("LIGHTNING", false, casts.Add);
			tracker.Reset();
			tracker.Process("lightning", true, casts.Add);
			CollectionAssert.AreEqual(new[] { Wand.Spell.Lightning, Wand.Spell.Lightning }, casts);
		}

		[Test]
		public void PickupResolvesThroughWeaponAndMapDatabases()
		{
			var wand = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Wand.prefab");
			var weapons = AssetDatabase.LoadAssetAtPath<WeaponDatabase>(
				"Assets/Anaglyph/LaserTag/Weapons/Weapon Database.asset");
			Assert.That(weapons.IndexOf(wand), Is.GreaterThanOrEqualTo(0));
			var view = weapons.GetView(weapons.IndexOf(wand));
			Assert.That(view.GetComponent<WeaponVisual>(), Is.Not.Null);
			Assert.That(view.GetComponentInChildren<Wand>(true), Is.Null);
			Assert.That(wand.GetComponent<IWeapon>().VisualObject, Is.SameAs(view));
			var objects = AssetDatabase.LoadAssetAtPath<MapObjectDatabase>(
				"Assets/Anaglyph/LaserTag/Objects/Map Object Databse.asset");
			var pickup = objects.FindPrefab("Wand Pickup");
			Assert.That(pickup, Is.Not.Null);
			Assert.That(pickup.GetComponent<WeaponPickup>().weaponPrefab, Is.SameAs(wand));
			Assert.That(objects.Categories.SelectMany(c => c.Objects).Count(e => e.Prefab == pickup), Is.EqualTo(1));
		}

		[TestCase("Fire/Fireball", 0.5f)]
		[TestCase("Lightning/Lightning", 0.01f)]
		public void ProjectilesUseCurrentBulletAndBlasterPointLightSettings(string path, float radius)
		{
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + path + ".prefab");
			Assert.That(prefab.GetComponentsInChildren<Component>(true).All(c => c != null), Is.True);
			Assert.That(prefab.GetComponent<SpellProjectileVisual>(), Is.Not.Null);
			var bullet = new SerializedObject(prefab.GetComponent<Bullet>());
			Assert.That(bullet.FindProperty("collisionRadius").floatValue, Is.EqualTo(radius));
			var reference = AssetDatabase.LoadAssetAtPath<GameObject>(
				"Assets/Anaglyph/LaserTag/Weapons/Blaster/Blaster Bullet.prefab").GetComponentInChildren<Light>(true);
			var light = prefab.GetComponentInChildren<Light>(true);
			Assert.That(light.type, Is.EqualTo(LightType.Point));
			Assert.That(light.range, Is.EqualTo(reference.range));
			Assert.That(light.intensity, Is.EqualTo(reference.intensity));
			Assert.That(light.shadows, Is.EqualTo(reference.shadows));
			Assert.That(prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name.Contains("Depth Light")), Is.False);
		}

		[Test]
		public void SpellsAndPickupAreRegisteredForNetworking()
		{
			var network = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
			var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Anaglyph/LaserTag/Networking.prefab");
			var pools = new SerializedObject(root.GetComponent<NetworkObjectPool>()).FindProperty("PooledPrefabsList");
			foreach (string path in new[] { "Fire/Fireball", "Lightning/Lightning", "Shield", "Wand Pickup" })
			{
				var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + path + ".prefab");
				Assert.That(network.PrefabList.Count(p => p.Prefab == prefab), Is.EqualTo(1), path);
				if (path == "Wand Pickup") continue;
				int count = 0;
				for (int i = 0; i < pools.arraySize; i++)
					if (pools.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab)
						count++;
				Assert.That(count, Is.EqualTo(1), path);
			}
		}

		[Test]
		public void ShieldBlocksIncomingProjectiles()
		{
			var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
			var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
				UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
			Vector3 origin = Vector3.one * 10000;
			GameObject shield = null;
			try
			{
				shield = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Shield.prefab"));
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shield, scene);
				shield.transform.SetPositionAndRotation(origin, Quaternion.identity);
				Physics.SyncTransforms();
				var physics = scene.GetPhysicsScene();
				Assert.That(physics.Raycast(origin + new Vector3(0, 0, 3), Vector3.back, out RaycastHit hit, 5,
					Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), Is.True);
				Assert.That(hit.collider.transform.root.gameObject, Is.SameAs(shield));
			}
			finally
			{
				if (shield != null) Object.DestroyImmediate(shield);
				UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
				UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
			}
		}
	}
}
