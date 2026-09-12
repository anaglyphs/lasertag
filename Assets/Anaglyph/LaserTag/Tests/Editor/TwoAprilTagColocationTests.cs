using System.Collections.Generic;
using System.Reflection;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class TwoAprilTagColocationTests
	{
		private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
		private GameObject owner;
		private AprilTagColocationConstraintProvider observations;
		private TwoAprilTagColocationConstraintProvider provider;

		[SetUp]
		public void SetUp()
		{
			owner = new GameObject("Two AprilTag test");
			owner.SetActive(false);
			observations = owner.AddComponent<AprilTagColocationConstraintProvider>();
			provider = new TwoAprilTagColocationConstraintProvider(observations, null, owner.transform);
		}

		[TearDown]
		public void TearDown()
		{
			provider.StopProviding();
			Object.DestroyImmediate(owner);
		}

		[TestCase(0f, 0f)]
		[TestCase(90f, 0.5f)]
		[TestCase(-135f, -0.8f)]
		[TestCase(180f, 2f)]
		public void SolverPlacesLowerTagAtZeroAndHigherTagForwardWithoutTiltingOrScaling(float yaw, float height)
		{
			Vector3 origin = new(4, 1.2f, -8);
			Vector3 forward = origin + Quaternion.Euler(0, yaw, 0) * new Vector3(0, height, 3);
			Assert.That(TwoAprilTagColocationConstraintProvider.TryCreateConstraints(origin, forward,
				out var first, out var second), Is.True);
			Matrix4x4 fit = BestFit.Find4DOF(new List<(float3, float3)>
			{
				(first.observed.position, first.canon.position),
				(second.observed.position, second.canon.position)
			});
			Assert.That(fit.MultiplyPoint3x4(origin).magnitude, Is.LessThan(0.0001f));
			Assert.That(Vector3.Distance(fit.MultiplyPoint3x4(forward), new Vector3(0, height, 3)), Is.LessThan(0.0001f));
			Assert.That(Vector3.Distance(fit.MultiplyVector(Vector3.up), Vector3.up), Is.LessThan(0.0001f));
			Assert.That(Vector3.Distance(fit.lossyScale, Vector3.one), Is.LessThan(0.0001f));
			Assert.That(first.hasReliableRotation || second.hasReliableRotation, Is.False);
		}

		[Test]
		public void RejectsCoincidentVerticalTooCloseAndNonFinitePoints()
		{
			foreach (Vector3 position in new[] { Vector3.zero, Vector3.up * 2, Vector3.forward * 0.01f,
				new Vector3(float.NaN, 0, 2), new Vector3(0, float.PositiveInfinity, 2),
				new Vector3(float.MaxValue, 0, float.MaxValue) })
				Assert.That(TwoAprilTagColocationConstraintProvider.TryCreateConstraints(Vector3.zero, position,
					out _, out _), Is.False);
			Assert.That(TwoAprilTagColocationConstraintProvider.TryCreateConstraints(
				new Vector3(float.NaN, 0, 0), Vector3.forward, out _, out _), Is.False);
		}

		[TestCase(17, 92, 45)]
		[TestCase(92, 17, 45)]
		[TestCase(45, 92, 17)]
		[TestCase(92, 45, 17)]
		public void ArbitraryIdsSelectTheSameTwoLowestRegardlessOfScanOrder(int first, int second, int third)
		{
			Select(first);
			Assert.That(provider.ForwardTagId, Is.Null, "One ID cannot define a line.");
			Select(first);
			Assert.That(provider.ForwardTagId, Is.Null, "Repeated detections are not a second tag.");
			Select(second);
			Select(third);
			Select(-1);
			Select(100);
			Assert.That(provider.OriginTagId, Is.EqualTo(17));
			Assert.That(provider.ForwardTagId, Is.EqualTo(45));
		}

		[Test]
		public void ReplacingATagRetiresItsPendingMintButPreservesAReferenceThatChangesRole()
		{
			object higher = Select(80);
			object lower = Select(20);
			Assert.That(Select(80), Is.SameAs(higher));
			Select(10);
			Assert.That(Select(20), Is.SameAs(lower));
			Assert.That(higher.GetType().GetField("retired").GetValue(higher), Is.True);
			Assert.That(lower.GetType().GetField("retired").GetValue(lower), Is.False);
		}

		[Test]
		public void StopAndSizeChangesDiscardThePairAndDoNotStartRegisteredTagAlignment()
		{
			provider.StartProviding();
			provider.StartProviding();
			Select(9);
			Select(50);
			Assert.That(observations.IsRunning, Is.False);
			Assert.That(observations.RegisteredTags, Is.Empty);
			observations.AdoptTagSize(15);
			Assert.That(provider.OriginTagId, Is.Null);
			Assert.That(provider.ForwardTagId, Is.Null);
			Select(7);
			provider.StopProviding();
			provider.StopProviding();
			Assert.That(provider.OriginTagId, Is.Null);
			var requests = (HashSet<object>)typeof(AprilTagColocationConstraintProvider)
				.GetField("detectionRequests", PrivateInstance).GetValue(observations);
			Assert.That(requests, Is.Empty);
			var constraints = new List<ColocationConstraint>();
			provider.GetColocationConstraints(constraints);
			Assert.That(constraints, Is.Empty);
		}

		[TestCase(false, false)]
		[TestCase(false, true)]
		[TestCase(true, false)]
		[TestCase(true, true)]
		public void SavedChoiceSelectsNewProviderWithoutReferenceOrHostFallback(bool tags, bool anchors)
		{
			var manager = owner.AddComponent<ColocationManager>();
			var colocator = owner.AddComponent<Colocator>();
			var anchorProvider = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			typeof(ColocationManager).GetField("colocator", PrivateInstance).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("spatialAnchorColocationProvider", PrivateInstance).SetValue(manager, anchorProvider);
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			manager.ConfigureMap(true, tags, true, anchors, ColocationManager.ColocationMethod.TwoAprilTags, null);
			Assert.That(manager.ActiveProvider, Is.SameAs(provider));
			Assert.That(manager.PreferredSessionMethod, Is.EqualTo(ColocationManager.ColocationMethod.TwoAprilTags));
			Assert.That(manager.IsSettingUpTags, Is.False);
			Assert.That(anchorProvider.CanShareAnchors, Is.False);
		}

		[Test]
		public void LoadingAnotherMapWithTheSameTagSizeAllowsADifferentHigherIdPair()
		{
			var manager = owner.AddComponent<ColocationManager>();
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			var adapterType = typeof(Maps.GameMap).Assembly.GetType("Anaglyph.LaserTag.Maps.MapColocationAdapter", true);
			var adapter = System.Activator.CreateInstance(adapterType, new object[] { manager });
			provider.StartProviding();
			Select(1);
			Select(2);
			adapterType.GetMethod("Inject").Invoke(adapter, new object[] { new Maps.GameMap { tagSizeCm = 12 } });
			Assert.That(provider.OriginTagId, Is.Null);
			Select(50);
			Select(70);
			Assert.That(provider.OriginTagId, Is.EqualTo(50));
			Assert.That(provider.ForwardTagId, Is.EqualTo(70));
			adapterType.GetMethod("AdoptProviderState").Invoke(adapter, new object[] { new Maps.GameMap { tagSizeCm = 12 }, true });
			Assert.That(provider.OriginTagId, Is.Null);
		}

		[Test]
		public void BlankTwoTagMapRequiresAlignmentWithoutRequestingRegistration()
		{
			var policy = new MapPolicy(MapPhase.Hosting, true, true, false, false, false, false, false,
				hasAnchors: false, twoTagFrame: true);
			Assert.That(policy.HasAlignmentReferences, Is.True);
			Assert.That(policy.FrameIsTrusted, Is.False);
			Assert.That(policy.EditBlocker, Is.Not.Null);
			Assert.That(policy.NeedsFirstTag, Is.False);
			Assert.That(policy.ColocationMethodBlocker(true), Is.Null);
		}

		private object Select(int id) => typeof(TwoAprilTagColocationConstraintProvider)
			.GetMethod("SelectReference", PrivateInstance).Invoke(provider, new object[] { id });
	}
}
