using System;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class ColocationMethodHandoffTests
	{
		private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
		private GameObject owner;
		private ColocationManager manager;
		private AprilTagColocationConstraintProvider tags;
		private SpatialAnchorColocationConstraintProvider anchors;
		private object adapter;
		private MethodInfo adopt;

		[SetUp]
		public void SetUp()
		{
			// Exercise the real import/export paths without a scene, bus, or native anchor runtime.
			owner = new GameObject("Colocation method handoff test");
			owner.SetActive(false);
			manager = owner.AddComponent<ColocationManager>();
			tags = owner.AddComponent<AprilTagColocationConstraintProvider>();
			anchors = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			typeof(ColocationManager).GetField("aprilTagColocationProvider", PrivateInstance).SetValue(manager, tags);
			typeof(ColocationManager).GetField("spatialAnchorColocationProvider", PrivateInstance).SetValue(manager, anchors);
			Type adapterType = typeof(GameMap).Assembly.GetType("Anaglyph.LaserTag.Maps.MapColocationAdapter", true);
			adapter = Activator.CreateInstance(adapterType, new object[] { manager });
			adopt = adapterType.GetMethod("AdoptProviderState");
		}

		[TearDown]
		public void TearDown() => UnityEngine.Object.DestroyImmediate(owner);

		private GameMap Adopt(GameMap map, bool restore) => (GameMap)adopt.Invoke(adapter, new object[] { map, restore });

		[Test]
		public void JoiningThroughSharedAnchorsRetainsPrivateTagRealizationForALaterSwitch()
		{
			Guid local = Guid.NewGuid();
			Guid authority = Guid.NewGuid();
			Pose localCanon = new(new Vector3(1, 2, 3), Quaternion.Euler(0, 30, 0));
			Pose sharedCanon = new(new Vector3(4, 5, 6), Quaternion.identity);
			GameMap map = new();
			map.SetTag(7, Pose.identity);
			map.SetAnchorWithTag(local.ToString("N"), localCanon, 7);
			tags.SetRegisteredTags(new[] { new TagConstraintData(7, Pose.identity) });
			anchors.SetConstraints(new[] { new AnchorConstraintData(authority, sharedCanon, 7) });

			GameMap joined = Adopt(map, true);
			List<TaggedAnchorConstraintData> restored = new();
			tags.GetLocalAnchorConstraints(restored);
			Assert.That(restored.Count, Is.EqualTo(1));
			Assert.That(restored[0].guid, Is.EqualTo(local));
			Assert.That(restored[0].canonPose, Is.EqualTo(localCanon));
			Assert.That(joined.TryGetAnchor(authority.ToString("N"), out _), Is.True);

			// The method commit changes which realization is captured, without another map load.
			object method = typeof(ColocationManager).GetField("methodSync", PrivateInstance).GetValue(manager);
			method.GetType().GetProperty("Value").SetValue(method, ColocationManager.ColocationMethod.AprilTag);
			GameMap switched = Adopt(joined, false);
			Assert.That(switched.anchors.Count, Is.EqualTo(1));
			Assert.That(switched.anchors[0].guid, Is.EqualTo(local.ToString("N")));
			Assert.That(switched.anchors[0].canonPose, Is.EqualTo(localCanon));
		}

		[Test]
		public void DuplicateRealizationsForOneTagDoNotReplaceTheRestoredPrivateUuid()
		{
			Guid local = Guid.NewGuid();
			GameMap saved = new();
			saved.SetTag(7, Pose.identity);
			saved.SetAnchorWithTag(local.ToString("N"), Pose.identity, 7);
			saved.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, 7);
			tags.SetRegisteredTags(new[] { new TagConstraintData(7, Pose.identity) });
			Adopt(saved, true);
			List<TaggedAnchorConstraintData> restored = new();
			tags.GetLocalAnchorConstraints(restored);
			Assert.That(restored.Count, Is.EqualTo(1));
			Assert.That(restored[0].guid, Is.EqualTo(local));
		}

		[Test]
		public void OfflineImportKeepsTheCanonicalPosePairedWithItsOwnUuid()
		{
			Guid local = Guid.NewGuid();
			Pose localCanon = new(new Vector3(1, 2, 3), Quaternion.identity);
			GameMap saved = new();
			saved.SetTag(7, Pose.identity);
			saved.SetAnchorWithTag(local.ToString("N"), localCanon, 7);
			saved.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, 7);
			adapter.GetType().GetMethod("Inject").Invoke(adapter, new object[] { saved });
			List<TaggedAnchorConstraintData> restored = new();
			tags.GetLocalAnchorConstraints(restored);
			Assert.That(restored.Count, Is.EqualTo(1));
			Assert.That(restored[0].guid, Is.EqualTo(local));
			Assert.That(restored[0].canonPose, Is.EqualTo(localCanon));
			Assert.That(anchors.Constraints.Count, Is.EqualTo(2));
		}
	}
}
