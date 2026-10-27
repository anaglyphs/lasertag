using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.LaserTag.Maps;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;
using Object = UnityEngine.Object;

namespace Anaglyph.LaserTag.Tests
{
	public class MapSpaceObservationTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private ColocationManager manager;
		private AprilTagColocationConstraintProvider tags;
		private AnchorRegistry registry, previousRegistry;
		private ARAnchorManager anchorManager;
		private XRAnchorSubsystem subsystem;
		private Type rigType;
		private object previousRig;
		private bool trackingReady;
		private readonly List<IDisposable> disposables = new();
		private readonly List<ARAnchor> anchors = new();
		private static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
		private static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
		private static void Static(Type type, string name, object value) => type.GetProperty(name).SetValue(null, value);
		private static object Call(object target, string name, params object[] args) =>
			target.GetType().GetMethod(name, BindingFlags.Public | Private).Invoke(target, args);
		private object Create(string type, params object[] args)
		{
			var value = Activator.CreateInstance(typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps." + type, true), args);
			disposables.Add((IDisposable)value); return value;
		}

		public sealed class ObservationAnchorProvider : XRAnchorSubsystem.Provider
		{
			public override void Start() { }
			public override void Stop() { }
			public override void Destroy() { }
			public override TrackableChanges<XRAnchor> GetChanges(XRAnchor defaultAnchor, Allocator allocator) => default;
		}

		[SetUp]
		public void SetUp()
		{
			Assert.That(ColocationManager.Instance, Is.Null);
			previousRegistry = AnchorRegistry.Instance;
			rigType = Type.GetType("Anaglyph.XR.MainXRRig, Anaglyph.XR", true);
			previousRig = rigType.GetProperty("Instance").GetValue(null);
			owner = new GameObject("Space observation test"); owner.SetActive(false);
			manager = owner.AddComponent<ColocationManager>(); manager.ManagedSelection = true;
			tags = owner.AddComponent<AprilTagColocationConstraintProvider>();
			Set(manager, "aprilTagColocationProvider", tags); Static(typeof(ColocationManager), "Instance", manager);
			var rig = owner.AddComponent(rigType); rigType.GetField("trackingSpace").SetValue(rig, owner.transform);
			Static(rigType, "Instance", rig);
			const string id = "MapSpaceObservationTest";
			var descriptors = new List<XRAnchorSubsystemDescriptor>(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			if (!descriptors.Exists(d => d.id == id))
				XRAnchorSubsystemDescriptor.Register(new() { id = id, providerType = typeof(ObservationAnchorProvider) });
			descriptors.Clear(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			subsystem = descriptors.Find(d => d.id == id).Create();
			anchorManager = owner.AddComponent<ARAnchorManager>(); anchorManager.enabled = false;
			SetSubsystem(subsystem);
			registry = owner.AddComponent<AnchorRegistry>(); Set(registry, "anchorManager", anchorManager);
			Static(typeof(AnchorRegistry), "Instance", registry);
			trackingReady = true;
		}

		private void SetSubsystem(XRAnchorSubsystem value)
		{
			var property = typeof(ARAnchorManager).GetProperty("subsystem");
			property.DeclaringType.GetProperty("subsystem").SetValue(anchorManager, value);
		}

		[TearDown]
		public void TearDown()
		{
			for (int i = disposables.Count - 1; i >= 0; i--) disposables[i].Dispose();
			disposables.Clear();
			foreach (var anchor in anchors) if (anchor) Object.DestroyImmediate(anchor.gameObject);
			anchors.Clear();
			Static(rigType, "Instance", previousRig);
			Static(typeof(ColocationManager), "Instance", null);
			Static(typeof(AnchorRegistry), "Instance", previousRegistry);
			if (anchorManager) SetSubsystem(null);
			if (owner) Object.DestroyImmediate(owner);
			subsystem?.Destroy();
		}

		private object Observer(MapSpace space)
		{
			tags.AdoptTagSize(space.tagSizeCm);
			return Create("MapSpaceReferenceObservation", space, Method.AprilTag, registry, tags);
		}
		private static bool Confirmed(object observer, int id = 7) => (bool)Call(observer, "HasRepeatedObservation", id);
		private static void AgeLastObservation(object observer, float age, int id = 7)
		{
			var seen = (Dictionary<int, (Pose pose, float time)>)Get(observer, "seen");
			if (seen.TryGetValue(id, out var last)) seen[id] = (last.pose, Time.unscaledTime - age);
		}
		private static void Observe(object observer, int count, float gap = .4f, Pose? pose = null)
		{
			for (int i = 0; i < count; i++)
			{
				// Simulate detector frames slower than the live optical fit's freshness window.
				AgeLastObservation(observer, gap);
				Call(observer, "OnTag", 7, pose ?? Pose.identity);
			}
		}

		[TestCase(.3f)]
		[TestCase(2f)]
		public void SlowObservationsAccumulateButContradictorySamplesAndRecenterInvalidateThem(float gap)
		{
			var space = MapSpace.Create("Tag room"); space.SetTag(7, Pose.identity);
			var observation = Observer(space);
			Observe(observation, 11, gap); Assert.That(Confirmed(observation), Is.False);
			Observe(observation, 1, gap); Assert.That(Confirmed(observation), Is.True);
			AgeLastObservation(observation, 10); Assert.That(Confirmed(observation), Is.True, "Looking away does not contradict a tag's pose.");
			Observe(observation, 1, gap, new Pose(Vector3.right, Quaternion.identity));
			Assert.That(Confirmed(observation), Is.False, "A contradictory sample must revoke confirmation.");
			Observe(observation, 11, gap); Assert.That(Confirmed(observation), Is.False);
			Observe(observation, 1, gap); Assert.That(Confirmed(observation), Is.True);
			manager.TrackingOriginChanged(); Assert.That(Confirmed(observation), Is.False);
		}

		[Test]
		public void RetainedEvidenceDoesNotAuthorizeAChangedCanonicalTarget()
		{
			var space = MapSpace.Create("Tag room"); space.SetTag(7, Pose.identity);
			var original = Observer(space); Observe(original, 12);
			space.SetTag(22, new Pose(Vector3.forward, Quaternion.identity));
			var extended = Observer(space); Call(extended, "RetainObservationsFrom", original);
			Assert.That(Confirmed(extended), Is.True);
			space.SetTag(7, new Pose(Vector3.right, Quaternion.identity));
			var changed = Observer(space); Call(changed, "RetainObservationsFrom", extended);
			Assert.That(Confirmed(changed), Is.False);
		}

		private object BeginCandidate(out object candidate, out ReferenceAlignmentTransition transition)
		{
			var source = MapSpace.Create("New tag room");
			var target = source.Clone(); target.SetTag(7, Pose.identity); tags.AdoptTagSize(target.tagSizeCm);
			var guid = new SerializableGuid(Guid.NewGuid());
			var lease = registry.Acquire(guid, AnchorSource.Local); disposables.Add(lease);
			var anchorObject = new GameObject("Tracked tag anchor"); anchorObject.SetActive(false);
			var anchor = anchorObject.AddComponent<ARAnchor>(); anchors.Add(anchor);
			typeof(ARAnchor).BaseType.GetMethod("SetSessionRelativeData", Private).Invoke(anchor,
				new object[] { new XRAnchor((TrackableId)guid, Pose.identity, TrackingState.Tracking, IntPtr.Zero) });
			Call(lease.Handle, "OnAnchorAdded", anchor);
			Assert.That(lease.Handle.state, Is.EqualTo(AnchorHandle.State.Active));
			target.localAnchors.Add(new() { guid = guid.guid.ToString("N"), tagId = 7, canonPose = Pose.identity,
				tagCanonPose = Pose.identity, tagSizeCm = target.tagSizeCm });
			var controller = Create("MapSpaceAlignmentController", manager, (Func<bool>)(() => trackingReady));
			Call(controller, "Begin", Guid.NewGuid(), source, Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true);
			Call(controller, "SetCandidate", Guid.NewGuid(), target);
			candidate = Get(controller, "candidate");
			transition = (ReferenceAlignmentTransition)controller.GetType().GetProperty("Transition").GetValue(controller);
			var constraints = new List<ColocationConstraint>(); ((IColocationConstraintProvider)candidate).GetColocationConstraints(constraints);
			Assert.That(constraints.Count, Is.EqualTo(1)); Assert.That(constraints[0].hasReliableRotation, Is.True);
			return controller;
		}

		private static IEnumerator TickForStableWindow(object controller)
		{
			double until = Time.realtimeSinceStartupAsDouble + .7;
			do { Call(controller, "Tick"); yield return null; } while (Time.realtimeSinceStartupAsDouble < until);
			Call(controller, "Tick");
		}

		[UnityTest]
		public IEnumerator CandidateTickValidatesSlowObservationsUsingTheTrackedTagAnchor()
		{
			var controller = BeginCandidate(out var candidate, out var transition);
			var validated = new List<(Guid operation, Guid revision)>();
			controller.GetType().GetEvent("Validated").AddEventHandler(controller, (Action<Guid, Guid>)((op, revision) => validated.Add((op, revision))));
			Observe(candidate, 11); Call(controller, "Tick");
			Assert.That(validated, Is.Empty); Assert.That(transition.Ready, Is.False);
			Observe(candidate, 1); AgeLastObservation(candidate, 10);
			yield return TickForStableWindow(controller);
			Assert.That(transition.Ready, Is.True);
			Assert.That(validated, Does.Contain((transition.Operation, transition.Revision)),
				"The real candidate fit and repeated observations must emit the evidence sent to the authority.");
		}

		[UnityTest]
		public IEnumerator SavedRegistrationContinuesTheSameOperationWithoutReauthoringItsReferences()
		{
			var controller = BeginCandidate(out var candidate, out var transition);
			var saved = ((MapSpace)Get(controller, "candidateSpace")).Clone();
			int reports = 0;
			controller.GetType().GetEvent("Validated").AddEventHandler(controller, (Action<Guid, Guid>)((_, _) => reports++));
			Assert.That(Confirmed(candidate), Is.False);
			Call(controller, "Begin", transition.Operation, saved, Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, false);
			Assert.That(controller.GetType().GetProperty("Transition").GetValue(controller), Is.SameAs(transition));
			Assert.That(Get(controller, "candidate"), Is.SameAs(candidate), "The phase change must retain native observation leases.");
			Assert.That(transition.CreatesReferences, Is.False); Assert.That(manager.IsSettingUpTags, Is.False);
			Assert.That(manager.ReferenceAligned, Is.False, "There was never a usable shared-anchor source in this empty room.");
			yield return TickForStableWindow(controller);
			Assert.That(reports, Is.GreaterThan(0), "A saved tag needs a stable target fit, not a new reference-authoring grant.");
			Assert.That(transition.Ready, Is.True);
			var colocator = owner.AddComponent<Colocator>(); Set(manager, "colocator", colocator);
			colocator.StateChanged += state => Call(manager, "OnStateChanged", state);
			try
			{
				Call(controller, "Commit", true);
				// EditMode does not run the player-loop await in AlignLoop. Execute its fit
				// step using the real provider constraints and colocator before confirmation.
				var constraints = (List<ColocationConstraint>)Get(colocator, "constraints");
				colocator.GetCurrentConstraints(constraints); Call(colocator, "MeasureAgreement");
				Assert.That((bool)Call(colocator, "TryFit"), Is.True);
				Call(colocator, "SetState", ColocationAlignmentState.Localized);
				yield return TickForStableWindow(controller);
				Assert.That(manager.ActiveMethod, Is.EqualTo(Method.AprilTag));
				Assert.That(manager.ReferenceAligned, Is.True, "The real colocator must acquire the saved tag target.");
				Assert.That(transition.Phase, Is.EqualTo(ReferenceTransitionPhase.Completed));
			}
			finally { colocator.StopColocation(); }
		}

		[UnityTest]
		public IEnumerator CandidateTickRequiresFreshTagEvidenceAfterAuthorTrackingLoss()
		{
			var controller = BeginCandidate(out var candidate, out var transition);
			int reports = 0;
			controller.GetType().GetEvent("Validated").AddEventHandler(controller, (Action<Guid, Guid>)((_, _) => reports++));
			Observe(candidate, 12); Call(controller, "Tick");
			trackingReady = false; Call(controller, "Tick");
			Assert.That(Confirmed(candidate), Is.False); Assert.That(transition.Ready, Is.False);
			trackingReady = true;
			yield return TickForStableWindow(controller);
			Assert.That(reports, Is.Zero, "A stable anchor fit cannot replace the author's discarded tag observations.");
			Observe(candidate, 12);
			yield return TickForStableWindow(controller);
			Assert.That(reports, Is.GreaterThan(0));
		}
	}
}
