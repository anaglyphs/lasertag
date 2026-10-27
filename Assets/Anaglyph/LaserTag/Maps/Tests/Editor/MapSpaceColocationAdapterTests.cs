using System;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces.AprilTags;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class MapSpaceColocationAdapterTests
	{
		private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
		private static readonly Type AdapterType = typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceColocationAdapter", true);
		private GameObject owner;
		private AprilTagColocationConstraintProvider tags;
		private object adapter;

		[SetUp]
		public void SetUp()
		{
			Assert.That(SyncBus.Active, Is.False);
			owner = new GameObject("Private reference capture test");
			owner.SetActive(false);
			var manager = owner.AddComponent<ColocationManager>();
			tags = owner.AddComponent<AprilTagColocationConstraintProvider>();
			typeof(ColocationManager).GetField("aprilTagColocationProvider", PrivateInstance).SetValue(manager, tags);
			adapter = Activator.CreateInstance(AdapterType, new object[] { manager });
			AdapterType.GetMethod("Register").Invoke(adapter, null);
		}

		[TearDown]
		public void TearDown()
		{
			if (adapter != null) AdapterType.GetMethod("Unregister").Invoke(adapter, null);
			UnityEngine.Object.DestroyImmediate(owner);
		}

		private static MapAnchorEntry SavedAnchor(MapSpace space, string guid) => new()
		{
			guid = guid, tagId = 7, canonPose = new Pose(Vector3.right, Quaternion.identity),
			tagCanonPose = space.tags[0].canonPose, tagSizeCm = space.tagSizeCm
		};

		private MapSpace Capture(MapSpace source, bool pending)
		{
			if (!pending)
				return (MapSpace)AdapterType.GetMethod("CapturePrivateReferences").Invoke(adapter, new object[] { source });
			var captureType = AdapterType.Assembly.GetType("Anaglyph.LaserTag.Maps.SpaceReferenceCapture", true);
			var capture = Activator.CreateInstance(captureType);
			captureType.GetField("TaggedAnchors").SetValue(capture, true);
			return (MapSpace)AdapterType.GetMethod("TakePendingSnapshot").Invoke(adapter, new[] { source.Clone(), capture });
		}

		[TestCase(false)]
		[TestCase(true)]
		public void CapturingLiveAnchorPrioritizesItAndRetainsEarlierSavedUuid(bool pending)
		{
			var space = MapSpace.Create("Room");
			space.canonicalFromStorage = new Pose(Vector3.forward * 4, Quaternion.Euler(0, 90, 0));
			space.SetTag(7, Pose.identity);
			string oldId = Guid.NewGuid().ToString("N");
			space.localAnchors.Add(SavedAnchor(space, oldId));
			var liveId = Guid.NewGuid();
			var storedPose = new Pose(Vector3.left * 2, Quaternion.Euler(0, 30, 0));
			tags.AdoptTagSize(space.tagSizeCm);
			tags.SetRegisteredTags(new[] { new TagConstraintData(7, space.Frame.ToCanonical(Pose.identity)) });
			tags.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(liveId, 7, space.Frame.ToCanonical(storedPose)) });

			var captured = Capture(space, pending);

			Assert.That(captured.localAnchors.ConvertAll(anchor => anchor.guid), Is.EqualTo(new[] { liveId.ToString("N"), oldId }));
			Assert.That(captured.IsCompatibleLocalAnchor(captured.localAnchors[0]), Is.True);
			Assert.That(MapSpaceFrame.Near(captured.localAnchors[0].canonPose, storedPose), Is.True);
			Assert.That(space.localAnchors.Count, Is.EqualTo(1));
			Assert.That(space.localAnchors[0].guid, Is.EqualTo(oldId));
		}

		[TestCase(false, true)]
		[TestCase(true, true)]
		[TestCase(false, false)]
		[TestCase(true, false)]
		public void CapturingCannotAttachLiveAnchorToDifferentTagDefinition(bool pending, bool differentSize)
		{
			var space = MapSpace.Create("Room");
			space.SetTag(7, Pose.identity);
			space.localAnchors.Add(SavedAnchor(space, Guid.NewGuid().ToString("N")));
			tags.AdoptTagSize(space.tagSizeCm + (differentSize ? 1 : 0));
			tags.SetRegisteredTags(new[] { new TagConstraintData(7, differentSize ? Pose.identity : new Pose(Vector3.right, Quaternion.identity)) });
			tags.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(Guid.NewGuid(), 7, Pose.identity) });

			var captured = Capture(space, pending);

			Assert.That(captured.localAnchors, Is.EqualTo(space.localAnchors));
			Assert.That(captured.tags, Is.EqualTo(space.tags));
			Assert.That(captured.tagSizeCm, Is.EqualTo(space.tagSizeCm));
		}

		[Test]
		public void CopyingBetweenStorageFramesPreservesCanonicalPoseAndRestoreOrder()
		{
			var source = MapSpace.Create("Source");
			source.SetTag(7, Pose.identity);
			string liveId = Guid.NewGuid().ToString("N"), olderId = Guid.NewGuid().ToString("N");
			source.localAnchors.Add(SavedAnchor(source, liveId));
			source.localAnchors.Add(SavedAnchor(source, olderId));
			var target = MapSpace.Create("Target");
			target.canonicalFrameId = source.canonicalFrameId;
			target.canonicalFromStorage = new Pose(Vector3.right * 4, Quaternion.Euler(0, 90, 0));
			target.SetTag(7, target.Frame.ToStorage(Pose.identity));
			target.localAnchors.Add(SavedAnchor(target, olderId));

			AdapterType.GetMethod("CopyCompatiblePrivateAnchors").Invoke(null, new object[] { target, source });

			Assert.That(target.localAnchors.ConvertAll(anchor => anchor.guid), Is.EqualTo(new[] { liveId, olderId }));
			foreach (var anchor in target.localAnchors)
			{
				Assert.That(target.IsCompatibleLocalAnchor(anchor), Is.True);
				Assert.That(MapSpaceFrame.Near(target.Frame.ToCanonical(anchor.canonPose), source.localAnchors[0].canonPose), Is.True);
			}
		}

		[Test]
		public void CopyingPrivateReferencesRequiresTheSameCanonicalFrame()
		{
			var source = MapSpace.Create("Source");
			source.SetTag(7, Pose.identity);
			source.localAnchors.Add(SavedAnchor(source, Guid.NewGuid().ToString("N")));
			var target = MapSpace.Create("Target");
			target.SetTag(7, Pose.identity);

			AdapterType.GetMethod("CopyCompatiblePrivateAnchors").Invoke(null, new object[] { target, source });

			Assert.That(target.localAnchors, Is.Empty);
		}
	}
}
