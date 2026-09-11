using System.Collections.Generic;
using System.Reflection;
using Anaglyph.XR.SharedSpaces;
using NUnit.Framework;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class SystemDeterminedColocationTests
	{
		private GameObject owner;
		private Transform trackingSpace;
		private Transform camera;
		private SystemDeterminedColocationConstraintProvider provider;
		private readonly List<ColocationConstraint> constraints = new();

		[SetUp]
		public void SetUp()
		{
			owner = new GameObject("System origin test");
			owner.SetActive(false);
			owner.transform.SetPositionAndRotation(new Vector3(8, 0, 3), Quaternion.Euler(0, 70, 0));
			trackingSpace = new GameObject("Tracking space").transform;
			trackingSpace.SetParent(owner.transform, false);
			camera = new GameObject("Tracked camera").transform;
			camera.SetParent(trackingSpace, false);
			camera.localPosition = new Vector3(2, 1.6f, -1);
			camera.localRotation = Quaternion.Euler(10, 20, 0);
			provider = new SystemDeterminedColocationConstraintProvider(trackingSpace);
			constraints.Clear();
		}

		[TearDown]
		public void TearDown() => UnityEngine.Object.DestroyImmediate(owner);

		[Test]
		public void StartingResetsWorldOriginWithoutResettingTrackedCameraPose()
		{
			Vector3 position = camera.localPosition;
			Quaternion rotation = camera.localRotation;
			provider.StartProviding();
			Assert.That(trackingSpace.position.magnitude, Is.LessThan(0.0001f));
			Assert.That(Quaternion.Angle(trackingSpace.rotation, Quaternion.identity), Is.LessThan(0.001f));
			Assert.That(camera.localPosition, Is.EqualTo(position));
			Assert.That(Quaternion.Angle(camera.localRotation, rotation), Is.LessThan(0.001f));
			provider.GetColocationConstraints(constraints);
			Assert.That(constraints.Count, Is.EqualTo(1));
			Assert.That(constraints[0].hasReliableRotation, Is.True);
			Assert.That(constraints[0].canon, Is.EqualTo(Pose.identity));
		}

		[Test]
		public void ConstraintsExposeOriginOffsetsButNeverFollowCameraMovement()
		{
			provider.StartProviding();
			camera.localPosition += Vector3.forward * 5;
			Pose offset = new(new Vector3(1, 2, 3), Quaternion.Euler(0, 45, 0));
			trackingSpace.SetPositionAndRotation(offset.position, offset.rotation);
			provider.GetColocationConstraints(constraints);
			Assert.That(Vector3.Distance(constraints[0].observed.position, offset.position), Is.LessThan(0.0001f));
			Assert.That(Quaternion.Angle(constraints[0].observed.rotation, offset.rotation), Is.LessThan(0.001f));
			Assert.That(constraints[0].canon, Is.EqualTo(Pose.identity));
		}

		[Test]
		public void StoppingReleasesTheFrameAndRestartingResetsItAgain()
		{
			provider.GetColocationConstraints(constraints);
			Assert.That(constraints, Is.Empty);
			provider.StartProviding();
			provider.StopProviding();
			trackingSpace.position = Vector3.one;
			provider.GetColocationConstraints(constraints);
			Assert.That(constraints, Is.Empty);
			Assert.That(provider.IsRunning, Is.False);
			Assert.That(Vector3.Distance(trackingSpace.position, Vector3.one), Is.LessThan(0.0001f));
			provider.StartProviding();
			Assert.That(trackingSpace.position.magnitude, Is.LessThan(0.0001f));
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void SavedSystemChoiceSurvivesReferenceCompatibilityAndHostFallback(bool tags, bool anchors)
		{
			var method = ColocationManager.ColocationMethod.SystemDetermined;
			Assert.That(ColocationManager.CompatibleMethod(method, tags, true, anchors), Is.EqualTo(method));
			var manager = owner.AddComponent<ColocationManager>();
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			typeof(ColocationManager).GetField("mapPreference", flags).SetValue(manager, method);
			typeof(ColocationManager).GetField("mapHasTags", flags).SetValue(manager, tags);
			typeof(ColocationManager).GetField("mapHasAnchors", flags).SetValue(manager, anchors);
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(method));
			var colocator = owner.AddComponent<Colocator>();
			typeof(ColocationManager).GetField("colocator", flags).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("systemDeterminedProvider", flags).SetValue(manager, provider);
			colocator.SetProvider(provider);
			Assert.That(manager.UsingSystemDeterminedProvider, Is.True);
			Assert.That(manager.CountRealizableReferences(), Is.EqualTo(1));
			colocator.SetProvider(null);
			Assert.That(manager.CountRealizableReferences(), Is.Zero);
		}

		[Test]
		public void TagSetupRetainsSystemProviderUntilATagExists()
		{
			var manager = owner.AddComponent<ColocationManager>();
			var colocator = owner.AddComponent<Colocator>();
			var tags = owner.AddComponent<Anaglyph.XR.SharedSpaces.AprilTags.AprilTagColocationConstraintProvider>();
			const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
			typeof(ColocationManager).GetField("colocator", flags).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("systemDeterminedProvider", flags).SetValue(manager, provider);
			typeof(ColocationManager).GetField("aprilTagColocationProvider", flags).SetValue(manager, tags);
			manager.ConfigureMap(true, false, true, true, ColocationManager.ColocationMethod.AprilTag, null, true);
			Assert.That(manager.IsSettingUpTags, Is.True);
			Assert.That(manager.SelectedMethod, Is.EqualTo(ColocationManager.ColocationMethod.SystemDetermined));
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(ColocationManager.ColocationMethod.SystemDetermined));
			Assert.That(manager.UsingSystemDeterminedProvider, Is.True);
			Assert.That(manager.CountRealizableReferences(), Is.EqualTo(1));

			manager.ConfigureMap(true, true, true, true, ColocationManager.ColocationMethod.AprilTag, null);
			Assert.That(manager.IsSettingUpTags, Is.False);
			Assert.That(manager.SelectedMethod, Is.EqualTo(ColocationManager.ColocationMethod.AprilTag));
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(ColocationManager.ColocationMethod.AprilTag));
			Assert.That(manager.UsingTagProvider, Is.True);
		}

		[Test]
		public void OnlyATaglessSwitchFromSystemDeterminedCanStageSetup()
		{
			foreach (ColocationManager.ColocationMethod current in System.Enum.GetValues(typeof(ColocationManager.ColocationMethod)))
			foreach (ColocationManager.ColocationMethod requested in System.Enum.GetValues(typeof(ColocationManager.ColocationMethod)))
			{
				Assert.That(ColocationManager.RequiresTagSetup(current, requested, true), Is.False);
				Assert.That(ColocationManager.RequiresTagSetup(current, requested, false), Is.EqualTo(
					current == ColocationManager.ColocationMethod.SystemDetermined && requested == ColocationManager.ColocationMethod.AprilTag));
			}
		}

		[Test]
		public void ExistingSystemFrameCannotSwitchToAnEmptyPhysicalProvider()
		{
			MapPolicy policy = new(MapPhase.Hosting, hasMap: true, empty: false, hasTags: false,
				frameAgrees: true, sessionHolding: false, roundInProgress: false, sessionUsesTags: false,
				hasAnchors: false, systemDeterminedFrame: true);
			Assert.That(policy.ColocationMethodBlocker(targetHasReferences: false), Is.Not.Null);
			Assert.That(policy.ColocationMethodBlocker(targetHasReferences: true), Is.Null);
			Assert.That(policy.NeedsFirstTag, Is.False);
		}
	}
}
