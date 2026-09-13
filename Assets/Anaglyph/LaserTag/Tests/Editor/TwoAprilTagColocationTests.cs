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
		private Component tracker;
		private bool trackingReady;
		private long frameTimestamp;

		[SetUp]
		public void SetUp()
		{
			owner = new GameObject("Two AprilTag test");
			owner.SetActive(false);
			observations = owner.AddComponent<AprilTagColocationConstraintProvider>();
			var trackerField = typeof(AprilTagColocationConstraintProvider).GetField("tagTracker", PrivateInstance);
			tracker = owner.AddComponent(trackerField.FieldType);
			trackerField.SetValue(observations, tracker);
			trackingReady = true; frameTimestamp = 0;
			provider = new TwoAprilTagColocationConstraintProvider(observations, owner.transform, () => trackingReady);
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
		public void FirstUsablePairLocksItsIdsDespiteLaterLowerIds(int first, int second, int third)
		{
			Select(first);
			Assert.That(provider.ForwardTagId, Is.Null, "One ID cannot define a line.");
			Select(first);
			Assert.That(provider.ForwardTagId, Is.Null, "Repeated detections are not a second tag.");
			Select(second);
			Select(third);
			Select(-1);
			Select(100);
			Assert.That(provider.OriginTagId, Is.EqualTo(System.Math.Min(first, second)));
			Assert.That(provider.ForwardTagId, Is.EqualTo(System.Math.Max(first, second)));
		}

		[Test]
		public void SeeingIdsAloneDoesNotCommitAPairBeforeGeometryIsUsable()
		{
			int committed = 0; provider.PairSelected += (_, _) => committed++;
			Select(12); Select(24);
			provider.GetColocationConstraints(new List<ColocationConstraint>());
			Assert.That(committed, Is.Zero);
		}

		[Test]
		public void ChangingTheConfiguredPairDiscardsItsPreviousObservations()
		{
			provider.StartProviding();
			Observe(80, Vector3.forward * 2);
			Observe(20, Vector3.zero);
			Assert.That(Constraints().Count, Is.EqualTo(2));
			provider.ConfigurePair(10, 20);
			Observe(10, Vector3.back * 2);
			Assert.That(Constraints(), Is.Empty, "The previous observation of tag 20 must not survive.");
			Observe(20, Vector3.zero);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void SequentialObservationsAlignWithoutAnyAnchorRuntimeAndPersistOutOfView()
		{
			provider.StartProviding();
			Assert.That(provider.IsAvailable, Is.True);
			Observe(17, new Vector3(4, 1, -8));
			Assert.That(Constraints(), Is.Empty);
			Observe(92, new Vector3(4, 1.5f, -5));
			Assert.That(Constraints().Count, Is.EqualTo(2));
			Assert.That(observations.IsRunning, Is.False, "Detection must not start registered-tag anchors.");
			Assert.That(observations.RegisteredTags, Is.Empty);
			provider.ConfigurePair(17, 92);
			Assert.That(Constraints().Count, Is.EqualTo(2), "Reapplying the same space pair preserves observations.");
		}

		[Test]
		public void ObservationsAndCorrectionsFollowTrackingSpaceInsteadOfCachingWorldPositions()
		{
			provider.StartProviding();
			Vector3 first = new(4, 1, -8), second = new(4, 1.5f, -5);
			Observe(17, first); Observe(92, second);
			Assert.That(Constraints().Count, Is.EqualTo(2));
			owner.transform.SetPositionAndRotation(new Vector3(3, -1, 7), Quaternion.Euler(0, 90, 0));
			var constraints = Constraints();
			Assert.That(Vector3.Distance(constraints[0].observed.position, owner.transform.TransformPoint(first)), Is.LessThan(.0001f));
			Assert.That(Vector3.Distance(constraints[1].observed.position, owner.transform.TransformPoint(second)), Is.LessThan(.0001f));
			Vector3 corrected = first + Vector3.right * .02f;
			for (int i = 0; i < 30; i++)
			{
				owner.transform.rotation = Quaternion.Euler(0, i * 3, 0);
				Observe(17, owner.transform.TransformPoint(corrected));
			}
			Assert.That(Vector3.Distance(Constraints()[0].observed.position, owner.transform.TransformPoint(corrected)), Is.LessThan(.0001f));
		}

		[TestCase("recenter", false)]
		[TestCase("recenter", true)]
		[TestCase("focus", false)]
		[TestCase("focus", true)]
		[TestCase("pause", false)]
		[TestCase("pause", true)]
		[TestCase("lost", true)]
		[TestCase("invalidate", true)]
		public void TrackingLifecycleRequiresBothSavedTagsAgain(string reason, bool completePair)
		{
			var manager = owner.AddComponent<ColocationManager>();
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			provider.ConfigurePair(17, 92); provider.StartProviding();
			Observe(17, Vector3.zero);
			if (completePair) Observe(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(completePair ? 2 : 0));
			switch (reason)
			{
				case "recenter": manager.TrackingOriginChanged(); break;
				case "invalidate": manager.InvalidateAlignment(); break;
				case "focus": CallManager(manager, "OnApplicationFocus", false); break;
				case "pause": CallManager(manager, "OnApplicationPause", true); break;
				case "lost": CallManager(manager, "OnStateChanged", ColocationAlignmentState.Lost); break;
			}
			Assert.That(Constraints(), Is.Empty);
			Observe(1, Vector3.zero); Observe(2, Vector3.forward * 3);
			Assert.That(provider.OriginTagId, Is.Null, "Automatic resets retain the saved pair IDs.");
			Observe(92, Vector3.forward * 3);
			Assert.That(Constraints(), Is.Empty, "Both observations must come from the new tracking frame.");
			Observe(17, Vector3.zero);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void HeadTrackingLossDiscardsObservationsWithoutNeedingARecenterEvent(bool detectionDuringLoss)
		{
			provider.ConfigurePair(17, 92); provider.StartProviding();
			Observe(17, Vector3.zero); Observe(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(2));
			trackingReady = false;
			if (detectionDuringLoss) Observe(17, Vector3.zero);
			else Assert.That(Constraints(), Is.Empty);
			trackingReady = true;
			Observe(92, Vector3.forward * 3);
			Assert.That(Constraints(), Is.Empty);
			Observe(17, Vector3.zero);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void DetectionAlreadyInFlightAtInvalidationCannotReacquireThePair()
		{
			provider.ConfigurePair(17, 92); provider.StartProviding();
			NextFrame(); // The detector sets its timestamp before awaiting processing.
			provider.ResetReferences();
			Publish(17, Vector3.zero); Publish(92, Vector3.forward * 3);
			Assert.That(Constraints(), Is.Empty);
			NextFrame(); Publish(17, Vector3.zero); Publish(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void PairSelectionThatInvalidatesTheContextCannotReturnStaleConstraints()
		{
			provider.StartProviding();
			provider.PairSelected += (_, _) => provider.ResetReferences();
			Observe(17, Vector3.zero); Observe(92, Vector3.forward * 3);
			Assert.That(Constraints(), Is.Empty);
			Observe(17, Vector3.zero); Observe(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void SwitchingToTwoTagsStopsRetainedAnchorServicesButKeepsDetectionRunning()
		{
			var manager = owner.AddComponent<ColocationManager>();
			var anchors = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			typeof(ColocationManager).GetField("aprilTagColocationProvider", PrivateInstance).SetValue(manager, observations);
			typeof(ColocationManager).GetField("spatialAnchorColocationProvider", PrivateInstance).SetValue(manager, anchors);
			observations.StartProviding();
			typeof(SpatialAnchorColocationConstraintProvider).GetProperty("IsRunning").SetValue(anchors, true);
			var type = typeof(Maps.MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceAlignmentController", true);
			using var controller = (System.IDisposable)System.Activator.CreateInstance(type,
				new object[] { manager, (System.Func<bool>)(() => true) });
			type.GetMethod("Activate").Invoke(controller,
				new object[] { Maps.MapSpace.Create("Two tags"), ColocationManager.ColocationMethod.TwoAprilTags, false });
			Assert.That(observations.IsRunning, Is.False);
			Assert.That(anchors.IsRunning, Is.False);
			Assert.That(provider.IsRunning, Is.True);
			Assert.That(observations.IsDetecting, Is.True);
			Observe(17, Vector3.zero); Observe(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void RefreshingTheSameSourceWhileSearchingKeepsAPartiallyScannedPair()
		{
			var manager = owner.AddComponent<ColocationManager>();
			var colocator = owner.AddComponent<Colocator>();
			typeof(ColocationManager).GetField("colocator", PrivateInstance).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			manager.ActivateMethod(ColocationManager.ColocationMethod.TwoAprilTags);
			Observe(17, Vector3.zero);
			manager.ActivateMethod(ColocationManager.ColocationMethod.TwoAprilTags, true);
			Observe(92, Vector3.forward * 3);
			Assert.That(Constraints().Count, Is.EqualTo(2));
		}

		[Test]
		public void ReferenceCountReportsOnlyAnObservedUsablePair()
		{
			var manager = owner.AddComponent<ColocationManager>();
			var colocator = owner.AddComponent<Colocator>();
			typeof(ColocationManager).GetField("colocator", PrivateInstance).SetValue(manager, colocator);
			typeof(ColocationManager).GetField("twoAprilTagProvider", PrivateInstance).SetValue(manager, provider);
			manager.ActivateMethod(ColocationManager.ColocationMethod.TwoAprilTags);
			Assert.That(manager.CountRealizableReferences(), Is.Zero);
			Observe(17, Vector3.zero);
			Assert.That(manager.CountRealizableReferences(), Is.Zero);
			Observe(92, Vector3.forward * 3);
			Assert.That(manager.CountRealizableReferences(), Is.EqualTo(2));
			manager.TrackingOriginChanged();
			Assert.That(manager.CountRealizableReferences(), Is.Zero);
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
			Observe(7, Vector3.zero); Observe(8, Vector3.forward * 3);
			Assert.That(provider.OriginTagId, Is.Null, "Stopped providers must unsubscribe from detections.");
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
			var space = Maps.MapSpace.Create("Test"); space.preferredColocationMethod = ColocationManager.ColocationMethod.TwoAprilTags;
			manager.ConfigureSpace(space, null);
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
			var adapterType = typeof(Maps.GameMap).Assembly.GetType("Anaglyph.LaserTag.Maps.MapSpaceColocationAdapter", true);
			var adapter = System.Activator.CreateInstance(adapterType, new object[] { manager });
			provider.StartProviding();
			Select(1);
			Select(2);
			adapterType.GetMethod("Inject").Invoke(adapter, new object[] { Maps.MapSpace.Create("Test") });
			Assert.That(provider.OriginTagId, Is.Null);
			Select(50);
			Select(70);
			Assert.That(provider.OriginTagId, Is.EqualTo(50));
			Assert.That(provider.ForwardTagId, Is.EqualTo(70));
			adapterType.GetMethod("Inject").Invoke(adapter, new object[] { Maps.MapSpace.Create("Other") });
			Assert.That(provider.OriginTagId, Is.Null);
		}

		[Test]
		public void BlankTwoTagMapRequiresAlignmentWithoutRequestingRegistration()
		{
			var policy = new MapPolicy(MapPhase.Hosting, true, true, false, false, false, false, false,
				hasAnchors: false, twoTagFrame: true);
			Assert.That(policy.HasAlignmentReferences, Is.False);
			Assert.That(policy.FrameIsTrusted, Is.False);
			Assert.That(policy.EditBlocker, Is.Not.Null);
			Assert.That(policy.NeedsFirstTag, Is.False);
			Assert.That(policy.ColocationMethodBlocker(true), Is.Null);
		}

		private object Select(int id) => typeof(TwoAprilTagColocationConstraintProvider)
			.GetMethod("SelectReference", PrivateInstance).Invoke(provider, new object[] { id });

		private List<ColocationConstraint> Constraints()
		{
			var result = new List<ColocationConstraint>();
			provider.GetColocationConstraints(result);
			return result;
		}
		private void NextFrame() => tracker.GetType().GetProperty("FrameTimestampNs").SetValue(tracker, ++frameTimestamp);
		private void Observe(int id, Vector3 position) { NextFrame(); Publish(id, position); }
		private void Publish(int id, Vector3 position) =>
			((System.Action<int, Pose>)typeof(AprilTagColocationConstraintProvider)
				.GetField("TagObserved", PrivateInstance).GetValue(observations))?.Invoke(id, new Pose(position, Quaternion.identity));
		private static void CallManager(ColocationManager manager, string method, object argument) =>
			typeof(ColocationManager).GetMethod(method, PrivateInstance).Invoke(manager, new[] { argument });
	}
}
