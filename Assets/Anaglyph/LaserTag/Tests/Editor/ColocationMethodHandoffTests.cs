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

		[TestCase(false, false, ColocationManager.ColocationMethod.AprilTag)]
		[TestCase(true, true, ColocationManager.ColocationMethod.AprilTag)]
		[TestCase(false, true, ColocationManager.ColocationMethod.MetaSharedAnchor)]
		public void UnsupportedHostUsesTagsForBlankOrTaggedMapsButPreservesExistingTaglessFrames(
			bool hasTags, bool hasContent, ColocationManager.ColocationMethod expected)
		{
			typeof(ColocationManager).GetField("mapHasTags", PrivateInstance).SetValue(manager, hasTags);
			typeof(ColocationManager).GetField("mapHasContent", PrivateInstance).SetValue(manager, hasContent);
			typeof(ColocationManager).GetField("mapHasAnchors", PrivateInstance).SetValue(manager, hasContent);
			Assert.That(anchors.CanShareAnchors, Is.False);
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(expected));
		}

		[Test]
		public void SystemOriginAllowsFirstTagSetupButAnEmptyAnchorTargetRemainsBlocked()
		{
			var coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			var maps = new MapManager(new MapStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
			maps.Create();
			maps.SetObjects(new[] { new MapObjectEntry { prefabId = "Base", pose = Pose.identity } });
			maps.SetPreferredColocationMethod(ColocationManager.ColocationMethod.SystemDetermined);
			typeof(LaserTagMapCoordinator).GetField("maps", PrivateInstance).SetValue(coordinator, maps);
			typeof(LaserTagMapCoordinator).GetField("colocationManager", PrivateInstance).SetValue(coordinator, manager);
			typeof(ColocationManager).GetField("offlineMethod", PrivateInstance).SetValue(manager, ColocationManager.ColocationMethod.SystemDetermined);
			Assert.That(coordinator.DescribeColocationMethodBlocker(ColocationManager.ColocationMethod.AprilTag), Is.Null);
			Assert.That(coordinator.DescribeColocationMethodBlocker(ColocationManager.ColocationMethod.MetaSharedAnchor), Is.Not.Null);
		}

		[Test]
		public void AlignmentRejectionModalIsScopedToTheLatestRequestAndMap()
		{
			var coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			var maps = new MapManager(new MapStore(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
			maps.Create();
			var type = typeof(LaserTagMapCoordinator);
			type.GetField("maps", PrivateInstance).SetValue(coordinator, maps);
			Guid latest = Guid.NewGuid();
			type.GetField("latestMethodRequest", PrivateInstance).SetValue(coordinator, latest);
			var rejectionType = type.GetNestedType("MethodRejection", BindingFlags.NonPublic);
			object Rejection(Guid requestId, Guid mapId)
			{
				object rejection = Activator.CreateInstance(rejectionType);
				rejectionType.GetField("requestId").SetValue(rejection, requestId);
				rejectionType.GetField("mapId").SetValue(rejection, mapId);
				var reason = rejectionType.GetField("reason");
				reason.SetValue(rejection, Activator.CreateInstance(reason.FieldType, new object[] { "Sharing unavailable" }));
				return rejection;
			}
			var receive = type.GetMethod("OnMethodRejected", PrivateInstance);
			int modalErrors = 0;
			void OnError(UserError _) => modalErrors++;
			UserErrors.Raised += OnError;
			try
			{
				receive.Invoke(coordinator, new[] { (object)0UL, Rejection(Guid.NewGuid(), Guid.Parse(maps.CurrentId)) });
				receive.Invoke(coordinator, new[] { (object)0UL, Rejection(latest, Guid.NewGuid()) });
				Assert.That(modalErrors, Is.Zero);
				receive.Invoke(coordinator, new[] { (object)0UL, Rejection(latest, Guid.Parse(maps.CurrentId)) });
				Assert.That(modalErrors, Is.EqualTo(1));
				maps.Create();
				receive.Invoke(coordinator, new[] { (object)0UL, Rejection(latest, Guid.NewGuid()) });
				Assert.That(modalErrors, Is.EqualTo(1));
				type.GetMethod("OnColocationMethodChanged", PrivateInstance).Invoke(coordinator, null);
				Assert.That(type.GetField("latestMethodRequest", PrivateInstance).GetValue(coordinator), Is.EqualTo(Guid.Empty));
			}
			finally { UserErrors.Raised -= OnError; }
		}

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
