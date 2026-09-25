using System.Linq;
using System.Reflection;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.LaserTag.Weapons.Bullets;
using Anaglyph.Netcode;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class WandSweepShieldTests
	{
		private const string Folder = "Assets/Anaglyph/LaserTag/Weapons/Wand/";
		private static readonly Vector3 Origin = new(10000, 10000, 10000);

		[Test]
		public void RequiresBothSpeedAndDisplacement()
		{
			var gesture = new WandSweepGesture();
			Assert.That(gesture.Sample(Vector3.zero, 0.02f, 1.5f, 0.25f), Is.False);
			for (int i = 1; i <= 6; i++)
				Assert.That(gesture.Sample(Vector3.right * i * 0.04f, 0.02f, 1.5f, 0.25f), Is.False);
			Assert.That(gesture.Sample(Vector3.right * 0.28f, 0.02f, 1.5f, 0.25f), Is.True);
			Assert.That(gesture.Sample(Vector3.right * 0.29f, 0.02f, 1.5f, 0.25f), Is.False);
			Assert.That(gesture.Sample(Vector3.right * 0.33f, 0.02f, 1.5f, 0.25f), Is.False);
		}

		[Test]
		public void SlowMotionAndFastJitterDoNotEngage()
		{
			var gesture = new WandSweepGesture();
			for (int i = 0; i < 100; i++)
				Assert.That(gesture.Sample(Vector3.right * i * 0.01f, 0.02f, 1.5f, 0.25f), Is.False);
			gesture.Reset();
			for (int i = 0; i < 100; i++)
				Assert.That(gesture.Sample(Vector3.right * (i % 2) * 0.04f, 0.02f, 1.5f, 0.25f), Is.False);
		}

		[Test]
		public void TrackingResetGapsAndJumpsCannotEngage()
		{
			var gesture = new WandSweepGesture();
			gesture.Sample(Vector3.zero, 0.02f, 1.5f, 0.25f);
			Assert.That(gesture.Sample(Vector3.right * 3, 0.02f, 1.5f, 0.25f), Is.False);
			Assert.That(gesture.Sample(Vector3.right * 4, 0.2f, 1.5f, 0.25f), Is.False);
			gesture.Reset();
			Assert.That(gesture.Sample(Vector3.right * 5, 0.02f, 1.5f, 0.25f), Is.False);
		}

		[Test]
		public void IdleTrailIsVisibleWithoutBlocking()
		{
			WithTrail(trail =>
			{
				trail.Add(Segment(false));
				Refresh(trail);
				Assert.That(Get<TrailRenderer>(trail, "trailRenderer").emitting, Is.True);
				Assert.That((Color32)Get<TrailRenderer>(trail, "trailRenderer").startColor, Is.EqualTo((Color32)Get<Color>(trail, "trailColor")));
				Assert.That(Get<CapsuleCollider[]>(trail, "capsules").Any(c => c != null && c.enabled), Is.False);
			});
		}

		[Test]
		public void ShieldBlocksFromBothSidesAndSkipsItsOwner()
		{
			WithTrail(trail =>
			{
				trail.OwnerClientId = 42;
				trail.Add(Segment(true));
				Refresh(trail);
				foreach (float direction in new[] { -1f, 1f })
				foreach (float radius in new[] { 0f, 0.01f, 0.5f })
				{
					Vector3 from = Origin + Vector3.forward * direction * 2;
					Vector3 travel = Vector3.back * direction * 4;
					Assert.That(ProjectileBarrier.Cast(from, travel, radius, 7, out _), Is.True);
					Assert.That(ProjectileBarrier.Cast(from, travel, radius, 42, out _), Is.False);
				}
			});
		}

		[Test]
		public void RemoteReplayMatchesLocalGeometryAndColor()
		{
			WithTrail(local => WithTrail(remote =>
			{
				local.Emitted += remote.Add;
				local.Emit(Segment(true));
				Refresh(local); Refresh(remote);
				var localCapsule = Get<CapsuleCollider[]>(local, "capsules")[0];
				var remoteCapsule = Get<CapsuleCollider[]>(remote, "capsules")[0];
				Assert.That(remoteCapsule.transform.position, Is.EqualTo(localCapsule.transform.position));
				Assert.That(remoteCapsule.transform.rotation, Is.EqualTo(localCapsule.transform.rotation));
				Assert.That(remoteCapsule.radius, Is.EqualTo(localCapsule.radius));
				Assert.That(remoteCapsule.height, Is.EqualTo(localCapsule.height));
				Assert.That(Get<TrailRenderer>(local, "trailRenderer").startColor, Is.EqualTo(Get<TrailRenderer>(remote, "trailRenderer").startColor));
				Assert.That(Get<TrailRenderer>(remote, "trailRenderer").widthMultiplier, Is.EqualTo(1f));
				Assert.That(remoteCapsule.enabled, Is.True);
			}));
		}

		[Test]
		public void ExpiredAndDisabledTrailsCannotBlock()
		{
			WithTrail(trail =>
			{
				trail.Add(Segment(true)); Refresh(trail);
				Get<float[]>(trail, "expiresAt")[0] = SharedNetworkTime.TimeAsFloat - 1;
				var expired = Segment(true); expired.time -= 1;
				Refresh(trail);
				Assert.That(Get<CapsuleCollider[]>(trail, "capsules")[0].enabled, Is.False);
				Assert.That(Get<CapsuleCollider[]>(trail, "capsules").Any(c => c != null && c.enabled), Is.False);
				trail.Add(expired); Refresh(trail);
				Assert.That(Get<CapsuleCollider[]>(trail, "capsules")[0].enabled, Is.False);
				trail.Add(Segment(true)); Refresh(trail);
				trail.gameObject.SetActive(false);
				Invoke(trail, "OnDisable");
				trail.gameObject.SetActive(true);
				Invoke(trail, "OnEnable");
				Assert.That(Get<CapsuleCollider[]>(trail, "capsules").Any(c => c != null && c.enabled), Is.False);
			});
		}

		[Test]
		public void CapsulesAreReusedAndPoolIsBounded()
		{
			WithTrail(trail =>
			{
				trail.Add(Segment(true));
				var capsules = Get<CapsuleCollider[]>(trail, "capsules");
				CapsuleCollider first = capsules[0];
				Get<float[]>(trail, "expiresAt")[0] = SharedNetworkTime.TimeAsFloat - 1;
				Refresh(trail);
				trail.Add(Segment(true));
				Assert.That(capsules[0], Is.SameAs(first));
				Assert.That(capsules.Count(c => c != null), Is.EqualTo(1));
				for (int i = 0; i < 64; i++) trail.Add(Segment(true));
				Assert.That(Get<GameObject>(trail, "trailObject").GetComponentsInChildren<CapsuleCollider>().Length, Is.EqualTo(32));
			});
		}

		[Test]
		public void CapsuleCoversSweepEndpointsAndWidth()
		{
			WithTrail(trail =>
			{
				trail.Add(Segment(true)); Refresh(trail);
				CapsuleCollider capsule = Get<CapsuleCollider[]>(trail, "capsules")[0];
				Assert.That(capsule.radius, Is.EqualTo(0.5f));
				Assert.That(capsule.height, Is.EqualTo(2f));
				foreach (Vector3 offset in new[] { Vector3.left * 0.75f, Vector3.right * 0.75f, Vector3.up * 0.4f })
					Assert.That(ProjectileBarrier.Cast(Origin + offset + Vector3.back * 2, Vector3.forward * 4, 0, 7, out _), Is.True);
				Assert.That(ProjectileBarrier.Cast(Origin + Vector3.up * 0.6f + Vector3.back * 2, Vector3.forward * 4, 0, 7, out _), Is.False);
			});
		}

		[Test]
		public void PrefabSharesTrailAndOnlySpellsOptIntoBarriers()
		{
			var wand = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "Wand.prefab");
			var view = wand.GetComponent<Wand>().VisualObject;
			var trail = view.GetComponent<WandSweepTrail>();
			Assert.That(trail, Is.Not.Null);
			Assert.That(new SerializedObject(wand.GetComponent<Wand>()).FindProperty("sweepTrail").objectReferenceValue, Is.SameAs(trail));
			var renderer = (TrailRenderer)new SerializedObject(trail).FindProperty("trailRenderer").objectReferenceValue;
			Assert.That(renderer, Is.Not.Null);
			Assert.That(renderer.transform.IsChildOf(view.transform), Is.True);
			Assert.That(renderer.sharedMaterial, Is.Not.Null);
			foreach (string path in new[] { "Fire/Fireball", "Lightning/Lightning" })
			{
				var bullet = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + path + ".prefab").GetComponent<Bullet>();
				Assert.That(new SerializedObject(bullet).FindProperty("blockedByProjectileBarriers").boolValue, Is.True);
			}
			var blaster = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Anaglyph/LaserTag/Weapons/Blaster/Blaster Bullet.prefab").GetComponent<Bullet>();
			Assert.That(new SerializedObject(blaster).FindProperty("blockedByProjectileBarriers").boolValue, Is.False);
		}

		private static WandSweepTrail.Segment Segment(bool blocking) => new()
		{
			startLeft = Origin + new Vector3(-0.5f, -0.5f, 0),
			startRight = Origin + new Vector3(-0.5f, 0.5f, 0),
			endLeft = Origin + new Vector3(0.5f, -0.5f, 0),
			endRight = Origin + new Vector3(0.5f, 0.5f, 0),
			time = SharedNetworkTime.TimeAsFloat,
			blocking = blocking
		};

		private static void Refresh(WandSweepTrail trail)
		{
			Invoke(trail, "LateUpdate");
			Physics.SyncTransforms();
		}

		private static T Get<T>(WandSweepTrail trail, string name) =>
			(T)typeof(WandSweepTrail).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(trail);

		private static void Invoke(WandSweepTrail trail, string name) =>
			typeof(WandSweepTrail).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(trail, null);

		private static void WithTrail(System.Action<WandSweepTrail> action)
		{
			var root = new GameObject("Sweep test");
			var renderer = root.AddComponent<TrailRenderer>();
			var trail = root.AddComponent<WandSweepTrail>();
			var serialized = new SerializedObject(trail);
			serialized.FindProperty("trailRenderer").objectReferenceValue = renderer;
			serialized.ApplyModifiedPropertiesWithoutUndo();
			Invoke(trail, "OnEnable");
			try { action(trail); }
			finally
			{
				Object.DestroyImmediate(Get<GameObject>(trail, "trailObject"));
				Object.DestroyImmediate(root);
			}
		}
	}
}
