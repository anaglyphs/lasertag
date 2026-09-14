using System;
using System.Reflection;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode.SyncVariables;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Tests
{
	public class AprilTagSetupTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private LaserTagMapCoordinator coordinator;
		private HeadsetConfiguration configuration, previousConfiguration;
		private MapCoordinatorTestContext documents;
		private AprilTagSetupSession setup;
		private SyncBus bus;
		private SyncVariable<AprilTagSetupState> state;
		private MapSpace space;
		private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
		private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
		private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
		private static object Call(object target, string name, params object[] args) =>
			target.GetType().GetMethod(name, Private | BindingFlags.Public).Invoke(target, args);

		[SetUp]
		public void SetUp()
		{
			Assert.That(SyncBus.Active, Is.False);
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
			owner = new GameObject("AprilTag setup test"); owner.SetActive(false);
			coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			documents = new MapCoordinatorTestContext(coordinator);
			documents.ComposeReferences(owner.AddComponent<ColocationManager>());
			space = MapSpace.Create("Test room"); space.preferredColocationMethod = Method.AprilTag;
			documents.SpaceDocument.Load(space);
			previousConfiguration = HeadsetConfiguration.Instance;
			configuration = owner.AddComponent<HeadsetConfiguration>();
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Operator);
			typeof(HeadsetConfiguration).GetProperty("Instance").SetValue(null, configuration);
			setup = (AprilTagSetupSession)Activator.CreateInstance(typeof(AprilTagSetupSession), Private, null,
				new object[] { coordinator, (Func<ulong, bool>)(sender => sender == 7) }, null);
			Property(coordinator, "TagSetup", setup);
			state = (SyncVariable<AprilTagSetupState>)Get(setup, "state");
			setup.Register();
			owner.AddComponent<NetworkObject>(); bus = owner.AddComponent<SyncBus>();
			Property(bus, "IsSpawned", true); Property(bus, "HasAuthority", true);
			typeof(SyncBus).GetProperty("Current").SetValue(null, bus);
		}

		private AprilTagSetupState ActiveState => new()
		{
			operation = Guid.NewGuid(), context = coordinator.ReferenceContext,
			space = Guid.Parse(space.id), tagSizeCm = space.tagSizeCm
		};
		private void Receive(AprilTagSetupState value) => Call(state, "ApplyBroadcast", SyncBytes.Of(value));
		private static void ExpectBroadcast() => LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");

		[TearDown]
		public void TearDown()
		{
			Property(bus, "IsSpawned", false);
			typeof(SyncBus).GetProperty("Current").SetValue(null, null);
			setup.Dispose();
			typeof(HeadsetConfiguration).GetProperty("Instance").SetValue(null, previousConfiguration);
			documents.Dispose();
			Object.DestroyImmediate(owner);
		}

		[Test]
		public void SetupRequiresTheOperatorHostAndCurrentSpace()
		{
			Property(bus, "IsSpawned", false);
			Assert.That(setup.Begin(), Is.False);
			Property(bus, "IsSpawned", true);
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
			Assert.That(setup.Begin(), Is.False);
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Operator);
			documents.SpaceDocument.Unload();
			Assert.That(setup.Begin(), Is.False);
			Assert.That(setup.State.operation, Is.EqualTo(Guid.Empty));
		}

		[Test]
		public void SnapshotOnlyAppliesToItsSpaceAndReferenceVisit()
		{
			var command = ActiveState;
			Call(state, "ApplySnapshot", SyncBytes.Of(command)); Call(state, "FlushSnapshotEvents");
			Assert.That(setup.IsActive, Is.True);
			documents.MapDocument.Create(space.storageFrameId);
			Assert.That(setup.IsActive, Is.True, "A layout change keeps the same physical setup.");
			Property(documents.Visit, "ReferenceContext", Guid.NewGuid());
			Assert.That(setup.IsActive, Is.False);
			ExpectBroadcast(); setup.Tick();
			Assert.That(setup.State.operation, Is.EqualTo(Guid.Empty));
			Assert.That(command.Matches(Guid.NewGuid(), command.context), Is.False);
		}

		[Test]
		public void BeginningWithoutAHeadsetPersistsAprilTagSetupAndPublishesTheGuide()
		{
			ExpectBroadcast();
			Assert.That(setup.Begin(), Is.True);
			Assert.That(setup.IsActive, Is.True);
			Assert.That(setup.SizeConfirmed, Is.False);
			Assert.That(coordinator.CurrentSpace.hasPendingSetup, Is.True);
			Assert.That(coordinator.CurrentSpace.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
			Guid operation = setup.State.operation;
			Assert.That(setup.Begin(), Is.True);
			Assert.That(setup.State.operation, Is.EqualTo(operation));
		}

		[Test]
		public void MeasurementMustBeAcceptedByTheAuthorityForTheCurrentOperation()
		{
			var command = ActiveState; Receive(command);
			Assert.That(coordinator.DescribeTagRegistrationBlocker(), Is.EqualTo(MenuCopy.Get("Game", "tag-setup.measure-first")));
			Call(setup, "OnMeasurement", 8UL, command);
			Assert.That(setup.SizeConfirmed, Is.False);
			var stale = command; stale.operation = Guid.NewGuid();
			Call(setup, "OnMeasurement", 7UL, stale);
			Assert.That(setup.SizeConfirmed, Is.False);
			var wrongSize = command; wrongSize.tagSizeCm += 1;
			Call(setup, "OnMeasurement", 7UL, wrongSize);
			Assert.That(setup.SizeConfirmed, Is.False);
			ExpectBroadcast(); Call(setup, "OnMeasurement", 7UL, command);
			Assert.That(setup.SizeConfirmed, Is.True);
			space.tagSizeCm += 1; documents.SpaceDocument.Load(space);
			Assert.That(setup.SizeConfirmed, Is.False, "A replaced size needs another measurement.");
		}

		[Test]
		public void FinishRequiresRegisteredTagsAndCompletedAprilTagSelection()
		{
			Receive(ActiveState);
			Assert.That(setup.CanFinish, Is.False);
			space.SetTag(10, Pose.identity); space.hasPendingSetup = true;
			documents.SpaceDocument.Load(space);
			Assert.That(setup.CanFinish, Is.False);
			space.hasPendingSetup = false; space.preferredColocationMethod = Method.MetaSharedAnchor;
			documents.SpaceDocument.Load(space);
			Assert.That(setup.CanFinish, Is.False);
			space.preferredColocationMethod = Method.AprilTag; documents.SpaceDocument.Load(space);
			Assert.That(setup.CanFinish, Is.True);
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
			Assert.That(setup.CanFinish, Is.False);
		}

		[Test]
		public void FinishedSetupRejectsInFlightReferenceEdits()
		{
			var operation = Guid.NewGuid();
			object workflow = Get(coordinator, "referenceWorkflow");
			Property(workflow, "TagSetupOperation", (Func<Guid>)(() => Guid.Empty));
			var request = Activator.CreateInstance(typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.SpaceReferenceRequest"));
			request.GetType().GetField("context").SetValue(request, coordinator.ReferenceContext);
			request.GetType().GetField("setupOperation").SetValue(request, operation);
			Assert.That(Call(workflow, "ApplyReferenceRequest", 7UL, request), Is.False);
			Assert.That(coordinator.CurrentSpace.tags, Is.Empty);
		}

		[Test]
		public void SaveFailureKeepsSetupOpenAndRetryFinishesAfterSaving()
		{
			space.SetTag(10, Pose.identity); documents.SpaceDocument.Load(space);
			documents.SpaceDocument.MarkUsed();
			Receive(ActiveState);
			string directory = (string)Get(documents, "directory");
			string blocked = System.IO.Path.Combine(directory, "spaces", space.id + ".json.tmp");
			System.IO.Directory.CreateDirectory(blocked);
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("IOException|UnauthorizedAccessException"));
			Assert.That(setup.Finish(), Is.False);
			Assert.That(setup.IsActive, Is.True);
			System.IO.Directory.Delete(blocked);
			ExpectBroadcast();
			Assert.That(setup.Finish(), Is.True);
			Assert.That(setup.IsActive, Is.False);
			Assert.That(new MapSpaceStore(System.IO.Path.Combine(directory, "spaces")).TryGet(space.id, out var saved), Is.True);
			Assert.That(saved.tags.Count, Is.EqualTo(1));
			Assert.That(saved.preferredColocationMethod, Is.EqualTo(Method.AprilTag));
		}

		[Test]
		public void FinishingOrDisconnectingReleasesHeadsetEditing()
		{
			var command = ActiveState; Receive(command);
			var previousMode = MapEditorTool.CurrentMode;
			bool previouslyActive = MapEditor.MapEditor.IsActive;
			try
			{
				Set(setup, "guidingOperation", command.operation);
				MapEditor.MapEditor.SetActive(true);
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
				Receive(default);
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Move));
				Receive(command); Set(setup, "guidingOperation", command.operation);
				MapEditor.MapEditor.SetActive(true);
				Property(bus, "IsSpawned", false); setup.Tick();
				Assert.That(MapEditor.MapEditor.IsActive, Is.False);
			}
			finally { MapEditor.MapEditor.SetActive(previouslyActive); MapEditorTool.SetMode(previousMode); }
		}
	}
}
