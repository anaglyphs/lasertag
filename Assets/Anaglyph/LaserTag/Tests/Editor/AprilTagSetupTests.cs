using System;
using System.Reflection;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Interface;
using Anaglyph.Netcode.SyncVariables;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
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
			space = Guid.Parse(space.id), tagSizeCm = space.tagSizeCm, operatorManaged = true
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
		public void OperatorSetupRequiresHostingAndAllSetupRequiresASpace()
		{
			Property(bus, "IsSpawned", false);
			Assert.That(setup.Begin(), Is.False);
			Property(bus, "IsSpawned", true);
			documents.SpaceDocument.Unload();
			Assert.That(setup.Begin(), Is.False);
			Assert.That(setup.State.operation, Is.EqualTo(Guid.Empty));
		}

		[Test]
		public void StandaloneHeadsetCanStartMeasureRegisterAndFinishSetup()
		{
			Property(bus, "IsSpawned", false);
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
			Set(setup, "canAuthor", (Func<ulong, bool>)(_ => true));
			var previousMode = MapEditorTool.CurrentMode;
			try
			{
				Assert.That(setup.Begin(), Is.True);
				Assert.That(setup.IsGuidingHeadset, Is.True);
				Assert.That(setup.State.operatorManaged, Is.False);
				Assert.That(setup.CanEndFromHere, Is.True);
				Call(setup, "RefreshHeadsetMode");
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				Assert.That(setup.ContinueToRegistration(coordinator.EffectiveTagSizeCm), Is.True);
				Assert.That(setup.SizeConfirmed, Is.True);
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
				setup.ReturnToMeasurement();
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				space.SetTag(1, Pose.identity); documents.SpaceDocument.Load(space);
				Assert.That(setup.CanFinish, Is.True);
				Assert.That(setup.Finish(), Is.True);
				Assert.That(setup.IsActive, Is.False);
			}
			finally { MapEditorTool.SetMode(previousMode); }
		}

		[TestCase(true)]
		[TestCase(false)]
		public void SharedAlignmentButtonStartsSetupOnEitherPlatform(bool operatorMode)
		{
			Property(bus, "IsSpawned", operatorMode);
			Set(configuration, "role", operatorMode ? HeadsetConfiguration.DeviceRole.Operator : HeadsetConfiguration.DeviceRole.Headset);
			typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, coordinator);
			try
			{
				var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
					"Assets/Anaglyph/LaserTag/Interface/Shared/Game/AlignmentSettings.uxml").CloneTree();
				using var binder = new AlignmentSettingsBinder(root, operatorMode);
				var button = root.Q("tag-configuration-section").Q<Button>("start-apriltag-setup");
				Assert.That(button.enabledInHierarchy, Is.True);
				Assert.That(button.style.display.value, Is.EqualTo(DisplayStyle.Flex));
				if (operatorMode) ExpectBroadcast();
				typeof(Clickable).GetMethod("Invoke", Private).Invoke(button.clickable, new object[] { null });
				Assert.That(setup.IsActive, Is.True);
				Assert.That(setup.IsGuidingHeadset, Is.EqualTo(!operatorMode));
				Assert.That(setup.State.operatorManaged, Is.EqualTo(operatorMode));
			}
			finally { typeof(LaserTagMapCoordinator).GetProperty("Instance").SetValue(null, null); }
		}

		[Test]
		public void HeadsetCanStopItsOwnSetupButCannotStopOperatorSetup()
		{
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
			Receive(ActiveState);
			Assert.That(setup.CanEndFromHere, Is.False);
			setup.Cancel(); Assert.That(setup.IsActive, Is.True);
			var local = ActiveState; local.operatorManaged = false; Receive(local);
			Assert.That(setup.CanEndFromHere, Is.True);
			ExpectBroadcast(); setup.Cancel();
			Assert.That(setup.IsActive, Is.False);
		}

		[Test]
		public void RemoteSetupRequestsRequireCurrentContextAndAnEligibleHeadset()
		{
			var requestType = typeof(AprilTagSetupSession).GetNestedType("SetupRequest", BindingFlags.NonPublic);
			var request = Activator.CreateInstance(requestType);
			requestType.GetField("space").SetValue(request, coordinator.SessionSpaceId);
			requestType.GetField("context").SetValue(request, Guid.NewGuid());
			Call(setup, "OnRequest", 7UL, request);
			Assert.That(setup.IsActive, Is.False);
			requestType.GetField("context").SetValue(request, coordinator.ReferenceContext);
			Call(setup, "OnRequest", 8UL, request);
			Assert.That(setup.IsActive, Is.False);
			ExpectBroadcast(); Call(setup, "OnRequest", 7UL, request);
			Assert.That(setup.IsActive, Is.True);
			Assert.That(setup.State.operatorManaged, Is.True);
			requestType.GetField("operation").SetValue(request, setup.State.operation);
			requestType.GetField("action").SetValue(request, Enum.Parse(requestType.GetField("action").FieldType, "Cancel"));
			Call(setup, "OnRequest", 7UL, request);
			Assert.That(setup.IsActive, Is.True, "Operator setup still finishes on the PC.");
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
			Assert.That(coordinator.DescribeTagRegistrationBlocker(), Is.EqualTo(MenuCopy.Get("Map", "tag-setup.measure-first")));
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
		public void ConfirmedSizeStaysOnMeasurementUntilContinueAndBackStaysOnMeasurement()
		{
			var previousMode = MapEditorTool.CurrentMode;
			try
			{
				Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
				var command = ActiveState; command.sizeConfirmed = true; Receive(command);
				Call(setup, "RefreshHeadsetMode");
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				Assert.That(setup.ContinueToRegistration(command.tagSizeCm), Is.True);
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
				setup.ReturnToMeasurement();
				Call(setup, "RefreshHeadsetMode");
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				Assert.That(setup.ContinueToRegistration(command.tagSizeCm), Is.True);
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
			}
			finally { MapEditorTool.SetMode(previousMode); }
		}

		[Test]
		public void ContinueWaitsForTheRequestedSizeAndBackCancelsDelayedAcknowledgement()
		{
			var previousMode = MapEditorTool.CurrentMode;
			try
			{
				Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
				Set(setup, "canAuthor", (Func<ulong, bool>)(_ => true));
				var command = ActiveState; command.sizeConfirmed = true; Receive(command);
				float correctedSize = command.tagSizeCm + 5;
				Assert.That(setup.ContinueToRegistration(correctedSize), Is.True);
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				space.tagSizeCm = correctedSize; documents.SpaceDocument.Load(space);
				command.tagSizeCm = correctedSize; Receive(command);
				Call(setup, "RefreshHeadsetMode");
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.Tags));
				setup.ReturnToMeasurement();
				Receive(command); Call(setup, "RefreshHeadsetMode");
				Assert.That(MapEditorTool.CurrentMode, Is.EqualTo(MapEditorTool.Mode.MeasureTagSize));
				Assert.That(setup.CanMeasure, Is.True, "A confirmed size can still be corrected before tags are registered.");
				space.SetTag(1, Pose.identity); documents.SpaceDocument.Load(space);
				Assert.That(setup.CanMeasure, Is.False, "Registered poses keep the existing size lock.");
			}
			finally { MapEditorTool.SetMode(previousMode); }
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
