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
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.LaserTag.Tests
{
	public class TagAnchorRecoveryTests
	{
		private GameObject owner;
		private AprilTagColocationConstraintProvider provider;
		private AnchorRegistry registry;
		private ARAnchorManager anchorManager;
		private XRAnchorSubsystem subsystem;
		private readonly List<AnchorLease> leases = new();
		private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
		private static readonly Type ProviderType = typeof(AprilTagColocationConstraintProvider);
		public sealed class RecoveryAnchorProvider : XRAnchorSubsystem.Provider
		{
			public override void Start() { }
			public override void Stop() { }
			public override void Destroy() { }
			public override TrackableChanges<XRAnchor> GetChanges(XRAnchor defaultAnchor, Allocator allocator) => default;
		}

		[SetUp]
		public void SetUp()
		{
			// Keep Awake/network registration and all native anchor operations out of these tests.
			owner = new GameObject("Tag anchor recovery test");
			owner.SetActive(false);
			provider = owner.AddComponent<AprilTagColocationConstraintProvider>();
			provider.SetRegisteredTags(new[] { new TagConstraintData(1, Pose.identity) });
			const string id = "TagAnchorRecoveryTest";
			var descriptors = new List<XRAnchorSubsystemDescriptor>(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			if (!descriptors.Exists(d => d.id == id))
				XRAnchorSubsystemDescriptor.Register(new() { id = id, providerType = typeof(RecoveryAnchorProvider) });
			descriptors.Clear(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			subsystem = descriptors.Find(d => d.id == id).Create();
			anchorManager = owner.AddComponent<ARAnchorManager>(); anchorManager.enabled = false;
			SetSubsystem(subsystem);
			registry = owner.AddComponent<AnchorRegistry>();
			typeof(AnchorRegistry).GetField("anchorManager", PrivateInstance).SetValue(registry, anchorManager);
		}
		private void SetSubsystem(XRAnchorSubsystem value)
		{
			var property = typeof(ARAnchorManager).GetProperty("subsystem");
			property.DeclaringType.GetProperty("subsystem").SetValue(anchorManager, value);
		}

		[TearDown]
		public void TearDown()
		{
			provider.StopProviding();
			foreach (var lease in leases) lease.Dispose(); leases.Clear();
			SetSubsystem(null); UnityEngine.Object.DestroyImmediate(owner); subsystem.Destroy();
		}

		private object SavedAnchor(Guid guid)
		{
			ProviderType.GetProperty("IsRunning").SetValue(provider, false);
			provider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(guid, 1, Pose.identity) });
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			return ((IDictionary)ProviderType.GetField("localAnchors", PrivateInstance).GetValue(provider))[1];
		}

		private int Generation => (int)ProviderType.GetField("stateGeneration", PrivateInstance).GetValue(provider);

		private bool Commit(Guid guid, int generation, object replacing, Pose? trackingTag = null, Pose? canonTag = null, Pose? trackingAnchor = null)
		{
			var anchorId = new SerializableGuid(guid);
			var lease = registry.Acquire(anchorId, AnchorSource.Local); leases.Add(lease);
			var anchorObject = new GameObject("Minted tag anchor"); anchorObject.transform.SetParent(owner.transform, false);
			var anchor = anchorObject.AddComponent<ARAnchor>();
			Pose pose = trackingAnchor ?? Pose.identity;
			typeof(ARAnchor).BaseType.GetMethod("SetSessionRelativeData", PrivateInstance).Invoke(anchor,
				new object[] { new XRAnchor((TrackableId)anchorId, pose, TrackingState.Tracking, IntPtr.Zero) });
			anchor.transform.SetLocalPositionAndRotation(pose.position, pose.rotation);
			typeof(AnchorHandle).GetMethod("OnAnchorAdded", PrivateInstance).Invoke(lease.Handle, new object[] { anchor });
			Type mintedType = ProviderType.Assembly.GetType("Anaglyph.XR.SharedSpaces.SharedAnchors.MintedAnchor", true);
			object minted = Activator.CreateInstance(mintedType,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
				null, new object[] { lease, guid, true }, null);
			return (bool)ProviderType.GetMethod("CommitTagAnchor", PrivateInstance).Invoke(provider,
				new object[] { 1, canonTag ?? Pose.identity, trackingTag ?? Pose.identity, owner.transform, generation, minted, replacing });
		}

		private Guid RecordedGuid()
		{
			var records = new System.Collections.Generic.List<TaggedAnchorConstraintData>();
			provider.GetLocalAnchorConstraints(records);
			Assert.That(records.Count, Is.EqualTo(1));
			return records[0].guid;
		}

		[Test]
		public void UnrestoredSavedAnchorCanBeReplacedWithoutDroppingItsTag()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			Guid replacement = Guid.NewGuid();
			int changed = 0;
			provider.AnchorsChanged += () => changed++;
			Assert.That(Commit(replacement, Generation, saved), Is.True);
			Assert.That(RecordedGuid(), Is.EqualTo(replacement));
			Assert.That(provider.RegisteredTags.ContainsKey(1), Is.True);
			Assert.That(changed, Is.EqualTo(1));
		}

		[Test]
		public void StaleMintCannotReplaceAnAnchorFromANewerMapImport()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			int oldGeneration = Generation;
			Guid newer = Guid.NewGuid();
			SavedAnchor(newer);
			Assert.That(Commit(Guid.NewGuid(), oldGeneration, saved), Is.False);
			Assert.That(RecordedGuid(), Is.EqualTo(newer));
		}

		[Test]
		public void MintMustStillReplaceTheSameAnchorEvenWithACurrentGeneration()
		{
			object saved = SavedAnchor(Guid.NewGuid());
			Guid newer = Guid.NewGuid();
			SavedAnchor(newer);
			Assert.That(Commit(Guid.NewGuid(), Generation, saved), Is.False);
			Assert.That(RecordedGuid(), Is.EqualTo(newer));
		}

		[Test]
		public void FreshTagWithoutASavedAnchorStillAcceptsItsFirstMint()
		{
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			Guid fresh = Guid.NewGuid();
			Assert.That(Commit(fresh, Generation, null), Is.True);
			Assert.That(RecordedGuid(), Is.EqualTo(fresh));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void InitialAnchorTargetAccountsForNativePoseAndRigCorrectionsDuringMint(bool rigMoved)
		{
			Pose tag = new(new Vector3(.5f, 1, 2), Quaternion.Euler(30, 35, 20));
			Pose anchor = new(new Vector3(.53f, 1.02f, 1.99f), Quaternion.Euler(0, 70, 0));
			Pose correction = new(new Vector3(2, 0, -3), Quaternion.Euler(0, 100, 0));
			Pose canonTag = MapSpaceFrame.Compose(correction, tag);
			provider.SetRegisteredTags(new[] { new TagConstraintData(1, canonTag) });
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			if (rigMoved) owner.transform.SetPositionAndRotation(new Vector3(-4, .2f, 3), Quaternion.Euler(0, -50, 0));
			Assert.That(Commit(Guid.NewGuid(), Generation, null, tag, canonTag, anchor), Is.True);
			var records = new List<TaggedAnchorConstraintData>(); provider.GetLocalAnchorConstraints(records);
			Assert.That(MapSpaceFrame.Near(records[0].canonPose, MapSpaceFrame.Compose(correction, anchor), .0001f, .01f), Is.True,
				"The target belongs to the actual anchor, not the requested tag pose.");
			var constraints = new List<ColocationConstraint>(); provider.GetColocationConstraints(constraints);
			Assert.That(ColocationFit.TryEvaluate(constraints, out _, out var residual, out var angle), Is.True);
			Assert.That(residual, Is.LessThan(.001f)); Assert.That(angle, Is.LessThan(.1f));
		}

		[Test]
		public void RecenterInvalidatesTheTrackingPoseCapturedByAPendingMint()
		{
			ProviderType.GetProperty("IsRunning").SetValue(provider, true);
			int generation = Generation;
			ProviderType.GetMethod("InvalidatePendingObservations", PrivateInstance).Invoke(provider, null);
			Assert.That(Commit(Guid.NewGuid(), generation, null), Is.False);
			Assert.That(provider.LocalAnchorCount, Is.Zero);
		}
	}
}
