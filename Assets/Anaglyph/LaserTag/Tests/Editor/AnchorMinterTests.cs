using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Tests
{
	public class AnchorMinterTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private readonly Guid map = Guid.NewGuid();
		private readonly Dictionary<ulong, HeadsetReadiness> headsets = new();
		private GameObject owner;
		private SyncBus previousBus;
		private SyncBus bus;
		private SpatialAnchorColocationConstraintProvider provider;
		private Guid assignment;
		private Guid context;

		[SetUp]
		public void SetUp()
		{
			previousBus = SyncBus.Current;
			owner = new GameObject("Anchor minter protocol test");
			owner.SetActive(false);
			owner.AddComponent<NetworkObject>();
			bus = owner.AddComponent<SyncBus>();
			typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, true);
			typeof(NetworkBehaviour).GetProperty("HasAuthority").SetValue(bus, true);
			typeof(SyncBus).GetProperty("Current").SetValue(null, bus);
			provider = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			typeof(SpatialAnchorColocationConstraintProvider).GetProperty("IsRunning").SetValue(provider, true);
			assignment = Guid.NewGuid();
			context = Guid.NewGuid();
			SetValue("minter", new AnchorMinterAssignment { assigned = true, clientId = 7, generation = assignment });
			SetValue("referenceContext", context);
			headsets.Clear();
		}

		[TearDown]
		public void TearDown()
		{
			typeof(SpatialAnchorColocationConstraintProvider).GetProperty("IsRunning").SetValue(provider, false);
			typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(bus, false);
			typeof(SyncBus).GetProperty("Current").SetValue(null, previousBus);
			UnityEngine.Object.DestroyImmediate(owner);
		}

		private HeadsetReadiness Ready(bool aligned = false) => new()
		{
			mapId = map, method = ColocationManager.ColocationMethod.MetaSharedAnchor,
			hasAnchorRuntime = true, supportsSharedAnchors = true, isHeadTracked = true, isFocused = true,
			referenceFrameTrusted = aligned
		};

		private ulong? Choose(ulong? current = null, bool managed = true, ulong sessionOwner = 0) =>
			AnchorMinterPolicy.Select(current, sessionOwner, managed, map,
				ColocationManager.ColocationMethod.MetaSharedAnchor, headsets);

		[Test]
		public void BlankSessionSelectsOneHeadsetBeforeAlignmentAndKeepsItStable()
		{
			headsets[8] = Ready();
			headsets[7] = Ready();
			Assert.That(Choose(), Is.EqualTo(7UL));
			headsets[2] = Ready();
			Assert.That(Choose(7), Is.EqualTo(7UL));
		}

		[Test]
		public void DisconnectAndLossOfReadinessSelectAnotherHeadset()
		{
			headsets[7] = Ready();
			headsets[8] = Ready();
			headsets.Remove(7);
			Assert.That(Choose(7), Is.EqualTo(8UL));
			var sleeping = Ready(); sleeping.isFocused = false;
			headsets[8] = sleeping;
			Assert.That(Choose(8), Is.Null);
			headsets[7] = Ready();
			Assert.That(Choose(8), Is.EqualTo(7UL));
		}

		[Test]
		public void ExistingFramePrefersLocalizedHeadsetOverOneUnableToLoadReferences()
		{
			headsets[7] = Ready();
			headsets[8] = Ready(true);
			Assert.That(Choose(7), Is.EqualTo(8UL));
			headsets[2] = Ready(true);
			Assert.That(Choose(8), Is.EqualTo(8UL));
		}

		[Test]
		public void HeadsetSessionAlwaysUsesSessionOwnerIncludingClientZero()
		{
			headsets[0] = Ready();
			headsets[7] = Ready(true);
			Assert.That(Choose(7, managed: false), Is.EqualTo(0UL));
			headsets.Remove(0);
			Assert.That(Choose(7, managed: false), Is.Null);
		}

		[Test]
		public void OperatorUnsupportedRuntimeAndOldMapOrMethodAreIneligible()
		{
			var status = Ready(); status.isOperator = true; headsets[0] = status;
			status = Ready(); status.supportsSharedAnchors = false; headsets[1] = status;
			status = Ready(); status.mapId = Guid.NewGuid(); headsets[2] = status;
			status = Ready(); status.method = ColocationManager.ColocationMethod.AprilTag; headsets[3] = status;
			Assert.That(Choose(), Is.Null);
		}

		[Test]
		public void EmptyConstraintSetMintsAndEveryCanonicalPositionParticipatesInProximity()
		{
			Assert.That(SpatialAnchorColocationConstraintProvider.HasNearbyAnchor(Array.Empty<AnchorConstraintState>(), Vector3.zero, 3), Is.False);
			var near = new AnchorConstraintState { canonPose = new Pose(Vector3.right, Quaternion.identity) };
			var far = new AnchorConstraintState { canonPose = new Pose(Vector3.right * 9, Quaternion.identity) };
			Assert.That(SpatialAnchorColocationConstraintProvider.HasNearbyAnchor(new[] { far, near }, Vector3.zero, 3), Is.True);
			Assert.That(SpatialAnchorColocationConstraintProvider.HasNearbyAnchor(new[] { near, far }, Vector3.zero, 3), Is.True);
			Assert.That(SpatialAnchorColocationConstraintProvider.HasNearbyAnchor(new[] { far }, Vector3.zero, 3), Is.False);
		}

		[Test]
		public void PcAuthorityAcceptsOnlyTheAssignedMinterWithoutHavingAnAnchorRuntime()
		{
			Assert.That(provider.IsAvailable, Is.False);
			object proposal = Proposal();
			Assert.That(Accept(7, proposal), Is.True);
			Assert.That(Accept(8, proposal), Is.False);
			provider.ValidateRemoteMint = _ => false;
			Assert.That(Accept(7, proposal), Is.False);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void OperatorKeepsSharedAnchorPreferenceWithoutItsOwnRuntime(bool hasReferences)
		{
			PropertyInfo instance = typeof(HeadsetConfiguration).GetProperty("Instance");
			object previous = instance.GetValue(null);
			try
			{
				var configuration = owner.AddComponent<HeadsetConfiguration>();
				Set(configuration, "role", HeadsetConfiguration.DeviceRole.Operator);
				instance.SetValue(null, configuration);
				var manager = owner.AddComponent<ColocationManager>();
				Set(manager, "spatialAnchorColocationProvider", provider);
				Set(manager, "mapPreference", ColocationManager.ColocationMethod.MetaSharedAnchor);
				Set(manager, "mapHasAnchors", hasReferences);
				Set(manager, "mapHasTags", hasReferences);
				Assert.That(manager.PreferredSessionMethod, Is.EqualTo(ColocationManager.ColocationMethod.MetaSharedAnchor));
			}
			finally { instance.SetValue(null, previous); }
		}

		[Test]
		public void PcRunsAndStopsTheProviderWithoutAnXrRig()
		{
			PropertyInfo rigInstance = Type.GetType("Anaglyph.XR.MainXRRig, Anaglyph.XR", true).GetProperty("Instance");
			object previousRig = rigInstance.GetValue(null);
			var lifetime = new CancellationTokenSource();
			try
			{
				rigInstance.SetValue(null, null);
				typeof(SyncBus).GetProperty("Current").SetValue(null, null);
				typeof(SpatialAnchorColocationConstraintProvider).GetProperty("IsRunning").SetValue(provider, false);
				Set(provider, "lifetimeCtknSrc", lifetime);
				var manager = owner.AddComponent<ColocationManager>();
				Set(manager, "colocator", owner.AddComponent<Colocator>());
				Set(manager, "spatialAnchorColocationProvider", provider);
				Set(manager, "mapLoaded", true);
				manager.GetType().GetMethod("UpdateProvider", Private).Invoke(manager, null);
				Assert.That(provider.IsRunning, Is.True);
				Set(manager, "mapLoaded", false);
				manager.GetType().GetMethod("UpdateProvider", Private).Invoke(manager, null);
				Assert.That(provider.IsRunning, Is.False);
			}
			finally
			{
				provider.StopProviding();
				lifetime.Dispose();
				rigInstance.SetValue(null, previousRig);
				typeof(SyncBus).GetProperty("Current").SetValue(null, bus);
			}
		}

		[Test]
		public void ReassignmentAndMapOrMethodContextChangesRejectLateMints()
		{
			object proposal = Proposal();
			SetValue("minter", new AnchorMinterAssignment { assigned = true, clientId = 7, generation = Guid.NewGuid() });
			Assert.That(Accept(7, proposal), Is.False);
			SetValue("minter", new AnchorMinterAssignment { assigned = true, clientId = 7, generation = assignment });
			SetValue("referenceContext", Guid.NewGuid());
			Assert.That(Accept(7, proposal), Is.False);
		}

		[Test]
		public void NoAssignmentOrLostAuthorityCannotAcceptMintResults()
		{
			object proposal = Proposal();
			SetValue("minter", new AnchorMinterAssignment { clientId = 7, generation = assignment });
			Assert.That(Accept(7, proposal), Is.False);
			SetValue("minter", new AnchorMinterAssignment { assigned = true, clientId = 7, generation = assignment });
			typeof(NetworkBehaviour).GetProperty("HasAuthority").SetValue(bus, false);
			Assert.That(Accept(7, proposal), Is.False);
		}

		[Test]
		public void StoppedProviderTaggedMapAndInvalidPosesCannotCommitRoamingAnchors()
		{
			object proposal = Proposal();
			provider.RoamingMintEnabled = false;
			Assert.That(Accept(7, proposal), Is.False);
			provider.RoamingMintEnabled = true;
			Set(proposal, "canon", new Pose(new Vector3(float.NaN, 0, 0), Quaternion.identity));
			Assert.That(Accept(7, proposal), Is.False);
			Set(proposal, "canon", new Pose(Vector3.zero, new Quaternion()));
			Assert.That(Accept(7, proposal), Is.False);
			Set(proposal, "canon", Pose.identity);
			typeof(SpatialAnchorColocationConstraintProvider).GetProperty("IsRunning").SetValue(provider, false);
			Assert.That(Accept(7, proposal), Is.False);
		}

		[Test]
		public void RemoteTagRealizationCanBePreparedWithoutExistingInPcMap()
		{
			object proposal = Proposal();
			Set(proposal, "bindingId", 42);
			SetValue("preparation", Get(proposal, "operation"));
			provider.ValidatePreparedAnchor = (_, data) => data.bindingId == 42;
			Invoke("OnAnchorPrepared", 7UL, proposal);
			var prepared = (List<AnchorConstraintData>)Get(provider, "preparedConstraints");
			Assert.That(prepared.Count, Is.EqualTo(1));
			Assert.That(prepared[0].guid, Is.EqualTo(Get(proposal, "guid")));
			Assert.That(provider.Constraints.Count, Is.Zero, "Preparation must not publish before the method commits.");
			Invoke("OnAnchorPrepared", 7UL, proposal);
			Assert.That(prepared.Count, Is.EqualTo(1), "Duplicate messages cannot duplicate references.");
		}

		[Test]
		public void PreparationRejectsUnassignedHeadsetsAndLateOperations()
		{
			object proposal = Proposal();
			SetValue("preparation", Get(proposal, "operation"));
			Invoke("OnAnchorPrepared", 8UL, proposal);
			Assert.That((IList)Get(provider, "preparedConstraints"), Is.Empty);
			SetValue("referenceContext", Guid.NewGuid());
			Invoke("OnAnchorPrepared", 7UL, proposal);
			Assert.That((IList)Get(provider, "preparedConstraints"), Is.Empty);
		}

		[Test]
		public void FailedPreparationDiscardsUploadedCandidatesWithoutPublishingThem()
		{
			object proposal = Proposal();
			object operation = Get(proposal, "operation");
			SetValue("preparation", operation);
			Invoke("OnAnchorPrepared", 7UL, proposal);
			object reply = Activator.CreateInstance(provider.GetType().GetNestedType("AnchorReply", BindingFlags.NonPublic));
			Set(reply, "id", Get(operation, "id"));
			Set(reply, "accepted", false);
			Invoke("OnPreparationDone", 7UL, reply);
			Assert.That((IList)Get(provider, "preparedConstraints"), Is.Empty);
			Assert.That(Get(provider, "preparationComplete"), Is.True);
			Assert.That(provider.Constraints.Count, Is.Zero);
			Invoke("OnAnchorPrepared", 7UL, proposal);
			Assert.That((IList)Get(provider, "preparedConstraints"), Is.Empty);
		}

		[Test]
		public void LateJoinSnapshotCarriesTheAssignmentAndReferenceContextTogether()
		{
			var other = owner.AddComponent<SpatialAnchorColocationConstraintProvider>();
			foreach (string name in new[] { "minter", "referenceContext" })
			{
				object source = Get(provider, name);
				object target = Get(other, name);
				object bytes = source.GetType().GetMethod("SerializeSnapshot", Private).Invoke(source, null);
				target.GetType().GetMethod("ApplySnapshot", Private).Invoke(target, new[] { bytes });
			}
			Assert.That(other.Minter.clientId, Is.EqualTo(7UL));
			Assert.That(other.Minter.generation, Is.EqualTo(assignment));
			Assert.That(Get(Get(other, "referenceContext"), "current"), Is.EqualTo(context));
		}

		private object Proposal()
		{
			Type type = typeof(SpatialAnchorColocationConstraintProvider);
			object operation = Activator.CreateInstance(type.GetNestedType("AnchorOperation", BindingFlags.NonPublic));
			Set(operation, "id", Guid.NewGuid()); Set(operation, "assignment", assignment); Set(operation, "context", context);
			object proposal = Activator.CreateInstance(type.GetNestedType("AnchorProposal", BindingFlags.NonPublic));
			Set(proposal, "operation", operation); Set(proposal, "guid", Guid.NewGuid());
			Set(proposal, "canon", Pose.identity); Set(proposal, "bindingId", -1);
			return proposal;
		}
		private bool Accept(ulong sender, object proposal) => (bool)Invoke("CanAcceptMint", sender, proposal);
		private object Invoke(string name, params object[] args) => provider.GetType().GetMethod(name, Private).Invoke(provider, args);
		private void SetValue(string name, object value) => Set(Get(provider, name), "current", value);
		private static object Get(object owner, string name) => owner.GetType().GetField(name, Private | BindingFlags.Public).GetValue(owner);
		private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private | BindingFlags.Public).SetValue(owner, value);
	}
}
