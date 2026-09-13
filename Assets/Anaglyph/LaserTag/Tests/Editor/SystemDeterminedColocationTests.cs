using System.Collections.Generic;
using System.Reflection;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.LaserTag.Maps;
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
			var space = MapSpace.Create("Test"); space.preferredColocationMethod = method;
			manager.ManagedSelection = true; manager.ConfigureSpace(space, null);
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(method));
			var colocator = owner.AddComponent<Colocator>();
			typeof(ColocationManager).GetField("colocator", flags).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("systemDeterminedProvider", flags).SetValue(manager, provider);
			manager.ActivateMethod(method);
			Assert.That(manager.UsingSystemDeterminedProvider, Is.True);
			Assert.That(manager.CountRealizableReferences(), Is.EqualTo(1));
			colocator.SetProvider(null);
			Assert.That(manager.CountRealizableReferences(), Is.Zero);
		}

	}
}
