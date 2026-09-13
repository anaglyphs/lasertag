using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;
using Object = UnityEngine.Object;

namespace Anaglyph.LaserTag.Tests
{
	public class MapAlignmentRequestTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject owner;
		private LaserTagMapCoordinator coordinator;
		private ColocationManager manager;
		private HeadsetConfiguration configuration, previousConfiguration;
		private MapSpaceStore previousStore;
		private MapStore previousMapStore;
		private MapSpaceManager spaces;
		private IDisposable alignment;
		private string directory;
		private Dictionary<ulong, PlayerAvatar> previousPlayers;
		private SyncBus bus;
		private readonly List<UserError> errors = new();
		private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
		private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
		private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);
		private static void Static(Type type, string name, object value) => type.GetProperty(name).SetValue(null, value);
		private static object Create(string name, params object[] args) =>
			Activator.CreateInstance(typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps." + name, true), args);
		private object Call(string name, params object[] args) => typeof(LaserTagMapCoordinator).GetMethod(name, Private).Invoke(coordinator, args);

		[SetUp]
		public void SetUp()
		{
			Assert.That(SyncBus.Active, Is.False);
			Assert.That(LaserTagMapCoordinator.Instance, Is.Null);
			Assert.That(ColocationManager.Instance, Is.Null);
			previousStore = MapSpaceStore.Default;
			previousMapStore = MapStore.Default;
			directory = Path.Combine(Path.GetTempPath(), "lasertag-alignment-request-" + Guid.NewGuid().ToString("N"));
			Static(typeof(MapSpaceStore), "Default", new MapSpaceStore(Path.Combine(directory, "spaces")));
			previousConfiguration = HeadsetConfiguration.Instance;
			previousPlayers = PlayerAvatar.All;
			Static(typeof(PlayerAvatar), "All", new Dictionary<ulong, PlayerAvatar>());
			owner = new GameObject("Alignment request test"); owner.SetActive(false);
			configuration = owner.AddComponent<HeadsetConfiguration>();
			Set(configuration, "role", HeadsetConfiguration.DeviceRole.Operator);
			Static(typeof(HeadsetConfiguration), "Instance", configuration);
			coordinator = owner.AddComponent<LaserTagMapCoordinator>();
			manager = owner.AddComponent<ColocationManager>(); manager.ManagedSelection = true;
			Set(manager, "spatialAnchorColocationProvider", owner.AddComponent<SpatialAnchorColocationConstraintProvider>());
			Set(manager, "aprilTagColocationProvider", owner.AddComponent<AprilTagColocationConstraintProvider>());
			var mapStore = new MapStore(Path.Combine(directory, "maps"));
			Static(typeof(MapStore), "Default", mapStore);
			spaces = new MapSpaceManager(MapSpaceStore.Default); spaces.Load(MapSpace.Create("Editor room"));
			Set(coordinator, "spaces", spaces); Set(coordinator, "maps", new MapManager(mapStore));
			Set(coordinator, "colocationManager", manager);
			Set(coordinator, "catalog", new MapSpaceCatalog(mapStore, MapSpaceStore.Default, directory));
			Set(coordinator, "references", Create("MapSpaceColocationAdapter", manager));
			alignment = (IDisposable)Create("MapSpaceAlignmentController", manager, (Func<bool>)(() => false));
			Set(coordinator, "alignment", alignment);
			Set(coordinator, "transitions", Create("MapSpaceTransitionSync"));
			Set(coordinator, "session", Create("MapSessionSync"));
			Static(typeof(LaserTagMapCoordinator), "Instance", coordinator);
			Static(typeof(ColocationManager), "Instance", manager);
			errors.Clear(); UserErrors.Raised += OnError;
		}
		private void OnError(UserError error) => errors.Add(error);

		private void WithSimulationRestore(Action test)
		{
			Assert.That(UnityEngine.XR.XRSettings.enabled, Is.False);
			var rigType = Type.GetType("Anaglyph.XR.MainXRRig, Anaglyph.XR", true);
			var previousRig = rigType.GetProperty("Instance").GetValue(null);
			var rig = owner.AddComponent(rigType); rigType.GetField("trackingSpace").SetValue(rig, owner.transform);
			Static(rigType, "Instance", rig);
			using var anchorLifetime = new System.Threading.CancellationTokenSource();
			Set(manager.AnchorProvider, "lifetimeCtknSrc", anchorLifetime);
			var tracker = manager.TagProvider.GetType().GetField("tagTracker", Private);
			tracker.SetValue(manager.TagProvider, owner.AddComponent(tracker.FieldType));
			Set(manager, "systemDeterminedProvider", new SystemDeterminedColocationConstraintProvider(owner.transform));
			Set(manager, "twoAprilTagProvider", new TwoAprilTagColocationConstraintProvider(manager.TagProvider, owner.transform, () => true));
			Set(coordinator, "objects", Create("MapObjectDirector", null, (Action)(() => { }), (Func<bool>)(() => false), (Func<MapSpaceFrame>)(() => spaces.Frame)));
			Set(coordinator, "discovery", Create("MapSpaceDiscovery", MapSpaceStore.Default, manager.AnchorProvider, 1f, (Func<bool>)(() => false)));
			spaces.Unload();
			try { test(); }
			finally { manager.AnchorProvider.StopProviding(); manager.TwoTagProvider.StopProviding(); Static(rigType, "Instance", previousRig); }
		}

		[TestCase(Method.AprilTag)]
		[TestCase(Method.MetaSharedAnchor)]
		[TestCase(Method.TwoAprilTags)]
		[TestCase(Method.SystemDetermined)]
		public void SimulationRestartRestoresTheSavedMethodAndItsDetectionService(Method method)
		{
			WithSimulationRestore(() => {
				var space = MapSpace.Create("Previously aligned room"); space.preferredColocationMethod = method;
				space.SetTag(1, Pose.identity); space.SetTag(2, new Pose(Vector3.forward, Quaternion.identity));
				space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
				var map = new GameMap { id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"), name = "Saved layout", storageFrameId = space.storageFrameId };
				space.mapIds.Add(map.id); Assert.That(MapStore.Default.Save(map), Is.True);
				Assert.That(MapSpaceStore.Default.Save(space), Is.True);
				MapSpaceStore.Default.Refresh(); MapStore.Default.Refresh();
				Assert.That(coordinator.RestoreLastMapForSimulation(), Is.True);
				Assert.That(coordinator.CurrentSpace.id, Is.EqualTo(space.id));
				Assert.That(coordinator.CurrentMap.id, Is.EqualTo(map.id));
				Assert.That(coordinator.CurrentSpace.preferredColocationMethod, Is.EqualTo(method));
				Assert.That(manager.ActiveMethod, Is.EqualTo(method), "A restored space must not be overridden with the system origin.");
				Assert.That(manager.SelectedMethod, Is.EqualTo(method));
				Assert.That(manager.TagProvider.RegisteredTags.Count, Is.EqualTo(2));
				Assert.That(manager.TagProvider.IsRunning, Is.EqualTo(method == Method.AprilTag));
				Assert.That(manager.TagProvider.IsDetecting, Is.EqualTo(method is Method.AprilTag or Method.TwoAprilTags));
				Assert.That(coordinator.RestoreLastMapForSimulation(), Is.True, "The auto-host retry is idempotent.");
				Assert.That(manager.ActiveMethod, Is.EqualTo(method));
			});
		}

		[Test]
		public void NewSimulationSpacePersistsTheSameMethodItActivates()
		{
			WithSimulationRestore(() => {
				Assert.That(coordinator.RestoreLastMapForSimulation(), Is.True);
				Assert.That(manager.ActiveMethod, Is.EqualTo(Method.SystemDetermined));
				Assert.That(manager.SelectedMethod, Is.EqualTo(Method.SystemDetermined));
				Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(coordinator.CurrentSpace.id, out var saved), Is.True);
				Assert.That(saved.preferredColocationMethod, Is.EqualTo(Method.SystemDetermined));
				Assert.That(saved.HasReferenceBasedData, Is.False);
			});
		}

		[TearDown]
		public void TearDown()
		{
			UserErrors.Raised -= OnError;
			if (bus) { Property(bus, "IsSpawned", false); Static(typeof(SyncBus), "Current", null); }
			alignment?.Dispose();
			Static(typeof(LaserTagMapCoordinator), "Instance", null);
			Static(typeof(ColocationManager), "Instance", null);
			Static(typeof(HeadsetConfiguration), "Instance", previousConfiguration);
			Static(typeof(PlayerAvatar), "All", previousPlayers);
			if (owner) Object.DestroyImmediate(owner);
			Static(typeof(MapSpaceStore), "Default", previousStore);
			Static(typeof(MapStore), "Default", previousMapStore);
			if (Directory.Exists(directory)) Directory.Delete(directory, true);
		}

		[Test]
		public void OperatorCanRequestFirstTagsWithoutAnAvailableHeadsetOrAnchorFrame()
		{
			var original = coordinator.CurrentSpace;
			Assert.That(manager.ActiveMethod, Is.EqualTo(Method.MetaSharedAnchor));
			Assert.That(coordinator.SetPreferredColocationMethod(Method.AprilTag), Is.True);
			Assert.That(errors, Is.Empty);
			Assert.That(coordinator.WaitingForAlignmentAuthor, Is.True);
			Assert.That(coordinator.IsChangingColocation, Is.False);
			Assert.That(coordinator.CurrentSpace.HasReferenceBasedData, Is.False);
			Assert.That(coordinator.CurrentSpace.canonicalFrameId, Is.EqualTo(original.canonicalFrameId));
			Assert.That(coordinator.CurrentSpace.preferredColocationMethod, Is.EqualTo(Method.MetaSharedAnchor), "Selection is committed after validation.");
			Assert.That(MapSpaceStore.Default.TryGet(original.id, out var saved), Is.True);
			Assert.That(saved.hasPendingSetup, Is.True);
			Assert.That(saved.pendingSetupMethod, Is.EqualTo(Method.AprilTag));

			var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Anaglyph/LaserTag/Interface/Shared/Game/AlignmentSettings.uxml").CloneTree();
			using var binder = new AlignmentSettingsBinder(root); binder.Refresh();
			Assert.That(binder.DisplayedMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(binder.Status, Is.EqualTo(MenuCopy.Get("Game", "alignment.waiting-for-headset")));
			Assert.That(root.Q<DropdownField>("colocation-method-field").value, Is.EqualTo(MenuCopy.Get("Game", "alignment.tags")));
			coordinator.CancelAlignmentTransition(); binder.Refresh();
			Assert.That(coordinator.WaitingForAlignmentAuthor, Is.False);
			Assert.That(binder.DisplayedMethod, Is.EqualTo(Method.MetaSharedAnchor));
			Assert.That(MapSpaceStore.Default.TryGet(original.id, out saved) && !saved.hasPendingSetup, Is.True);
		}

		[TestCase(Method.MetaSharedAnchor, false, false, false)]
		[TestCase(Method.SystemDetermined, false, false, false)]
		[TestCase(Method.AprilTag, true, true, false)]
		[TestCase(Method.TwoAprilTags, true, false, true)]
		public void AlignmentSettingsExposeOnlyTheSelectedMethodsControls(Method method, bool size, bool registrations, bool pair)
		{
			var space = coordinator.CurrentSpace; space.preferredColocationMethod = method;
			spaces.Load(space);
			var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Anaglyph/LaserTag/Interface/Shared/Game/AlignmentSettings.uxml").CloneTree();
			using var binder = new AlignmentSettingsBinder(root, operatorMode: true); binder.Refresh();
			Assert.That(root.Q("tag-configuration-section").ClassListContains("map-field-hidden"), Is.EqualTo(!size));
			Assert.That(root.Q<Label>("tag-status").style.display.value, Is.EqualTo(registrations ? DisplayStyle.Flex : DisplayStyle.None));
			Assert.That(root.Q<Button>("unregister-all-tags-button").ClassListContains("map-field-hidden"), Is.EqualTo(!registrations));
			Assert.That(root.Q<Button>("choose-two-tag-pair").ClassListContains("map-field-hidden"), Is.EqualTo(!pair));
		}

		private PlayerHeadsetStatus RemoteHeadset(out HeadsetReadiness readiness)
		{
			var avatar = owner.AddComponent<PlayerAvatar>();
			var status = owner.GetComponent<PlayerHeadsetStatus>();
			Property(avatar, "HeadsetStatus", status); Property(avatar, "IsSpawned", true);
			PlayerAvatar.All[7] = avatar;
			readiness = new HeadsetReadiness {
				isFocused = true, isHeadTracked = true, activeMethod = Method.MetaSharedAnchor,
				spaceId = coordinator.SessionSpaceId, frameId = coordinator.CanonicalFrameId,
				referenceContext = coordinator.ReferenceContext
			};
			Report(status, readiness); return status;
		}
		private static void Report(PlayerHeadsetStatus status, HeadsetReadiness readiness) =>
			((NetworkVariable<HeadsetReadiness>)Get(status, "readinessSync")).Value = readiness;
		private bool Ready(ulong sender = 7) => (bool)Call("ReadyForReferenceWork", sender, true, Method.AprilTag);
		private object TagRequest(int id, Pose pose, int generation = 0)
		{
			var type = typeof(LaserTagMapCoordinator).GetNestedType("ReferenceRequest", BindingFlags.NonPublic);
			var request = Activator.CreateInstance(type);
			type.GetField("context").SetValue(request, coordinator.ReferenceContext);
			type.GetField("operation").SetValue(request, coordinator.AlignmentTransition.Operation);
			type.GetField("trackingGeneration").SetValue(request, generation);
			type.GetField("tag").SetValue(request, id); type.GetField("pose").SetValue(request, pose);
			return request;
		}

		[TestCase(false)]
		[TestCase(true)]
		public void AcceptedRegistrationIsSavedBeforePublicationAndSurvivesAnInterruptedHandoff(bool established)
		{
			var space = coordinator.CurrentSpace;
			space.canonicalFromStorage = new Pose(new Vector3(3, 0, 5), Quaternion.Euler(0, 90, 0));
			if (established) space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
			spaces.Load(space);
			var status = RemoteHeadset(out var readiness); readiness.referenceFrameTrusted = established; Report(status, readiness);
			Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
			bool persisted = false;
			void OnSaved()
			{
				Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(space.id, out var disk), Is.True);
				Assert.That(disk.TryGetTag(7, out var tag), Is.True, "Registered must mean the authority has saved the tag.");
				Assert.That(MapSpaceFrame.Near(tag.canonPose, Pose.identity), Is.True, "Save in storage coordinates.");
				Assert.That(manager.TagProvider.RegisteredTags.ContainsKey(7), Is.False, "Save must precede provider publication.");
				persisted = true;
			}
			MapSpaceStore.Default.Changed += OnSaved;
			try { Assert.That((bool)Call("ApplyReferenceRequest", 7UL, TagRequest(7, space.Frame.ToCanonical(Pose.identity))), Is.True); }
			finally { MapSpaceStore.Default.Changed -= OnSaved; }
			Assert.That(persisted, Is.True);
			Assert.That(manager.TagProvider.RegisteredTags.ContainsKey(7), Is.True);
			Assert.That(coordinator.AlignmentTransition.CreatesReferences, Is.False);
			Assert.That(coordinator.AlignmentTransition.Ready, Is.False, "No headset validation has been supplied.");
			Assert.That(manager.ActiveMethod, Is.EqualTo(Method.MetaSharedAnchor), "The old source remains active during validation.");
			readiness.referenceFrameTrusted = false; Report(status, readiness);
			Assert.That((bool)Call("ApplyReferenceRequest", 7UL, TagRequest(8, Pose.identity)), Is.False,
				"Saving the first tag does not grant an unaligned headset permission to author more references.");
			Call("CancelTransitionInternal");
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(space.id, out var reopened), Is.True);
			Assert.That(reopened.TryGetTag(7, out _), Is.True);
			Assert.That(reopened.hasPendingSetup, Is.True);
			Assert.That(reopened.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(reopened.initializationPending, Is.False);
			spaces.Load(reopened);
			readiness.referenceFrameTrusted = false; Report(status, readiness);
			Call("ResumePendingSetup");
			Assert.That(coordinator.AlignmentTransition.Target, Is.EqualTo(Method.AprilTag));
			Assert.That(coordinator.AlignmentTransition.CreatesReferences, Is.False, "The saved target can recover without the old source.");
			Assert.That(manager.TagProvider.RegisteredTags.ContainsKey(7), Is.True);
		}

		[TestCase("author")]
		[TestCase("trackingGeneration")]
		[TestCase("operation")]
		[TestCase("context")]
		[TestCase("focus")]
		[TestCase("source")]
		public void ImmediateRegistrationPersistenceStillRejectsObsoleteOrUnauthorizedRequests(string invalid)
		{
			var space = coordinator.CurrentSpace;
			space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1); spaces.Load(space);
			var status = RemoteHeadset(out var readiness); readiness.referenceFrameTrusted = true; Report(status, readiness);
			Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
			var request = TagRequest(7, Pose.identity);
			if (invalid == "trackingGeneration") request.GetType().GetField(invalid).SetValue(request, 1);
			if (invalid is "operation" or "context") request.GetType().GetField(invalid).SetValue(request, Guid.NewGuid());
			if (invalid == "focus") readiness.isFocused = false;
			if (invalid == "source") { readiness.activeMethod = Method.SystemDetermined; readiness.referenceFrameTrusted = false; }
			Report(status, readiness); PlayerAvatar.All[8] = PlayerAvatar.All[7];
			Assert.That((bool)Call("ApplyReferenceRequest", invalid == "author" ? 8UL : 7UL, request), Is.False);
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(space.id, out var saved), Is.True);
			Assert.That(saved.tags, Is.Empty); Assert.That(manager.TagProvider.RegisteredTags, Is.Empty);
		}

		[Test]
		public void FailedRegistrationSaveDoesNotPublishOrChangeTheCandidate()
		{
			RemoteHeadset(out _);
			Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
			var blocker = Path.Combine(directory, "not-a-directory"); File.WriteAllText(blocker, "blocked");
			Static(typeof(MapSpaceStore), "Default", new MapSpaceStore(Path.Combine(blocker, "spaces")));
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("IOException|DirectoryNotFoundException"));
			LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("IOException|DirectoryNotFoundException"));
			Assert.That((bool)Call("ApplyReferenceRequest", 7UL, TagRequest(7, Pose.identity)), Is.False);
			Assert.That(coordinator.CurrentSpace.tags, Is.Empty);
			Assert.That(((MapSpace)Get(coordinator, "staged")).tags, Is.Empty);
			Assert.That(manager.TagProvider.RegisteredTags, Is.Empty);
			Assert.That(coordinator.AlignmentTransition.CreatesReferences, Is.True);
		}

		[Test]
		public void SimulatedHeadsetCanEstablishTagsWithoutSharingOrPriorAlignment()
		{
			Assert.That(coordinator.SetPreferredColocationMethod(Method.AprilTag), Is.True);
			var status = RemoteHeadset(out var readiness);
			Assert.That(readiness.CanMintSharedAnchors, Is.False);
			owner.AddComponent<NetworkObject>(); bus = owner.AddComponent<SyncBus>();
			Property(bus, "IsSpawned", true); Property(bus, "HasAuthority", true); Static(typeof(SyncBus), "Current", bus);
			readiness.isFocused = false; Report(status, readiness);
			Call("ResumePendingSetup");
			Assert.That(coordinator.WaitingForAlignmentAuthor, Is.True);
			readiness.isFocused = true; Report(status, readiness);
			Assert.That(Ready(), Is.True);
			// This fixture exercises authority selection and staging without a network transport.
			for (int i = 0; i < 4; i++) LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
			Call("ResumePendingSetup");
			Assert.That(coordinator.IsChangingColocation, Is.True);
			Assert.That(coordinator.AlignmentTransition.Bootstrap, Is.True);
			Assert.That(coordinator.AlignmentTransition.Source, Is.EqualTo(Method.MetaSharedAnchor));
			Assert.That(Ready(), Is.True);
			PlayerAvatar.All[8] = PlayerAvatar.All[7];
			Assert.That(Ready(8), Is.False, "Only the assigned headset may initialize the frame.");
			readiness.isFocused = false; Report(status, readiness);
			Assert.That(Ready(), Is.False);
			readiness.isFocused = true; readiness.referenceContext = Guid.NewGuid(); Report(status, readiness);
			Assert.That(Ready(), Is.False, "Old readiness cannot authorize a new frame.");
		}

		[TestCase(false)]
		[TestCase(true)]
		public void ExistingActiveOrRetainedReferencesRequireTrustedSource(bool retained)
		{
			var space = coordinator.CurrentSpace;
			var anchor = new MapAnchorEntry { guid = Guid.NewGuid().ToString("N"), canonPose = Pose.identity, tagId = -1 };
			if (retained) space.retainedReferences.Add(new() { anchors = new() { anchor } });
			else space.anchors.Add(anchor);
			spaces.Load(space);
			Assert.That(coordinator.SetPreferredColocationMethod(Method.AprilTag), Is.True);
			Assert.That(coordinator.WaitingForAlignmentAuthor, Is.True);
			Assert.That(errors, Is.Empty);
			var status = RemoteHeadset(out var readiness);
			Assert.That(Ready(), Is.False);
			readiness.referenceFrameTrusted = true; Report(status, readiness);
			Assert.That(Ready(), Is.True);
			readiness.activeMethod = Method.SystemDetermined; Report(status, readiness);
			Assert.That(Ready(), Is.False);
			readiness.activeMethod = Method.TwoAprilTags; Report(status, readiness);
			Assert.That(Ready(), Is.False);
			Assert.That(coordinator.CurrentSpace.HasReferenceBasedData, Is.True);
		}

		private void AlignCall(string method, params object[] args) => alignment.GetType().GetMethod(method).Invoke(alignment, args);
		private void ReceiveCandidate(MapSpace candidate, Guid operation, bool committed = false, Guid? context = null)
		{
			var type = typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.SpaceTransitionCommand", true);
			var command = Activator.CreateInstance(type);
			void Field(string name, object value) => type.GetField(name).SetValue(command, value);
			Field("operation", operation); Field("revision", Guid.NewGuid());
			Field("referenceContext", context ?? coordinator.ReferenceContext);
			Field("space", new MapIdentity { spaceId = Guid.Parse(candidate.id), frameId = Guid.Parse(candidate.canonicalFrameId) });
			Field("target", Method.AprilTag); Field("intent", ReferenceTransitionIntent.ActivateTarget);
			Field("createsReferences", true); Field("committed", committed);
			Call("OnTransitionReceived", command, candidate);
		}
		private static MapSpace CanonicalCandidate(MapSpace local)
		{
			var remote = local.Clone();
			remote.tags = local.tags.ConvertAll(t => new MapTagEntry { id = t.id, canonPose = local.Frame.ToCanonical(t.canonPose) });
			remote.anchors = local.anchors.ConvertAll(a => new MapAnchorEntry { guid = a.guid, tagId = a.tagId, canonPose = local.Frame.ToCanonical(a.canonPose) });
			remote.localAnchors.Clear(); remote.storageFrameId = remote.canonicalFrameId; remote.canonicalFromStorage = Pose.identity;
			return remote;
		}

		[TestCase(false)]
		[TestCase(true)]
		public void IncomingCandidatesRetainLivePrivateAnchorsUntilTheirTagDefinitionChanges(bool changeSize)
		{
			var space = coordinator.CurrentSpace;
			space.canonicalFromStorage = new Pose(new Vector3(3, 0, 5), Quaternion.Euler(0, 90, 0)); spaces.Load(space);
			var candidate = CanonicalCandidate(space); candidate.SetTag(7, space.Frame.ToCanonical(Pose.identity));
			var operation = Guid.NewGuid(); ReceiveCandidate(candidate, operation);
			var anchorId = Guid.NewGuid(); var storedAnchor = new Pose(Vector3.right * .2f, Quaternion.identity);
			manager.TagProvider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(anchorId, 7, space.Frame.ToCanonical(storedAnchor)) });
			Assert.That(((MapSpace)Get(coordinator, "staged")).localAnchors, Is.Empty, "The next callback can arrive before LateUpdate captures the mint.");
			candidate.SetTag(22, new Pose(Vector3.forward * 6, Quaternion.identity)); ReceiveCandidate(candidate, operation);
			var staged = (MapSpace)Get(coordinator, "staged");
			Assert.That(staged.localAnchors.Count, Is.EqualTo(1));
			Assert.That(staged.localAnchors[0].guid, Is.EqualTo(anchorId.ToString("N")));
			Assert.That(MapSpaceFrame.Near(staged.localAnchors[0].canonPose, storedAnchor), Is.True);
			Assert.That(staged.IsCompatibleLocalAnchor(staged.localAnchors[0]), Is.True);
			Assert.That(manager.TagProvider.LocalAnchorCount, Is.EqualTo(1));

			var stale = candidate.Clone(); stale.SetTag(7, Pose.identity);
			ReceiveCandidate(stale, operation, context: Guid.NewGuid());
			Assert.That(manager.TagProvider.LocalAnchorCount, Is.EqualTo(1), "A stale context cannot replace the candidate or its private anchors.");
			stale.canonicalFrameId = Guid.NewGuid().ToString("N"); ReceiveCandidate(stale, operation);
			Assert.That(manager.TagProvider.LocalAnchorCount, Is.EqualTo(1), "A different frame cannot import private anchors.");

			if (changeSize) candidate.tagSizeCm += 1; else candidate.SetTag(7, Pose.identity);
			ReceiveCandidate(candidate, operation);
			Assert.That(manager.TagProvider.LocalAnchorCount, Is.Zero, "A changed tag pose or size requires a fresh realization.");
		}

		[TestCase(false)]
		[TestCase(true)]
		public void CommittedSessionSnapshotsPersistPrivateTagAnchorsInEitherCallbackOrder(bool mapFirst)
		{
			var local = coordinator.CurrentSpace;
			local.canonicalFromStorage = new Pose(new Vector3(3, 0, 5), Quaternion.Euler(0, 90, 0));
			var remote = CanonicalCandidate(local); remote.id = Guid.NewGuid().ToString("N");
			remote.SetTag(7, local.Frame.ToCanonical(Pose.identity)); remote.preferredColocationMethod = Method.AprilTag;
			local.associations.Add(new() { remoteSpaceId = remote.id, frameId = remote.canonicalFrameId });
			var map = new GameMap { id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"), name = "Session layout", storageFrameId = local.storageFrameId };
			local.mapIds.Add(map.id); MapStore.Default.Save(map); MapSpaceStore.Default.Save(local); spaces.Load(local);
			((MapManager)Get(coordinator, "maps")).Load(map);
			Set(coordinator, "objects", Create("MapObjectDirector", null, (Action)(() => { }), (Func<bool>)(() => false), (Func<MapSpaceFrame>)(() => spaces.Frame)));
			var identity = new MapIdentity { id = Guid.Parse(map.id), version = Guid.Parse(map.version), spaceId = Guid.Parse(remote.id),
				frameId = Guid.Parse(remote.canonicalFrameId), spaceVersion = Guid.Parse(remote.version), context = Guid.NewGuid(),
				referenceContext = coordinator.ReferenceContext, scan = coordinator.ScanContext, method = Method.AprilTag };
			identity.name.CopyFromTruncated(map.name); Set(coordinator, "sessionIdentity", identity);
			var operation = Guid.NewGuid(); ReceiveCandidate(remote, operation);
			var anchorId = Guid.NewGuid(); var storedAnchor = new Pose(Vector3.right * .2f, Quaternion.identity);
			manager.TagProvider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(anchorId, 7, local.Frame.ToCanonical(storedAnchor)) });
			void ReceiveMap()
			{
				Call("OnSessionMapReceived", identity, new List<MapObjectEntry>(), remote.Clone());
				Assert.That(manager.TagProvider.LocalAnchorCount, Is.EqualTo(1), "The map snapshot must preserve the live realization before the next transition callback.");
			}
			if (mapFirst) { ReceiveMap(); ReceiveCandidate(remote, operation, true); }
			else { ReceiveCandidate(remote, operation, true); ReceiveMap(); }
			Assert.That(manager.TagProvider.LocalAnchorCount, Is.EqualTo(1));
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(local.id, out var reopened), Is.True);
			Assert.That(reopened.TryGetTag(7, out _), Is.True);
			Assert.That(reopened.localAnchors.Count, Is.EqualTo(1));
			Assert.That(reopened.localAnchors[0].guid, Is.EqualTo(anchorId.ToString("N")));
			Assert.That(reopened.IsCompatibleLocalAnchor(reopened.localAnchors[0]), Is.True);
			Assert.That(MapSpaceFrame.Near(reopened.localAnchors[0].canonPose, storedAnchor), Is.True);
		}

		[Test]
		public void IncomingCandidateKeepsAMintedReplacementAheadOfItsOlderSavedTagAnchor()
		{
			var space = coordinator.CurrentSpace; space.SetTag(7, Pose.identity);
			var oldId = Guid.NewGuid(); var currentId = Guid.NewGuid();
			space.localAnchors.Add(new() { guid = oldId.ToString("N"), tagId = 7, canonPose = Pose.identity,
				tagCanonPose = Pose.identity, tagSizeCm = space.tagSizeCm }); spaces.Load(space);
			var candidate = CanonicalCandidate(space); var operation = Guid.NewGuid(); ReceiveCandidate(candidate, operation);
			manager.TagProvider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(currentId, 7, Pose.identity) });
			candidate.SetTag(22, new Pose(Vector3.forward, Quaternion.identity)); ReceiveCandidate(candidate, operation);
			var realized = new List<TaggedAnchorConstraintData>(); manager.TagProvider.GetLocalAnchorConstraints(realized);
			Assert.That(realized.Count, Is.EqualTo(1)); Assert.That(realized[0].guid, Is.EqualTo(currentId));
			var staged = (MapSpace)Get(coordinator, "staged");
			Assert.That(staged.localAnchors[0].guid, Is.EqualTo(currentId.ToString("N")));
			Assert.That(staged.localAnchors.Exists(a => a.guid == oldId.ToString("N")), Is.True, "The old saved UUID still needs ownership bookkeeping.");
			ReceiveCandidate(candidate, operation, true);
			realized.Clear(); manager.TagProvider.GetLocalAnchorConstraints(realized);
			Assert.That(realized[0].guid, Is.EqualTo(currentId));
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(space.id, out var reopened), Is.True);
			Assert.That(reopened.localAnchors[0].guid, Is.EqualTo(currentId.ToString("N")));
			Assert.That(reopened.localAnchors.Exists(a => a.guid == oldId.ToString("N")), Is.True);
		}

		[TestCase("current")]
		[TestCase("operation")]
		[TestCase("context")]
		[TestCase("space")]
		[TestCase("frame")]
		public void ReconciliationCapturesLatestPrivateAnchorsOnlyForItsCurrentSession(string scope)
		{
			var local = coordinator.CurrentSpace;
			local.canonicalFromStorage = new Pose(new Vector3(3, 0, 5), Quaternion.Euler(0, 90, 0));
			var remote = CanonicalCandidate(local); remote.id = Guid.NewGuid().ToString("N");
			remote.SetTag(7, local.Frame.ToCanonical(Pose.identity));
			var map = new GameMap { id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"), name = "Reconciled layout", storageFrameId = remote.storageFrameId };
			MapSpaceStore.Default.Save(local); spaces.Load(remote);
			Call("NotifySpace");
			manager.TagProvider.AdoptTagSize(remote.tagSizeCm);
			manager.TagProvider.SetRegisteredTags(new[] { new TagConstraintData(7, remote.tags[0].canonPose) });
			var anchorId = Guid.NewGuid(); var storedAnchor = new Pose(Vector3.right * .2f, Quaternion.identity);
			manager.TagProvider.SetLocalAnchors(new[] { new TaggedAnchorConstraintData(anchorId, 7, local.Frame.ToCanonical(storedAnchor)) });
			Assert.That(remote.localAnchors, Is.Empty, "The snapshot held across await predates the native mint.");
			int operation = 10; var context = coordinator.ReferenceContext;
			Set(coordinator, "incomingGeneration", scope == "operation" ? operation + 1 : operation);
			if (scope == "context") Set(coordinator, "referenceContext", Guid.NewGuid());
			Set(coordinator, "sessionIdentity", new MapIdentity {
				spaceId = scope == "space" ? Guid.NewGuid() : Guid.Parse(remote.id),
				frameId = scope == "frame" ? Guid.NewGuid() : Guid.Parse(remote.canonicalFrameId) });
			owner.AddComponent<NetworkObject>(); bus = owner.AddComponent<SyncBus>();
			Property(bus, "IsSpawned", true); Property(bus, "HasAuthority", false); Static(typeof(SyncBus), "Current", bus);
			bool adopted = (bool)Call("TryFinishSessionReconciliation", local, remote, map, local.canonicalFromStorage, operation, context);
			Assert.That(adopted, Is.EqualTo(scope == "current"));
			Assert.That(new MapSpaceStore(Path.Combine(directory, "spaces")).TryGet(local.id, out var reopened), Is.True);
			if (scope == "current")
			{
				Assert.That(reopened.localAnchors.Count, Is.EqualTo(1));
				Assert.That(reopened.localAnchors[0].guid, Is.EqualTo(anchorId.ToString("N")));
				Assert.That(reopened.IsCompatibleLocalAnchor(reopened.localAnchors[0]), Is.True);
				Assert.That(MapSpaceFrame.Near(reopened.localAnchors[0].canonPose, storedAnchor), Is.True);
			}
			else Assert.That(reopened.localAnchors, Is.Empty, "An obsolete reconciliation cannot commit its snapshot or captured anchors.");
			Assert.That(remote.localAnchors, Is.Empty, "Preservation must not mutate the held source snapshot.");
		}

		[Test]
		public void ReferenceHandoffRetainsTheSourceServiceUntilCommitThenStopsTagScanning()
		{
			using var lifetime = new System.Threading.CancellationTokenSource();
			Set(manager.AnchorProvider, "lifetimeCtknSrc", lifetime);
			var field = typeof(AprilTagColocationConstraintProvider).GetField("tagTracker", Private);
			field.SetValue(manager.TagProvider, owner.AddComponent(field.FieldType));
			var space = coordinator.CurrentSpace; space.SetTag(7, Pose.identity);
			space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
			AlignCall("Activate", space, Method.AprilTag, false);
			AlignCall("Begin", Guid.NewGuid(), space, Method.MetaSharedAnchor, ReferenceTransitionIntent.ActivateTarget, false);
			AlignCall("SetCandidate", Guid.NewGuid(), space);
			Assert.That(manager.TagProvider.IsRunning, Is.True);
			Assert.That(manager.AnchorProvider.IsRunning, Is.True);
			Assert.That(manager.TagProvider.IsDetecting, Is.True);
			AlignCall("Commit", false);
			Assert.That(manager.ActiveMethod, Is.EqualTo(Method.MetaSharedAnchor));
			Assert.That(manager.TagProvider.IsRunning, Is.False);
			Assert.That(manager.TagProvider.IsDetecting, Is.False);
			Assert.That(manager.AnchorProvider.IsRunning, Is.True);
			AlignCall("Begin", Guid.NewGuid(), space, Method.AprilTag, ReferenceTransitionIntent.SetupOnly, true);
			Assert.That(manager.TagProvider.IsDetecting, Is.True);
			AlignCall("Cancel");
			Assert.That(manager.TagProvider.IsDetecting, Is.False);
			Assert.That(manager.AnchorProvider.IsRunning, Is.True);
			lifetime.Cancel();
		}

		[Test]
		public void CancelingTagSetupStopsItsServiceAndReleasesTheDetector()
		{
			var field = typeof(AprilTagColocationConstraintProvider).GetField("tagTracker", Private);
			field.SetValue(manager.TagProvider, owner.AddComponent(field.FieldType));
			Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
			Assert.That(manager.TagProvider.IsRunning, Is.True);
			Assert.That(manager.TagProvider.IsDetecting, Is.True);
			coordinator.CancelAlignmentTransition();
			Assert.That(manager.TagProvider.IsRunning, Is.False);
			Assert.That(manager.TagProvider.IsDetecting, Is.False);
			Assert.That(coordinator.IsChangingColocation, Is.False);
		}

		[Test]
		public void DelegatedTagValidationPersistsOnTheOperatorWithoutWaitingForItsOwnRig()
		{
			var space = coordinator.CurrentSpace;
			space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1); spaces.Load(space);
			var status = RemoteHeadset(out var readiness); readiness.referenceFrameTrusted = true; Report(status, readiness);
			Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
			Assert.That((bool)Call("ApplyReferenceRequest", 7UL, TagRequest(7, Pose.identity)), Is.True);
			readiness.referenceFrameTrusted = false; Report(status, readiness);
			var evidenceType = typeof(MapSpace).Assembly.GetType("Anaglyph.LaserTag.Maps.SpaceTransitionEvidence", true);
			var evidence = Activator.CreateInstance(evidenceType);
			void Evidence(string key, object value) => evidenceType.GetField(key).SetValue(evidence, value);
			Evidence("operation", coordinator.AlignmentTransition.Operation); Evidence("revision", coordinator.AlignmentTransition.Revision);
			Evidence("referenceContext", coordinator.ReferenceContext); Evidence("trackingGeneration", readiness.trackingGeneration);
			Call("OnTargetValidated", 7UL, evidence);
			var reopened = new MapSpaceStore(Path.Combine(directory, "spaces"));
			Assert.That(reopened.TryGet(space.id, out var saved), Is.True);
			Assert.That(saved.TryGetTag(7, out _), Is.True);
			Assert.That(saved.preferredColocationMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(saved.hasPendingSetup, Is.False);
			Assert.That(coordinator.IsChangingColocation, Is.False);
			Assert.That(manager.ActiveMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(manager.IsSettingUpTags, Is.False);
		}

		[Test]
		public void AnchorUpdatesCannotReplaceAnExplicitPendingTagRequest()
		{
			Assert.That(coordinator.SetPreferredColocationMethod(Method.AprilTag), Is.True);
			var adapter = Get(coordinator, "references"); adapter.GetType().GetMethod("Register").Invoke(adapter, null);
			try
			{
				Property(manager.AnchorProvider, "IsRunning", true);
				manager.AnchorProvider.SetConstraints(new[] { new AnchorConstraintData(Guid.NewGuid(), Pose.identity, -1) });
				Call("ApplyReferenceChanges");
				Assert.That(coordinator.CurrentSpace.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
				Assert.That(coordinator.WaitingForAlignmentAuthor, Is.True);
			}
			finally { adapter.GetType().GetMethod("Unregister").Invoke(adapter, null); }
		}

		[Test]
		public void SessionSnapshotCarriesPendingMethodAndIntent()
		{
			var snapshot = Create("MapSpaceSessionSync", "test-space");
			var identity = new MapIdentity { spaceId = Guid.NewGuid(), frameId = Guid.NewGuid(), spaceVersion = Guid.NewGuid(),
				tagSizeCm = 10, firstTagId = -1, secondTagId = -1, hasPendingSetup = true,
				pendingSetupMethod = Method.AprilTag, pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget };
			var read = (MapSpace)snapshot.GetType().GetMethod("Read").Invoke(snapshot, new object[] { identity });
			Assert.That(read.hasPendingSetup, Is.True);
			Assert.That(read.pendingSetupMethod, Is.EqualTo(Method.AprilTag));
			Assert.That(read.pendingSetupIntent, Is.EqualTo(ReferenceTransitionIntent.ActivateTarget));
		}

		private sealed class TrustedProvider : IColocationConstraintProvider
		{
			public bool IsAvailable => true;
			public bool IsRunning => true;
			public void StartProviding() { }
			public void StopProviding() { }
			public void GetColocationConstraints(List<ColocationConstraint> result) { }
		}
		[Test]
		public void PendingTagSetupDoesNotBlockFiringWithAStillTrustedSource()
		{
			var solver = owner.AddComponent<Colocator>(); Set(manager, "colocator", solver);
			Property(solver, "Provider", new TrustedProvider()); Property(solver, "Agreement", new FitAgreement(1, 1, 0, true));
			bool wasColocated = ColocationManager.IsColocated;
			bool couldFire = (bool)typeof(Weapons.WeaponsManagement).GetField("canFire", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
			try
			{
				Static(typeof(ColocationManager), "IsColocated", true);
				Weapons.WeaponsManagement.CanFire = true;
				Assert.That((bool)Call("BeginTransition", Method.AprilTag, ReferenceTransitionIntent.ActivateTarget, true, (ulong?)7), Is.True);
				Assert.That(coordinator.IsChangingColocation, Is.True);
				Assert.That(Weapons.WeaponsManagement.CanFire, Is.True);
				Static(typeof(ColocationManager), "IsColocated", false);
				Assert.That(Weapons.WeaponsManagement.CanFire, Is.False);
			}
			finally { Static(typeof(ColocationManager), "IsColocated", wasColocated); Weapons.WeaponsManagement.CanFire = couldFire; }
		}

		[Test]
		public void SequentialTagValidationSurvivesCandidateExtensionButNotBadSamplesOrRecenter()
		{
			var rigType = Type.GetType("Anaglyph.XR.MainXRRig, Anaglyph.XR", true);
			var previousRig = rigType.GetProperty("Instance").GetValue(null);
			var rig = owner.AddComponent(rigType); rigType.GetField("trackingSpace").SetValue(rig, owner.transform);
			Static(rigType, "Instance", rig);
			try
			{
					var space = coordinator.CurrentSpace; space.SetTag(7, Pose.identity);
					manager.TagProvider.AdoptTagSize(space.tagSizeCm);
				using var first = (IDisposable)Create("MapSpaceReferenceObservation", space, Method.AprilTag, null, manager.TagProvider);
				void Observe(object observation, Pose pose) => observation.GetType().GetMethod("OnTag", Private).Invoke(observation, new object[] { 7, pose });
				bool Confirmed(object observation) => (bool)observation.GetType().GetMethod("HasRepeatedObservation").Invoke(observation, new object[] { 7 });
				for (int i = 0; i < 11; i++) Observe(first, Pose.identity);
				Observe(first, new Pose(Vector3.right, Quaternion.identity)); Observe(first, Pose.identity);
				Assert.That(Confirmed(first), Is.False, "Bad observations break the run of agreement.");
				for (int i = 0; i < 11; i++) Observe(first, Pose.identity);
				Assert.That(Confirmed(first), Is.True);
				var seen = (Dictionary<int, (Pose pose, float time)>)Get(first, "seen");
				seen[7] = (Pose.identity, Time.unscaledTime - 10);
				Assert.That(Confirmed(first), Is.True, "The tag need not stay visible while another is registered.");
				space.SetTag(22, new Pose(Vector3.forward * 3, Quaternion.identity));
				using var next = (IDisposable)Create("MapSpaceReferenceObservation", space, Method.AprilTag, null, manager.TagProvider);
				next.GetType().GetMethod("RetainObservationsFrom").Invoke(next, new object[] { first });
				Assert.That(Confirmed(next), Is.True);
				space.SetTag(7, new Pose(Vector3.right, Quaternion.identity));
				using var changed = (IDisposable)Create("MapSpaceReferenceObservation", space, Method.AprilTag, null, manager.TagProvider);
				changed.GetType().GetMethod("RetainObservationsFrom").Invoke(changed, new object[] { next });
				Assert.That(Confirmed(changed), Is.False, "A changed canonical pose needs new evidence.");
				manager.TrackingOriginChanged(); Assert.That(Confirmed(next), Is.False);
			}
			finally { Static(rigType, "Instance", previousRig); }
		}

		[Test]
		public void InvalidMethodRequestRaisesOnlyOneWarning()
		{
			var type = typeof(LaserTagMapCoordinator).GetNestedType("MethodRequest", BindingFlags.NonPublic);
			var request = Activator.CreateInstance(type);
			type.GetField("context").SetValue(request, coordinator.ReferenceContext);
			type.GetField("method").SetValue(request, (Method)999);
			Call("OnMethodRequested", 0UL, request);
			Assert.That(errors.Count, Is.EqualTo(1));
			Assert.That(coordinator.CurrentSpace.HasReferenceBasedData, Is.False);
		}

		[Test]
		public void NetworkReadinessUsesTheSameSimulatedHeadTrackingAsLocalAuthoring()
		{
			Assert.That(UnityEngine.XR.XRSettings.enabled, Is.False);
			var rigType = Type.GetType("Anaglyph.XR.MainXRRig, Anaglyph.XR", true);
			var previousRig = rigType.GetProperty("Instance").GetValue(null);
			try
			{
				Static(rigType, "Instance", owner.AddComponent(rigType));
				Set(configuration, "role", HeadsetConfiguration.DeviceRole.Headset);
				Assert.That(PlayerHeadsetStatus.HeadTrackingReady, Is.True);
				var status = owner.AddComponent<PlayerHeadsetStatus>();
				var readiness = (HeadsetReadiness)typeof(PlayerHeadsetStatus).GetMethod("SampleReadiness", Private).Invoke(status, null);
				Assert.That(readiness.isHeadTracked, Is.True);
				Assert.That(readiness.supportsSharedAnchors, Is.False);
				Assert.That(readiness.referenceFrameTrusted, Is.False);
				Set(configuration, "role", HeadsetConfiguration.DeviceRole.Operator);
				Assert.That(PlayerHeadsetStatus.HeadTrackingReady, Is.False);
			}
			finally { Static(rigType, "Instance", previousRig); }
		}

		public sealed class TestAnchorProvider : XRAnchorSubsystem.Provider
		{
			public override void Start() { }
			public override void Stop() { }
			public override void Destroy() { }
			public override TrackableChanges<XRAnchor> GetChanges(XRAnchor defaultAnchor, Allocator allocator) => default;
		}

		[TestCase(Method.MetaSharedAnchor)]
		[TestCase(Method.AprilTag)]
		public void OperatorCanObserveSavedReferencesWithoutAnAnchorRuntime(Method method)
		{
			var space = coordinator.CurrentSpace;
			space.SetTag(7, Pose.identity);
			space.SetAnchorWithTag(Guid.NewGuid().ToString("N"), Pose.identity, -1);
			space.localAnchors.Add(new() { guid = Guid.NewGuid().ToString("N"), tagId = 7, canonPose = Pose.identity,
				tagCanonPose = Pose.identity, tagSizeCm = space.tagSizeCm });
			var registry = owner.AddComponent<AnchorRegistry>();
			using var observation = (IDisposable)Create("MapSpaceReferenceObservation", space, method, registry, manager.TagProvider);
			var provider = (IColocationConstraintProvider)observation;
			Assert.DoesNotThrow(provider.StartProviding);
			Assert.That(provider.IsRunning, Is.True);
			Assert.That(provider.IsAvailable, Is.False);
			var constraints = new List<ColocationConstraint>();
			Assert.DoesNotThrow(() => provider.GetColocationConstraints(constraints));
			Assert.That(constraints, Is.Empty);
		}

		[TestCase(false)]
		[TestCase(true)]
		public void PersistentAnchorMintingDependsOnRuntimeCapabilityEvenInsideTheEditor(bool supportsSaving)
		{
			string id = "AlignmentRequestTest-" + supportsSaving;
			var descriptors = new List<XRAnchorSubsystemDescriptor>(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			if (!descriptors.Exists(d => d.id == id))
				XRAnchorSubsystemDescriptor.Register(new() { id = id, providerType = typeof(TestAnchorProvider), supportsSaveAnchorDelegate = () => supportsSaving });
			descriptors.Clear(); SubsystemManager.GetSubsystemDescriptors(descriptors);
			var subsystem = descriptors.Find(d => d.id == id).Create();
			var anchorManager = owner.AddComponent<ARAnchorManager>();
			var property = typeof(ARAnchorManager).GetProperty("subsystem");
			property.DeclaringType.GetProperty("subsystem").SetValue(anchorManager, subsystem);
			var registry = owner.AddComponent<AnchorRegistry>(); Set(registry, "anchorManager", anchorManager);
			var provider = manager.AnchorProvider; Set(provider, "registry", registry);
			provider.LocalReadinessGate = () => true; provider.MintingGate = () => true;
			Property(provider, "IsRunning", true);
			try
			{
				Assert.That(registry.IsAvailable, Is.True);
				Assert.That(registry.canSaveAnchors, Is.EqualTo(supportsSaving));
				Assert.That(typeof(SpatialAnchorColocationConstraintProvider).GetProperty("CanMintNow", Private).GetValue(provider), Is.EqualTo(supportsSaving));
			}
			finally
			{
				Property(provider, "IsRunning", false);
				Set(registry, "anchorManager", null);
				property.DeclaringType.GetProperty("subsystem").SetValue(anchorManager, null);
				subsystem.Destroy();
			}
		}
	}
}
