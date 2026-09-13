using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.Permissions;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag
{
	/// <summary>Composes separate layout and space documents, scene projections, alignment and session authority.</summary>
	[DefaultExecutionOrder(-100)]
	public class LaserTagMapCoordinator : MonoBehaviour
	{
		public static LaserTagMapCoordinator Instance { get; private set; }
		[SerializeField] private ColocationManager colocationManager;
		[SerializeField] private MapObjectDatabase objectDatabase;
		[SerializeField] private float probeTimeoutSeconds = 8f;
		[SerializeField] private float saveDebounceSeconds = 2f;
		[SerializeField] private float switchTimeoutSeconds = 20f;
		public static event Action<GameMap> CurrentMapChanged = delegate { };
		public static event Action<MapSpace> CurrentSpaceChanged = delegate { };
		public static event Action WorldFrameRebased = delegate { };
		public static event Action ChangingMapChanged = delegate { };
		public static event Action ProbeResultsChanged = delegate { };
		public static event Action ColocationSettingsChanged = delegate { };
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Instance = null; CurrentMapChanged = delegate { }; CurrentSpaceChanged = delegate { };
			WorldFrameRebased = delegate { }; ChangingMapChanged = delegate { };
			ProbeResultsChanged = delegate { }; ColocationSettingsChanged = delegate { };
		}
		private MapManager maps;
		private MapSpaceManager spaces;
		private MapSpaceCatalog catalog;
		private MapObjectDirector objects;
		private MapSpaceColocationAdapter references;
		private MapSpaceAlignmentController alignment;
		private MapSessionSync session;
		private MapSpaceTransitionSync transitions;
		private MapSpaceDiscovery discovery;
		private MapAutosave autosave;
		private readonly MapWorkflow workflow = new();
		private CancellationTokenSource lifetime, nativePreparation;
		private Guid mapContext = Guid.NewGuid(), referenceContext = Guid.NewGuid(), scan = Guid.NewGuid();
		private MapIdentity sessionIdentity;
		private MapSpace staged, beforeSessionSpace;
		private GameMap beforeSessionMap;
		private SpaceTransitionCommand transitionCommand;
		private Action finishCatalogOperation;
		private bool transientSession, applyingReferences, startingSpace, shuttingDown;
		private Method observedSource;
		private int incomingGeneration;
		private string registrationRequestedFor;
		private readonly HashSet<string> anchorsToErase = new();
		private readonly List<KeyValuePair<ulong, HeadsetReadiness>> anchorCandidates = new();

		private struct MethodRequest { public Guid context; public Method method; public bool cancel; }
		// Value 4 belonged to the removed manual observation-reset request.
		private enum ReferenceAction : byte { Register = 0, Remove = 1, Size = 2, Pair = 3, ForgetPair = 5 }
		private struct ReferenceRequest
		{ public Guid context, operation; public ReferenceAction action; public int tag, second, trackingGeneration; public float size; public Pose pose; }
		private struct RequestRejection { public Guid context, operation; public ulong recipient; }
		private readonly SyncEvent<RequestRejection> requestRejection = new("space.request.rejected", EventRoute.ViaAuthority);
		private readonly SyncEvent<MethodRequest> methodRequest = new("space.method.request", EventRoute.ToAuthority);
		private readonly SyncEvent<ReferenceRequest> referenceRequest = new("space.reference.request", EventRoute.ToAuthority);
		public MapPhase Phase => workflow.Phase;
		public GameMap CurrentMap => maps?.CurrentMap;
		public MapSpace CurrentSpace => spaces?.CurrentSpace;
		public Guid ReferenceContext => referenceContext;
		public Guid ScanContext => scan;
		public Guid CanonicalFrameId => Guid.TryParse(spaces?.Frame.CanonicalId, out var id) ? id : Guid.Empty;
		public Guid SessionSpaceId => SyncBus.Active && !SyncBus.IsAuthority ? sessionIdentity.spaceId :
			Guid.TryParse(spaces?.CurrentId, out var id) ? id : Guid.Empty;
		public bool IsChangingMap => session != null && session.IsChangingMap;
		public bool IsChangingColocation => alignment != null && alignment.Busy;
		public bool WaitingForAlignmentAuthor => !IsChangingColocation && CurrentSpace?.hasPendingSetup == true;
		public ReferenceAlignmentTransition AlignmentTransition => alignment?.Transition;
		public bool HasPendingSpaceAdoption => transientSession;
		public IReadOnlyDictionary<string, int> ProbeResults => discovery.Results;
		public MapPresence GetSpacePresence(string id) => discovery.GetPresence(id);
		public MapPresence GetMapPresence(string id) => GetSpacePresence(MapSpaceStore.Default.FindOwner(id)?.id);
		private bool Authority => !SyncBus.Active || SyncBus.IsAuthority;
		private bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;
		private bool CanManage => Authority && workflow.Phase is MapPhase.Local or MapPhase.Hosting;
		private bool FrameReady => spaces != null && spaces.HasSpace && ColocationManager.IsColocated &&
			(!ColocationManager.UsesSavedReferences(colocationManager.ActiveMethod) || colocationManager.ReferenceAligned) &&
			(workflow.Phase is MapPhase.Local or MapPhase.Hosting or MapPhase.FollowingSession) &&
			(!SyncBus.Active || colocationManager.ActiveMethod == colocationManager.SelectedMethod ||
			 ColocationManager.UsesSavedReferences(colocationManager.ActiveMethod) && ColocationManager.UsesSavedReferences(colocationManager.SelectedMethod));
		public bool CanIntegrateEnvironment => FrameReady && TrackingReady() &&
			(colocationManager.ActiveMethod != Method.TwoAprilTags || CurrentSpace?.firstTagId >= 0);

		private void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this); return; }
			Instance = this; lifetime = new();
			if (!colocationManager) colocationManager = FindFirstObjectByType<ColocationManager>();
			maps = new(MapStore.Default); spaces = new(MapSpaceStore.Default);
			catalog = new(MapStore.Default, MapSpaceStore.Default, MapStore.CatalogRoot);
			if (catalog.Recover()) MapSpaceStore.Default.PruneMembership(MapStore.Default);
			autosave = new(saveDebounceSeconds, OnAutosave);
			objects = new(objectDatabase, MarkMapContentChanged, CheckCanEditMap, () => spaces.Frame);
			references = new(colocationManager);
			alignment = new(colocationManager, TrackingReady);
			colocationManager.ManagedSelection = true;
			session = new(); transitions = new();
			discovery = new(MapSpaceStore.Default, colocationManager.AnchorProvider, probeTimeoutSeconds, TrackingReady);
			objects.Register(); references.Register(); ConfigureAnchorMinter();
			alignment.Validated += OnLocallyValidated; alignment.Changed += RaiseSettingsChanged;
			colocationManager.MethodChanged += OnSelectedMethodChanged; colocationManager.SourceChanged += OnAlignmentSourceChanged;
			colocationManager.TwoTagProvider.PairSelected += OnPairSelected;
			// All authoring requests go through a space/operation token before provider mutation.
			if (colocationManager.TagProvider) colocationManager.TagProvider.ReferenceEditGate = _ => false;
			requestRejection.Validate = (sender, _) => SyncBus.IsAuthority && sender == SyncBus.LocalClientId;
			requestRejection.Register(); requestRejection.Received += OnRequestRejected;
			methodRequest.Register(); methodRequest.Received += OnMethodRequested;
			referenceRequest.Register(); referenceRequest.Received += OnReferenceRequested;
			transitions.Register(); transitions.Received += OnTransitionReceived; transitions.Validated += OnTargetValidated;
			references.Changed += autosave.Schedule;
			session.AuthorityReady += OnAuthorityReady; session.Received += OnSessionMapReceived;
			session.ChangingMapChanged += RaiseChangingMapChanged; session.Register();
			discovery.ResultsChanged += RaiseProbeResultsChanged;
			SyncBus.Activated += OnBusActivated; SyncBus.Deactivated += OnBusDeactivated; SyncBus.AuthorityChanged += OnAuthorityChanged;
			MapObject.LocalEditOccurred += MarkMapContentChanged; MapObject.Added += OnObjectChanged; MapObject.Removed += OnObjectChanged;
			MainXRRig.Recentered += OnRecentered;
		}
		private void Start() => StartupProbe(lifetime.Token);
		private static void RaiseSettingsChanged() => ColocationSettingsChanged.Invoke();
		private static void RaiseProbeResultsChanged() => ProbeResultsChanged.Invoke();
		private void RaiseChangingMapChanged() => ChangingMapChanged.Invoke();
		private void OnRecentered() { alignment.Transition?.ResetEvidence(); discovery.Invalidate(); colocationManager.TrackingOriginChanged(); }
		private void LateUpdate()
		{
			CheckTransitionAuthor(); FallbackFromUnavailableSharing(); ApplyReferenceChanges(); ResumePendingSetup(); alignment.Tick();
			if (workflow.Phase == MapPhase.SwitchingMap && (objects.IsReplacementComplete || workflow.SwitchTimedOut(Time.unscaledTime, switchTimeoutSeconds)))
			{ workflow.FinishSwitch(); session.SetChanging(false); }
			RequestTagRegistrationIfNeeded();
		}
		private void OnDestroy()
		{
			if (Instance != this) return;
			shuttingDown = true; SaveCurrentMap(); lifetime.Cancel(); nativePreparation?.Cancel();
			workflow.Stop(); alignment.Dispose(); autosave.Dispose(); ClearAnchorMinter();
			alignment.Validated -= OnLocallyValidated; alignment.Changed -= RaiseSettingsChanged;
			colocationManager.MethodChanged -= OnSelectedMethodChanged; colocationManager.SourceChanged -= OnAlignmentSourceChanged;
			colocationManager.TwoTagProvider.PairSelected -= OnPairSelected;
			requestRejection.Received -= OnRequestRejected; requestRejection.Unregister();
			methodRequest.Received -= OnMethodRequested; methodRequest.Unregister();
			referenceRequest.Received -= OnReferenceRequested; referenceRequest.Unregister();
			transitions.Received -= OnTransitionReceived; transitions.Validated -= OnTargetValidated; transitions.Unregister();
			session.AuthorityReady -= OnAuthorityReady; session.Received -= OnSessionMapReceived;
			session.ChangingMapChanged -= RaiseChangingMapChanged; session.Unregister();
			references.Changed -= autosave.Schedule; references.Unregister(); objects.Unregister();
			SyncBus.Activated -= OnBusActivated; SyncBus.Deactivated -= OnBusDeactivated; SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MapObject.LocalEditOccurred -= MarkMapContentChanged; MapObject.Added -= OnObjectChanged; MapObject.Removed -= OnObjectChanged;
			MainXRRig.Recentered -= OnRecentered; lifetime.Dispose(); Instance = null;
		}
		private void OnApplicationQuit() { shuttingDown = true; SaveCurrentMap(); }
		private void OnApplicationPause(bool paused) { if (paused) SaveCurrentMap(); }
		private bool TrackingReady() => Application.isFocused && PlayerHeadsetStatus.HeadTrackingReady;
		public bool CheckWorldFrameIsTrusted() => FrameReady;
		public bool CheckReferenceFrameAgreement() => spaces != null && spaces.HasSpace && TrackingReady() && colocationManager.ReferenceAligned;
		public bool CheckCanEditMap() => !IsChangingMap && (FrameReady || (HeadsetConfiguration.IsOperatorDevice && Authority && CurrentSpace != null));
		private bool CanAuthorReferences => TrackingReady() && spaces != null && spaces.HasSpace &&
			MapPolicy.CanAuthorReferences(spaces.HasReferences, colocationManager.ActiveMethod, CheckReferenceFrameAgreement(), colocationManager.AnchorProvider.IsLocalMinter);
		private bool CanAuthorTags => TrackingReady() && spaces != null && spaces.HasSpace &&
			MapPolicy.CanAuthorReferences(spaces.HasReferences, colocationManager.ActiveMethod, CheckReferenceFrameAgreement(),
				MapPolicy.CanInitializeTags(SyncBus.Active, Authority, alignment.Busy,
					transitionCommand.target == Method.AprilTag && transitionCommand.author == SyncBus.LocalClientId));
		private CapabilitySupport SharedAnchorSupport
		{
			get
			{
				var capability = MetaPermissionChecks.CheckSharedSpatialAnchors();
				if (capability.vps == VpsStatus.Disabled || AnchorRegistry.Instance?.SharingDenied == true) return CapabilitySupport.Unsupported;
				return capability.support;
			}
		}

		// Layout changes keep the space, providers and live scan intact.
		public string DescribeChangeBlocker(string id)
		{
			if (!CanManage) return MenuCopy.Get("Game", "blocker.only-the-host-can-change-the-map");
			if (RoundInProgress) return MenuCopy.Get("Game", "blocker.not-during-a-round");
			if (CurrentMap?.id == id) return MenuCopy.Get("Game", "blocker.already-loaded");
			var owner = MapSpaceStore.Default.FindOwner(id);
			if (!MapStore.Default.TryGet(id, out _) || owner == null) return MenuCopy.Get("Game", "blocker.map-is-missing");
			return owner.id != CurrentSpace?.id ? DescribeSpaceChangeBlocker(owner.id) : null;
		}
		public bool ChangeMap(string id) => ReplaceMap(id);
		public bool LoadMap(string id) => !SyncBus.Active && ReplaceMap(id);
		public bool SwitchMap(string id) => ReplaceMap(id);
		private bool ReplaceMap(string id)
		{
			if (DescribeChangeBlocker(id) != null || !SaveCurrentMap() || !MapStore.Default.TryGet(id, out var target)) return false;
			var owner = MapSpaceStore.Default.FindOwner(id);
			if (owner.id != CurrentSpace?.id) return ChangeSpace(owner.id, id);
			BeginMapChange(); maps.Load(target); maps.MarkUsed(); SetObjectContext(); objects.Replace(target.objects);
			NotifyMap(); return SaveCurrentMap();
		}
		private void BeginMapChange()
		{
			mapContext = Guid.NewGuid();
			if (SyncBus.Active && SyncBus.IsAuthority) { workflow.BeginSwitch(Time.unscaledTime); session.SetChanging(true); }
		}
		private void SetObjectContext() => objects.SetContext(CurrentMap?.id, mapContext);
		private void NotifyMap() { SetObjectContext(); CurrentMapChanged.Invoke(CurrentMap); }
		private void NotifySpace()
		{ if (colocationManager.TagProvider) colocationManager.TagProvider.ReferenceContext = referenceContext;
			colocationManager.ConfigureSpace(CurrentSpace, () => CanAuthorReferences &&
				(!WaitingForAlignmentAuthor || CurrentSpace.pendingSetupMethod == Method.MetaSharedAnchor) &&
				(!alignment.Busy || (alignment.Transition.CreatesReferences && alignment.Transition.Target == Method.MetaSharedAnchor && (staged?.anchors.Count ?? 0) == 0))); CurrentSpaceChanged.Invoke(CurrentSpace); RaiseSettingsChanged(); }
		public string DescribeNewMapBlocker() => !CanManage ? MenuCopy.Get("Game", "blocker.only-the-host-can-change-the-map") :
			RoundInProgress ? MenuCopy.Get("Game", "blocker.not-during-a-round") : CurrentSpace == null ? MenuCopy.Get("Game", "space.load-first") : null;
		public bool NewMap()
		{
			if (DescribeNewMapBlocker() != null || !SaveCurrentMap()) return false;
			var target = NewLayout(CurrentSpace); var space = CurrentSpace; space.mapIds.Add(target.id);
			return CommitCatalog(space, new[] { target }, null, () => { spaces.Load(space); BeginMapChange(); maps.Load(target); SetObjectContext(); objects.Replace(target.objects); NotifySpace(); NotifyMap(); SaveCurrentMap(); });
		}
		private static GameMap NewLayout(MapSpace space) => new() {
			id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"), storageFrameId = space.storageFrameId,
			name = MapStore.Default.GenerateName(), lastUsed = DateTime.UtcNow.Ticks, lastEdited = DateTime.UtcNow.Ticks
		};
		public bool DuplicateMap(string id)
		{
			if (DescribeNewMapBlocker() != null || !MapStore.Default.TryGet(id, out var source) || !SaveCurrentMap()) return false;
			var owner = MapSpaceStore.Default.FindOwner(id); if (owner == null) return false;
			var copy = source.Clone(); copy.id = Guid.NewGuid().ToString("N"); copy.version = Guid.NewGuid().ToString("N"); copy.name += " (copy)";
			copy.storageFrameId = CurrentSpace.storageFrameId;
			copy.objects = source.objects.ConvertAll(o => new MapObjectEntry { prefabId = o.prefabId, pose = owner.canonicalFrameId == CurrentSpace.canonicalFrameId ? spaces.Frame.ToStorage(owner.Frame.ToCanonical(o.pose)) : o.pose });
			var space = CurrentSpace; space.mapIds.Add(copy.id);
			return CommitCatalog(space, new[] { copy }, null, () => { spaces.Load(space); NotifySpace(); });
		}
		public bool UnloadCurrentMap()
		{
			if (SyncBus.Active || !SaveCurrentMap()) return false;
			maps.Unload(); mapContext = Guid.NewGuid(); SetObjectContext(); objects.Replace(Array.Empty<MapObjectEntry>()); NotifyMap(); return true;
		}
		public string DescribeDeleteBlocker(string id) => id == null ? MenuCopy.Get("Game", "blocker.no-map-selected") :
			SyncBus.Active && CurrentMap?.id == id ? MenuCopy.Get("Game", "blocker.cannot-delete-the-active-session-map") : null;
		public bool DeleteMap(string id)
		{
			if (DescribeDeleteBlocker(id) != null || !SaveCurrentMap()) return false;
			var owner = MapSpaceStore.Default.FindOwner(id); if (owner == null) return false;
			owner.mapIds.Remove(id);
			return CommitCatalog(owner, null, new[] { id }, () => {
				if (owner.id == CurrentSpace?.id) spaces.Load(owner);
				if (CurrentMap?.id == id) { maps.Unload(); mapContext = Guid.NewGuid(); SetObjectContext(); objects.Replace(Array.Empty<MapObjectEntry>()); NotifyMap(); }
				NotifySpace();
			});
		}
		public string DescribeSpaceChangeBlocker(string id) => !CanManage || RoundInProgress || IsChangingColocation ?
			MenuCopy.Get("Game", "space.busy") : !HeadsetConfiguration.IsOperatorDevice && GetSpacePresence(id) == MapPresence.Elsewhere ?
			MenuCopy.Get("Game", "blocker.map-belongs-to-another-room") : null;
		public bool ChangeSpace(string id) => ChangeSpace(id, null);
		private bool ChangeSpace(string id, string mapId)
		{
			if (DescribeSpaceChangeBlocker(id) != null || !MapSpaceStore.Default.TryGet(id, out var target) || !SaveCurrentMap()) return false;
			if (id == CurrentSpace?.id) return mapId == null || ReplaceMap(mapId);
			var map = SelectMap(target, mapId);
			if (map == null) { map = NewLayout(target); target.mapIds.Add(map.id); var selected = map;
				return CommitCatalog(target, new[] { map }, null, () => ActivateSpace(target, selected)); }
			ActivateSpace(target, map); return SaveCurrentMap();
		}
		private static GameMap SelectMap(MapSpace space, string id = null)
		{
			if (id != null && space.mapIds.Contains(id) && MapStore.Default.TryGet(id, out var selected)) return selected;
			return MapStore.Default.GetByLastUsed().FirstOrDefault(m => space.mapIds.Contains(m.id));
		}
		public bool NewSpace()
		{
			if (!HeadsetConfiguration.IsOperatorDevice || !CanManage || RoundInProgress || !SaveCurrentMap()) return false;
			return CreateSpace(false);
		}
		private bool CreateSpace(bool automatic, Method initialMethod = Method.MetaSharedAnchor)
		{
			if (catalog.HasPending || startingSpace) return false;
			var space = MapSpace.Create(MapSpaceStore.Default.GenerateName()); space.automaticallyCreated = automatic; space.initializationPending = true;
			space.preferredColocationMethod = initialMethod;
			if (automatic) MapSpaceStartup.ConfigureDraft(space, initialMethod);
			var map = NewLayout(space); space.mapIds.Add(map.id); startingSpace = true;
			return CommitCatalog(space, new[] { map }, null, () => { startingSpace = false; ActivateSpace(space, map); SaveCurrentMap(); });
		}
		private bool CommitCatalog(MapSpace space, IReadOnlyList<GameMap> writes, IReadOnlyList<string> deletions, Action completed)
		{
			if (catalog.HasPending) return false;
			finishCatalogOperation = completed;
			if (!catalog.Commit(space, writes, deletions)) { autosave.Schedule(); return false; }
			finishCatalogOperation = null; completed(); return true;
		}
		private void ActivateSpace(MapSpace space, GameMap map)
		{
			CancelTransitionInternal(); transientSession = false; sessionIdentity = default;
			BeginMapChange(); referenceContext = Guid.NewGuid(); scan = Guid.NewGuid();
			spaces.Load(space); spaces.MarkUsed(); maps.Load(map); maps.MarkUsed();
			colocationManager.InvalidateAlignment(); references.Inject(space); references.ClearPendingSnapshots();
			NotifySpace(); alignment.Activate(space, colocationManager.PreferredAvailableMethod);
			if (SyncBus.Active && SyncBus.IsAuthority) colocationManager.CommitMethod(colocationManager.PreferredAvailableMethod);
			colocationManager.AnchorProvider?.InvalidateReferenceContext(); SetObjectContext(); objects.Replace(map?.objects ?? new());
			WorldFrameRebased.Invoke(); NotifyMap();
		}
		public string DescribeDeleteSpaceBlocker(string id) => !CanManage || RoundInProgress || IsChangingColocation ||
			(SyncBus.Active && CurrentSpace?.id == id) ? MenuCopy.Get("Game", "space.busy") : null;
		public bool DeleteSpace(string id)
		{
			if (DescribeDeleteSpaceBlocker(id) != null || catalog.HasPending || !SaveCurrentMap() || !MapSpaceStore.Default.TryGet(id, out var space)) return false;
			var deletions = space.mapIds.ToArray(); space.mapIds.Clear();
			void Complete()
			{
				foreach (var anchor in space.AllAnchors()) anchorsToErase.Add(anchor.guid);
				discovery.Forget(id);
				if (CurrentSpace?.id == id)
				{
					CancelTransitionInternal(); maps.Unload(); spaces.Unload(); references.ClearForNoMap(); colocationManager.ClearSpace();
					mapContext = Guid.NewGuid(); referenceContext = Guid.NewGuid(); scan = Guid.NewGuid();
					SetObjectContext(); objects.Replace(Array.Empty<MapObjectEntry>()); WorldFrameRebased.Invoke(); NotifySpace(); NotifyMap();
				}
				SaveCurrentMap();
				if (CurrentSpace == null) StartupProbe(lifetime.Token);
			}
			finishCatalogOperation = Complete;
			if (!catalog.Commit(space, null, deletions, true)) { autosave.Schedule(); return false; }
			finishCatalogOperation = null; Complete(); return true;
		}
		public bool ResetSpaceAlignment()
		{
			if (SyncBus.Active || CurrentSpace == null || IsChangingColocation || !SaveCurrentMap()) return false;
			var space = CurrentSpace;
			foreach (var anchor in space.AllAnchors()) anchorsToErase.Add(anchor.guid);
			space.tags.Clear(); space.anchors.Clear(); space.localAnchors.Clear(); space.retainedReferences.Clear();
			space.associations.Clear(); space.knownFrames.Clear(); space.previousCanonicalFrameId = null;
			space.canonicalFrameId = Guid.NewGuid().ToString("N"); space.frameRevision++;
			space.firstTagId = space.secondTagId = -1; space.initializationPending = true; space.hasPendingSetup = false;
			space.referenceSourceId = space.id; space.referenceVersion = Guid.NewGuid().ToString("N");
			space.preferredColocationMethod = Method.MetaSharedAnchor; space.MarkChanged();
			if (!MapSpaceStore.Default.Save(space)) return false;
			ActivateSpace(space, CurrentMap); return SaveCurrentMap();
		}
		public bool UndoSpaceRebase()
		{
			if (SyncBus.Active || CurrentSpace == null || !SaveCurrentMap()) return false;
			var restored = MapSpaceReconciler.Undo(CurrentSpace);
			if (!MapSpaceStore.Default.Save(restored)) return false;
			ActivateSpace(restored, CurrentMap); return true;
		}
		public bool RenameSpace(string name)
		{
			if (!CanManage || string.IsNullOrWhiteSpace(name)) return false;
			spaces.Rename(TrimName(name)); NotifySpace(); return SaveCurrentMap();
		}
		public const int MaxMapNameLength = 40;
		private static string TrimName(string name) => name.Trim().Substring(0, Mathf.Min(MaxMapNameLength, name.Trim().Length));
		public string DescribeRenameBlocker() => !Authority ? MenuCopy.Get("Game", "blocker.only-the-host-can-rename-the-map") : null;
		public bool RenameMap(string name)
		{
			if (DescribeRenameBlocker() != null || string.IsNullOrWhiteSpace(name) || CurrentMap == null) return false;
			maps.Rename(TrimName(name)); NotifyMap(); return SaveCurrentMap();
		}
		public bool RequestPlaceObject(MapObject prefab, Vector3 position, Quaternion rotation) => CheckCanEditMap() && CurrentMap != null && objects.RequestPlace(prefab, position, rotation);
		public bool RequestRemoveObject(MapObject obj) => CheckCanEditMap() && objects.RequestRemove(obj);
		public void CommitObjectMove(MapObject obj) { if (CheckCanEditMap()) objects.RequestMove(obj); }
		private void OnObjectChanged(MapObject _) => autosave.Schedule();
		private void MarkMapContentChanged() { if (Authority && CurrentMap != null) { CaptureObjects(); autosave.Schedule(); } }
		private void CaptureObjects()
		{ if (Authority && CurrentMap != null && workflow.Phase is MapPhase.Local or MapPhase.Hosting or MapPhase.SwitchingMap && objects.TryCapture(out var snapshot)) maps.SetObjects(snapshot); }
		public bool SaveCurrentMap()
		{
			if (maps == null || spaces == null || catalog.HasPending) return maps == null;
			CaptureObjects();
			if (transientSession) return CacheSession(CurrentSpace, CurrentMap);
			bool saved = maps.Save(SyncBus.Active && SyncBus.IsAuthority) && spaces.Save(SyncBus.Active && SyncBus.IsAuthority);
			if (!saved) { if (!shuttingDown) autosave.Schedule(); return false; }
			if (SyncBus.Active && SyncBus.IsAuthority) session.Publish(CurrentMap, CurrentSpace, mapContext, referenceContext, scan);
			foreach (var id in anchorsToErase) if (!MapSpaceStore.Default.IsAnchorReferenced(id)) references.EraseAnchorSave(id);
			anchorsToErase.Clear(); return true;
		}
		private void OnAutosave()
		{
			if (catalog.HasPending)
			{
				if (!catalog.Recover()) { autosave.Schedule(); return; }
				var completed = finishCatalogOperation; finishCatalogOperation = null; completed?.Invoke();
			}
			if (transientSession && CurrentSpace != null && CurrentMap != null)
			{
				var local = MapSpaceStore.Default.FindAssociation(sessionIdentity.spaceId.ToString("N"));
				if (local != null && MapSpaceReconciler.TryKnownOffset(local, CurrentSpace.canonicalFrameId, out var offset) && TryAdopt(local, CurrentSpace, CurrentMap, offset))
				{ NotifySpace(); NotifyMap(); }
			}
			if (workflow.Phase != MapPhase.Stopped) SaveCurrentMap();
		}

		// Reference revisions are staged separately; the active observer retains its previous target set.
		private void ApplyReferenceChanges()
		{
			if (applyingReferences || CurrentSpace == null || !references.HasPendingSnapshots) return;
			var snapshot = references.TakePendingSnapshot((staged ?? CurrentSpace).Clone(), new SpaceReferenceCapture {
				Anchors = Authority && colocationManager.AnchorProvider.IsRunning &&
					(staged != null ? transitionCommand.target == Method.MetaSharedAnchor :
					 !alignment.Busy && !CurrentSpace.hasPendingSetup),
				TaggedAnchors = colocationManager.TagProvider.IsRunning, Tags = false
			});
			if (snapshot == null) return;
			var current = CurrentSpace;
			// Realizations are private and do not revise or replace the host's tag definitions.
			if (staged != null) staged.localAnchors = snapshot.localAnchors;
			else if (!current.localAnchors.SequenceEqual(snapshot.localAnchors))
			{ current.localAnchors = snapshot.localAnchors.Where(a => current.IsCompatibleLocalAnchor(a)).ToList(); spaces.ReplaceReferences(current, false); autosave.Schedule(); }
			if (!Authority || snapshot.anchors.SequenceEqual((staged ?? current).anchors)) return;
			if (staged != null && transitionCommand.target != Method.MetaSharedAnchor) return;
			if (staged == null && !BeginTransition(Method.MetaSharedAnchor, ReferenceTransitionIntent.SetupOnly, true, SelectAuthor(true))) return;
			staged.anchors = snapshot.anchors; PublishCandidate();
		}
		private ulong? SelectAuthor(bool creates, Method target = Method.MetaSharedAnchor)
		{
			bool canAuthor = target == Method.AprilTag ? CanAuthorTags : CanAuthorReferences;
			if (!SyncBus.Active) return TrackingReady() && (!creates || canAuthor) ? SyncBus.LocalClientId : null;
			if (!HeadsetConfiguration.IsOperatorDevice && (!creates || canAuthor) && TrackingReady()) return SyncBus.LocalClientId;
			foreach (var pair in PlayerAvatar.All.OrderBy(p => p.Key))
				if (ReadyForReferenceWork(pair.Key, creates, target)) return pair.Key;
			return null;
		}
		private bool BeginTransition(Method target, ReferenceTransitionIntent intent, bool creates, ulong? author)
		{
			if (!Authority || CurrentSpace == null || staged != null || alignment.Busy || !author.HasValue) return false;
			var source = CurrentSpace;
			source.hasPendingSetup = true; source.pendingSetupMethod = target; source.pendingSetupIntent = intent;
			source.MarkChanged();
			if (!MapSpaceStore.Default.Save(source)) return false;
			spaces.Load(source); staged = source.Clone();
			transitionCommand = new() { operation = Guid.NewGuid(), referenceContext = referenceContext, target = target,
				intent = intent, createsReferences = creates, author = author.Value };
			alignment.RequireNewTagObservations = transitionCommand.author == SyncBus.LocalClientId;
			alignment.Begin(transitionCommand.operation, CurrentSpace, target, intent, creates);
			registrationRequestedFor = null; PublishCandidate(); SaveCurrentMap(); return true;
		}
		private void PublishCandidate()
		{
			if (staged == null) return;
			transitionCommand.revision = Guid.NewGuid();
			applyingReferences = true; references.Inject(staged); references.ClearPendingSnapshots(); applyingReferences = false;
			alignment.SetCandidate(transitionCommand.revision, staged);
			transitions.Publish(transitionCommand, staged); RaiseSettingsChanged();
		}
		private static MapSpace CandidateInStorage(MapSpace canonical, MapSpace local)
		{
			var copy = local.Clone();
			copy.tags = canonical.tags.ConvertAll(t => new MapTagEntry { id = t.id, canonPose = local.Frame.ToStorage(t.canonPose) });
			copy.anchors = canonical.anchors.ConvertAll(a => new MapAnchorEntry { guid = a.guid, tagId = a.tagId, canonPose = local.Frame.ToStorage(a.canonPose) });
			copy.tagSizeCm = canonical.tagSizeCm; return copy;
		}
		private MapSpace CapturePrivateReferences(MapSpace source)
		{
			if (source == null) return null;
			var copy = source.Clone();
			var provider = colocationManager.TagProvider;
			if (!provider || Mathf.Abs(provider.TagSizeCm - source.tagSizeCm) > .001f) return copy;
			List<TaggedAnchorConstraintData> realized = new(); provider.GetLocalAnchorConstraints(realized);
			foreach (var anchor in realized)
				if (source.TryGetTag(anchor.tagId, out var tag) && provider.RegisteredTags.TryGetValue(anchor.tagId, out var liveTag) &&
					MapSpaceFrame.Near(source.Frame.ToCanonical(tag.canonPose), liveTag, .001f, .1f))
					PrioritizePrivateAnchor(copy.localAnchors, new() { guid = anchor.guid.ToString("N"), tagId = anchor.tagId,
						canonPose = source.Frame.ToStorage(anchor.canonPose), tagCanonPose = tag.canonPose, tagSizeCm = source.tagSizeCm });
			return copy;
		}
		private static void PrioritizePrivateAnchor(List<MapAnchorEntry> anchors, MapAnchorEntry anchor)
		{
			// The adapter restores one realization per tag. Keep the currently live UUID ahead
			// of older saved UUIDs, which remain recorded for save ownership and cleanup.
			anchors.RemoveAll(a => a.guid == anchor.guid); anchors.Insert(0, anchor);
		}
		private static void CopyCompatiblePrivateAnchors(MapSpace target, MapSpace source)
		{
			if (source == null || source.canonicalFrameId != target.canonicalFrameId) return;
			for (int i = source.localAnchors.Count - 1; i >= 0; i--)
			{
				var anchor = source.localAnchors[i];
				if (Mathf.Abs(anchor.tagSizeCm - target.tagSizeCm) < .001f && target.TryGetTag(anchor.tagId, out var tag) &&
					MapSpaceFrame.Near(source.Frame.ToCanonical(anchor.tagCanonPose), target.Frame.ToCanonical(tag.canonPose), .001f, .1f))
				{
					var copy = anchor;
					copy.canonPose = target.Frame.ToStorage(source.Frame.ToCanonical(anchor.canonPose));
					copy.tagCanonPose = tag.canonPose;
					PrioritizePrivateAnchor(target.localAnchors, copy);
				}
			}
		}
		private void RememberCommittedPrivateAnchors(MapSpace candidate)
		{
			var current = CurrentSpace;
			if (current == null || candidate.id != current.id || candidate.storageFrameId != current.storageFrameId ||
				candidate.canonicalFrameId != current.canonicalFrameId || candidate.frameRevision != current.frameRevision) return;
			// The transition header can precede the map-identity callback in a combined snapshot.
			// Retain these private records with their tag metadata until that committed definition arrives.
			for (int i = candidate.localAnchors.Count - 1; i >= 0; i--)
			{
				var anchor = candidate.localAnchors[i];
				if (candidate.IsCompatibleLocalAnchor(anchor)) PrioritizePrivateAnchor(current.localAnchors, anchor);
			}
			spaces.ReplaceReferences(current, false);
			if (transientSession) CacheSession(current, CurrentMap);
			else if (!spaces.Save()) autosave?.Schedule();
		}
		private void PreserveCurrentPrivateReferences(MapSpace incoming)
		{
			var privateReferences = CapturePrivateReferences(staged ?? CurrentSpace);
			if (staged != null) CopyCompatiblePrivateAnchors(staged, privateReferences);
			CopyCompatiblePrivateAnchors(incoming, privateReferences);
		}
		private void OnTransitionReceived(SpaceTransitionCommand command, MapSpace candidate)
		{
			if (command.operation == Guid.Empty) { if (transitionCommand.operation != Guid.Empty && !transitionCommand.committed) CancelTransitionInternal(); transitionCommand = default; return; }
			if (command.referenceContext != referenceContext || CurrentSpace == null || command.space.frameId != CanonicalFrameId) return;
			var privateReferences = CapturePrivateReferences(transitionCommand.operation == command.operation ? staged ?? CurrentSpace : CurrentSpace);
			transitionCommand = command;
			alignment.RequireNewTagObservations = command.author == SyncBus.LocalClientId;
			alignment.Begin(command.operation, CurrentSpace, command.target, command.intent, command.createsReferences);
			staged = CandidateInStorage(candidate, CurrentSpace);
			CopyCompatiblePrivateAnchors(staged, privateReferences);
			applyingReferences = true; references.Inject(staged); references.ClearPendingSnapshots(); applyingReferences = false;
			alignment.SetCandidate(command.revision, staged);
			if (command.committed) { alignment.Commit(); RememberCommittedPrivateAnchors(staged); staged = null; }
		}
		private void OnLocallyValidated(Guid operation, Guid revision)
		{
			if (transitionCommand.author != SyncBus.LocalClientId) return;
			SpaceTransitionEvidence evidence = new() { operation = operation, revision = revision, referenceContext = referenceContext, trackingGeneration = colocationManager.TrackingGeneration };
			if (Authority) OnTargetValidated(SyncBus.LocalClientId, evidence); else transitions.Report(evidence);
		}
		private void OnTargetValidated(ulong sender, SpaceTransitionEvidence evidence)
		{
			if (!Authority || staged == null || transitionCommand.committed || sender != transitionCommand.author ||
				evidence.trackingGeneration != SenderTrackingGeneration(sender) || evidence.operation != transitionCommand.operation || evidence.revision != transitionCommand.revision || evidence.referenceContext != referenceContext ||
				!ReadyForReferenceWork(sender, transitionCommand.createsReferences, transitionCommand.target)) return;
			var saved = CurrentSpace.ApplyReferenceCandidate(staged);
			if (transitionCommand.createsReferences) MarkReferencesAuthored(saved);
			bool leavesProvisional = !ColocationManager.UsesSavedReferences(CurrentSpace.preferredColocationMethod) && transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget;
			if (transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget) saved.preferredColocationMethod = transitionCommand.target;
			saved.hasPendingSetup = false; saved.initializationPending = !saved.HasReferenceBasedData; saved.MarkChanged();
			alignment.Transition?.Persisting();
			CaptureObjects();
			if (!maps.Save(SyncBus.Active) || !MapSpaceStore.Default.Save(saved)) { autosave.Schedule(); return; }
			spaces.Load(saved); NotifySpace();
			if (leavesProvisional) { scan = Guid.NewGuid(); WorldFrameRebased.Invoke(); }
			transitionCommand.committed = true;
			// Publish the configuration before committing selection. Peers may retain their source after this point.
			SaveCurrentMap(); transitions.Publish(transitionCommand, saved);
			if (transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget) colocationManager.CommitMethod(transitionCommand.target);
			alignment.Commit(requireLocalHandoff: !HeadsetConfiguration.IsOperatorDevice);
			staged = null; colocationManager.AnchorProvider.FinishSessionSharing();
		}
		private void MarkReferencesAuthored(MapSpace saved)
		{
			var source = CurrentSpace;
			if (source.referenceSourceId != source.id) { source.RetainActiveReferences(); saved.retainedReferences = source.retainedReferences; }
			saved.referenceSourceId = saved.id; saved.referenceVersion = Guid.NewGuid().ToString("N"); saved.referenceDirty = !SyncBus.Active;
		}
		public void CancelAlignmentTransition()
		{
			if (transitionCommand.committed) return;
			if (Authority)
			{
				var space = CurrentSpace;
				if (space != null) { space.hasPendingSetup = false; space.MarkChanged(); if (!MapSpaceStore.Default.Save(space)) return; spaces.Load(space); }
				CancelTransitionInternal(); transitions.Publish(default, null);
				NotifySpace(); SaveCurrentMap();
			}
			else methodRequest.Raise(new() { context = referenceContext, cancel = true });
		}
		private void CancelTransitionInternal()
		{
			nativePreparation?.Cancel(); nativePreparation = null;
			alignment.Cancel(); staged = null; transitionCommand = default;
			colocationManager.AnchorProvider?.FinishSessionSharing();
			if (CurrentSpace != null) { references.Inject(CurrentSpace); references.ClearPendingSnapshots(); }
		}
		public string DescribeColocationPreferenceBlocker() => CurrentSpace == null ? MenuCopy.Get("Game", "space.load-first") :
			RoundInProgress ? MenuCopy.Get("Game", "blocker.wait-until-the-round-ends") :
			HeadsetConfiguration.SessionIsOperatorManaged && !HeadsetConfiguration.IsOperatorDevice ? MenuCopy.Get("Game", "blocker.the-operator-sets-the-alignment-method") : null;
		public string DescribeColocationMethodBlocker(Method method) => !ColocationManager.IsValidMethod(method) ?
			MenuCopy.Get("Game", "blocker.unknown-colocation-method") : DescribeColocationPreferenceBlocker();
		public bool SetPreferredColocationMethod(Method method)
		{
			if (DescribeColocationMethodBlocker(method) != null) return false;
			var request = new MethodRequest { context = referenceContext, method = method };
			if (Authority) { bool accepted = RequestMethod(SyncBus.LocalClientId, request); if (!accepted) RejectRequest(SyncBus.LocalClientId, request.context, Guid.Empty); return accepted; }
			methodRequest.Raise(request); return true;
		}
		private void OnMethodRequested(ulong sender, MethodRequest request) { if (Authority && !RequestMethod(sender, request)) RejectRequest(sender, request.context, Guid.Empty); }
		private void RejectRequest(ulong sender, Guid context, Guid operation)
		{
			var rejection = new RequestRejection { recipient = sender, context = context, operation = operation };
			if (!SyncBus.Active || sender == SyncBus.LocalClientId) OnRequestRejected(SyncBus.LocalClientId, rejection);
			else requestRejection.Raise(rejection);
		}
		private void OnRequestRejected(ulong _, RequestRejection rejection)
		{
			if (rejection.recipient != SyncBus.LocalClientId || rejection.context != referenceContext ||
				rejection.operation != (alignment.Busy ? transitionCommand.operation : Guid.Empty)) return;
			UserErrors.RaiseLocalized(UserErrorArea.Game, "error.alignment-title", "error.alignment-details", MenuCopy.Get("Game", "alignment.request-rejected"));
			RaiseSettingsChanged();
		}
		private bool RequestMethod(ulong sender, MethodRequest request)
		{
			if (request.context != referenceContext || RoundInProgress || CurrentSpace == null ||
				(HeadsetConfiguration.SessionIsOperatorManaged && sender != SyncBus.LocalClientId)) return false;
			if (request.cancel) { CancelAlignmentTransition(); return true; }
			if (!ColocationManager.IsValidMethod(request.method)) return false;
			CancelTransitionInternal(); transitions.Publish(default, null);
			if (!ColocationManager.UsesSavedReferences(request.method))
			{
				var changed = CurrentSpace; changed.hasPendingSetup = false; changed.preferredColocationMethod = request.method; changed.MarkChanged();
				if (!MapSpaceStore.Default.Save(changed)) return false;
				spaces.Load(changed); NotifySpace(); scan = Guid.NewGuid(); WorldFrameRebased.Invoke();
				SaveCurrentMap(); colocationManager.CommitMethod(request.method); alignment.Activate(changed, request.method); return true;
			}
			bool setup = !MapSpaceAlignmentController.HasConfiguration(CurrentSpace, request.method);
			var author = SelectAuthor(setup, request.method);
			if (!author.HasValue)
			{
				// Choosing a method is not evidence of alignment. Keep the intent while a headset
				// connects, regains focus/tracking, or localizes this space's existing references.
				var pending = CurrentSpace; pending.hasPendingSetup = true;
				pending.pendingSetupMethod = request.method; pending.pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget;
				pending.MarkChanged();
				if (!MapSpaceStore.Default.Save(pending)) return false;
				spaces.Load(pending); NotifySpace(); SaveCurrentMap(); return true;
			}
			if (!BeginTransition(request.method, ReferenceTransitionIntent.ActivateTarget, setup, author)) return false;
			if (request.method == Method.MetaSharedAnchor && setup) PrepareAnchors(transitionCommand.operation);
			return true;
		}
		private async void PrepareAnchors(Guid operation)
		{
			if (!SyncBus.Active) return; // The local native provider mints under the same source gate.
			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); nativePreparation = cancellation;
			cancellation.CancelAfter(TimeSpan.FromSeconds(20));
			try
			{
				var prepared = await colocationManager.AnchorProvider.PrepareSessionSharingAsync(cancellation.Token);
				if (cancellation.IsCancellationRequested || staged == null || transitionCommand.operation != operation) return;
				foreach (var anchor in prepared) staged.SetAnchorWithTag(anchor.guid.ToString("N"), staged.Frame.ToStorage(anchor.canonPose), anchor.bindingId);
				if (prepared.Count == 0) { CancelAlignmentTransition(); return; }
				PublishCandidate();
			}
			catch (OperationCanceledException) { if (transitionCommand.operation == operation) CancelAlignmentTransition(); }
			catch (Exception e) { Debug.LogException(e); if (transitionCommand.operation == operation) CancelAlignmentTransition(); }
			finally { if (nativePreparation == cancellation) nativePreparation = null; cancellation.Dispose(); }
		}
		private void OnAlignmentSourceChanged()
		{
			var next = colocationManager.ActiveMethod;
			if (next != observedSource && (!ColocationManager.UsesSavedReferences(next) || !ColocationManager.UsesSavedReferences(observedSource)))
				WorldFrameRebased.Invoke();
			observedSource = next; RaiseSettingsChanged();
		}
		private void OnSelectedMethodChanged()
		{
			if (CurrentSpace == null || !SyncBus.Active || SyncBus.IsAuthority) { if (CurrentSpace != null && SyncBus.Active && SyncBus.IsAuthority) SaveCurrentMap(); RaiseSettingsChanged(); return; }
			// Selection can arrive before the matching snapshot. The snapshot/transition owns activation.
			RaiseSettingsChanged();
		}
		public bool SessionIsWaitingOnFirstTag => alignment.Busy && alignment.Transition.Target == Method.AprilTag &&
			alignment.Transition.CreatesReferences && (staged?.tags.Count ?? CurrentSpace?.tags.Count ?? 0) == 0;
		private void CheckTransitionAuthor()
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !alignment.Busy || transitionCommand.committed || transitionCommand.author == SyncBus.LocalClientId) return;
			if (NetworkManager.Singleton && NetworkManager.Singleton.ConnectedClientsIds.Contains(transitionCommand.author)) return;
			// Durable intent remains, but the next author must start a fresh operation and observation interval.
			CancelTransitionInternal(); transitions.Publish(default, null);
		}
		private void ResumePendingSetup()
		{
			if (!Authority || alignment.Busy || staged != null || RoundInProgress || CurrentSpace?.hasPendingSetup != true || catalog.HasPending) return;
			var space = CurrentSpace; bool creates = !MapSpaceAlignmentController.HasConfiguration(space, space.pendingSetupMethod);
			var author = SelectAuthor(creates, space.pendingSetupMethod); if (!author.HasValue) return;
			if (BeginTransition(space.pendingSetupMethod, space.pendingSetupIntent, creates, author) && creates && space.pendingSetupMethod == Method.MetaSharedAnchor)
				PrepareAnchors(transitionCommand.operation);
		}
		private void RequestTagRegistrationIfNeeded()
		{
			if (!SessionIsWaitingOnFirstTag || !CanAuthorTags || transitionCommand.author != SyncBus.LocalClientId || HeadsetConfiguration.IsOperatorDevice || registrationRequestedFor == transitionCommand.operation.ToString()) return;
			registrationRequestedFor = transitionCommand.operation.ToString(); MapEditor.MapEditor.RequestTagRegistration();
		}
		public string DescribeTagSetupBlocker() => DescribeTagRegistrationBlocker();
		public string DescribeTagRegistrationBlocker() => CanAuthorTags ? null : MenuCopy.Get("Game", "alignment.align-before-reference-setup");
		public string DescribeTagRemovalBlocker() => CurrentSpace == null ? MenuCopy.Get("Game", "space.load-first") : IsChangingColocation ? MenuCopy.Get("Game", "space.busy") : null;
		public string DescribeTagSizeBlocker() => CurrentSpace == null ? MenuCopy.Get("Game", "space.load-first") : (CurrentSpace.HasTags || (staged?.HasTags ?? false) ? MenuCopy.Get("Game", "blocker.unregister-this-map-s-tags-to-change-their-size") : null);
		public float EffectiveTagSizeCm => staged?.tagSizeCm ?? CurrentSpace?.tagSizeCm ?? MapSpace.DefaultTagSizeCm;
		private void SubmitReference(ReferenceRequest request)
		{ request.context = referenceContext; request.trackingGeneration = colocationManager.TrackingGeneration; request.operation = alignment.Busy ? transitionCommand.operation : Guid.Empty; if (Authority) OnReferenceRequested(SyncBus.LocalClientId, request); else referenceRequest.Raise(request); }
		public bool RegisterTag(int id, Pose worldPose)
		{
			if (DescribeTagRegistrationBlocker() != null || id < 0 || !MapSpaceFrame.ValidPose(worldPose)) return false;
			SubmitReference(new() { action = ReferenceAction.Register, tag = id, pose = worldPose }); return true;
		}
		public bool UnregisterTag(int id)
		{ if (DescribeTagRemovalBlocker() != null) return false; SubmitReference(new() { action = ReferenceAction.Remove, tag = id }); return true; }
		public void UnregisterAllTags() { if (CurrentSpace != null) foreach (var tag in CurrentSpace.tags) UnregisterTag(tag.id); }
		public bool SetTagSize(float value)
		{ if (!float.IsFinite(value) || value <= 0 || DescribeTagSizeBlocker() != null) return false; SubmitReference(new() { action = ReferenceAction.Size, size = value }); return true; }
		private void OnReferenceRequested(ulong sender, ReferenceRequest request)
		{
			if (Authority && !ApplyReferenceRequest(sender, request)) RejectRequest(sender, request.context, request.operation);
		}
		private bool ApplyReferenceRequest(ulong sender, ReferenceRequest request)
		{
			if (!Authority || request.context != referenceContext || CurrentSpace == null ||
				request.operation != (alignment.Busy ? transitionCommand.operation : Guid.Empty)) return false;
			if (request.action == ReferenceAction.Register)
			{
				if (request.tag < 0 || request.trackingGeneration != SenderTrackingGeneration(sender) || !MapSpaceFrame.ValidPose(request.pose) || !ReadyForReferenceWork(sender, true, Method.AprilTag)) return false;
				if (staged == null && !BeginTransition(Method.AprilTag, ReferenceTransitionIntent.SetupOnly, true, sender)) return false;
				if (transitionCommand.target != Method.AprilTag || transitionCommand.author != sender) return false;
				var candidate = CapturePrivateReferences(staged);
				candidate.SetTag(request.tag, candidate.Frame.ToStorage(request.pose));
				var saved = CurrentSpace.ApplyReferenceCandidate(candidate);
				MarkReferencesAuthored(saved); saved.initializationPending = false; saved.MarkChanged();
				// A registered indication must acknowledge a saved definition, not a candidate
				// that disappears if the headset disconnects before the method handoff.
				if (!MapSpaceStore.Default.Save(saved)) return false;
				spaces.Load(saved); staged = saved.Clone(); transitionCommand.createsReferences = false;
				alignment.Begin(transitionCommand.operation, saved, transitionCommand.target, transitionCommand.intent, false);
				PublishCandidate(); NotifySpace(); SaveCurrentMap(); return true;
			}
			if (alignment.Busy)
			{
				if (request.action == ReferenceAction.Size && staged != null && !CurrentSpace.HasTags && !staged.HasTags && float.IsFinite(request.size) && request.size > 0)
				{ staged.tagSizeCm = request.size; PublishCandidate(); return true; }
				return false;
			}
			var space = CurrentSpace;
			switch (request.action)
			{
				case ReferenceAction.Remove:
					space.tags.RemoveAll(t => t.id == request.tag);
					foreach (var anchor in space.localAnchors.Where(a => a.tagId == request.tag)) anchorsToErase.Add(anchor.guid);
					space.localAnchors.RemoveAll(a => a.tagId == request.tag);
					foreach (var anchor in space.anchors.Where(a => a.tagId == request.tag)) anchorsToErase.Add(anchor.guid);
					space.anchors.RemoveAll(a => a.tagId == request.tag);
					space.referenceSourceId = space.id; space.referenceVersion = Guid.NewGuid().ToString("N"); space.referenceDirty = !SyncBus.Active; break;
				case ReferenceAction.Size:
					if (space.HasTags || !float.IsFinite(request.size) || request.size <= 0) return false;
					space.tagSizeCm = request.size; break;
				case ReferenceAction.ForgetPair:
					if (DescribeColocationPreferenceBlocker() != null || (HeadsetConfiguration.SessionIsOperatorManaged && sender != SyncBus.LocalClientId)) return false;
					space.firstTagId = space.secondTagId = -1;
					break;
				case ReferenceAction.Pair:
					if (request.trackingGeneration != SenderTrackingGeneration(sender) || request.tag < 0 || request.second <= request.tag || space.firstTagId >= 0) return false;
					space.firstTagId = request.tag; space.secondTagId = request.second; break;
				default: return false;
			}
			space.MarkChanged(); if (!MapSpaceStore.Default.Save(space)) return false;
			spaces.Load(space); references.Inject(space); references.ClearPendingSnapshots(); NotifySpace();
			alignment.Activate(space, colocationManager.ActiveMethod, true);
			if (colocationManager.ActiveMethod == Method.TwoAprilTags && request.action is ReferenceAction.Size or ReferenceAction.ForgetPair)
			{ colocationManager.TwoTagProvider.ResetReferences(); colocationManager.InvalidateAlignment(); alignment.Activate(space, Method.TwoAprilTags); scan = Guid.NewGuid(); WorldFrameRebased.Invoke(); }
			SaveCurrentMap(); return true;
		}
		private void OnPairSelected(int first, int second)
		{
			if (CurrentSpace == null) return;
			if (SyncBus.Active && !SyncBus.IsAuthority) colocationManager.TwoTagProvider.ConfigurePair(CurrentSpace.firstTagId, CurrentSpace.secondTagId);
			SubmitReference(new() { action = ReferenceAction.Pair, tag = first, second = second });
		}
		public void ChooseAnotherTagPair() => SubmitReference(new() { action = ReferenceAction.ForgetPair });

		// Shared-anchor assignment is scoped to the space/frame, independent of the active map.
		private void ConfigureAnchorMinter()
		{
			var provider = colocationManager.AnchorProvider; if (!provider) return;
			provider.SelectMinter = SelectAnchorMinter;
			provider.LocalReadinessGate = () => TrackingReady();
			provider.ValidateRemoteMint = sender => ReadyForReferenceWork(sender, true, Method.MetaSharedAnchor);
			provider.TrackingGeneration = () => colocationManager.TrackingGeneration;
			provider.ValidateTrackingGeneration = (sender, generation) => generation == SenderTrackingGeneration(sender);
			provider.PreparationGate = () => CanAuthorReferences;
			provider.PreparationMintingGate = () => CanAuthorReferences;
			provider.SharingCandidates = LocalSharingCandidates;
			provider.ValidatePreparedAnchor = (sender, _) => ReadyForReferenceWork(sender, true, Method.MetaSharedAnchor);
		}
		private void ClearAnchorMinter()
		{
			var p = colocationManager.AnchorProvider; if (!p) return;
			p.SelectMinter = null; p.LocalReadinessGate = null; p.ValidateRemoteMint = null; p.TrackingGeneration = null; p.ValidateTrackingGeneration = null;
			p.PreparationGate = null; p.PreparationMintingGate = null; p.SharingCandidates = null; p.ValidatePreparedAnchor = null;
		}
		private ulong? SelectAnchorMinter()
		{
			var manager = NetworkManager.Singleton; if (!manager || CurrentSpace == null) return null;
			anchorCandidates.Clear();
			foreach (var pair in PlayerAvatar.All)
				if (pair.Value && pair.Value.IsSpawned && pair.Value.HeadsetStatus && manager.ConnectedClientsIds.Contains(pair.Key)) anchorCandidates.Add(new(pair.Key, pair.Value.HeadsetStatus.Readiness));
			var assigned = colocationManager.AnchorProvider.Minter;
			return AnchorMinterPolicy.Select(assigned.assigned ? assigned.clientId : null, manager.CurrentSessionOwner,
				HeadsetConfiguration.SessionIsOperatorManaged, SessionSpaceId, CanonicalFrameId, referenceContext, anchorCandidates);
		}
		private int SenderTrackingGeneration(ulong sender) => sender == SyncBus.LocalClientId ? colocationManager.TrackingGeneration :
			PlayerAvatar.All.TryGetValue(sender, out var avatar) && avatar && avatar.HeadsetStatus ? avatar.HeadsetStatus.Readiness.trackingGeneration : -1;
		private bool ReadyForReferenceWork(ulong sender, bool creates, Method target)
		{
			if (CurrentSpace == null) return false;
			if (sender == SyncBus.LocalClientId) return TrackingReady() && (!creates || (target == Method.AprilTag ? CanAuthorTags : CanAuthorReferences));
			if (!PlayerAvatar.All.TryGetValue(sender, out var avatar) || !avatar || !avatar.IsSpawned || !avatar.HeadsetStatus) return false;
			var r = avatar.HeadsetStatus.Readiness;
			bool initializationGrant = target == Method.AprilTag
				? MapPolicy.CanInitializeTags(SyncBus.Active, HeadsetConfiguration.SessionIsOperatorManaged && Authority, alignment.Busy,
					transitionCommand.target == Method.AprilTag && transitionCommand.author == sender)
				: colocationManager.AnchorProvider.Minter.assigned && colocationManager.AnchorProvider.Minter.clientId == sender;
			return r.isFocused && r.isHeadTracked && !r.isOperator && r.spaceId == SessionSpaceId && r.frameId == CanonicalFrameId && r.referenceContext == referenceContext &&
				(!creates || MapPolicy.CanAuthorReferences(CurrentSpace.HasReferenceBasedData, r.activeMethod, r.referenceFrameTrusted, initializationGrant));
		}
		private IReadOnlyList<AnchorConstraintData> LocalSharingCandidates()
		{
			List<AnchorConstraintData> result = new();
			if (colocationManager.ActiveMethod == Method.AprilTag)
			{
				List<TaggedAnchorConstraintData> local = new(); colocationManager.TagProvider.GetLocalAnchorConstraints(local);
				foreach (var a in local) result.Add(new(a.guid, a.canonPose, a.tagId));
			}
			else foreach (var a in colocationManager.AnchorProvider.Constraints) result.Add(new(a.Key, a.Value.canonPose, a.Value.bindingId));
			return result;
		}

		// Joining may use a session snapshot immediately; durable reconciliation needs verified geometry.
		private void OnBusActivated()
		{
			if (!SyncBus.IsAuthority && workflow.Phase == MapPhase.Local && CurrentMap != null) maps.SetObjects(objects.CaptureLocal());
			CaptureObjects(); SaveCurrentMap(); beforeSessionSpace = CurrentSpace; beforeSessionMap = CurrentMap;
			workflow.EnterSession(SyncBus.IsAuthority); discovery.Invalidate(); incomingGeneration++;
		}
		private void OnAuthorityReady()
		{
			workflow.BeginHosting(false, Time.unscaledTime);
			if (CurrentSpace == null)
			{
				var last = MapSpaceStore.Default.Spaces.FirstOrDefault();
				if (HeadsetConfiguration.IsOperatorDevice && last != null) ChangeSpace(last.id);
				else if (!HeadsetConfiguration.IsOperatorDevice) StartupProbe(lifetime.Token);
			}
			if (CurrentMap == null && CurrentSpace != null) NewMap();
			if (CurrentSpace != null) { NotifySpace(); SaveCurrentMap(); objects.SpawnLocalObjects(); }
		}
		private void OnAuthorityChanged(bool authority)
		{
			CancelTransitionInternal(); incomingGeneration++;
			if (authority) { referenceContext = Guid.NewGuid(); scan = Guid.NewGuid(); WorldFrameRebased.Invoke(); transitions.Publish(default, null); OnAuthorityReady(); }
			else workflow.EnterSession(false);
		}
		private void OnSessionMapReceived(MapIdentity identity, List<MapObjectEntry> placements, MapSpace remote)
		{
			if (identity.referenceContext == Guid.Empty || identity.context == Guid.Empty || identity.frameId == Guid.Empty || !remote.Validate()) return;
			var map = new GameMap { id = identity.id.ToString("N"), version = identity.version.ToString("N"), name = identity.name.ToString(), storageFrameId = remote.storageFrameId, objects = placements };
			if (!map.Validate()) return;
			int operation = ++incomingGeneration;
			bool referencesChanged = sessionIdentity.spaceVersion != identity.spaceVersion;
			bool newFrame = sessionIdentity.spaceId != identity.spaceId || sessionIdentity.frameId != identity.frameId || referenceContext != identity.referenceContext;
			// Host snapshots contain no device-private tag anchors. Preserve this session's local
			// realizations before adoption writes the committed space or replaces the live provider.
			if (!newFrame) PreserveCurrentPrivateReferences(remote);
			var local = MapSpaceStore.Default.FindAssociation(remote.id) ?? beforeSessionSpace;
			sessionIdentity = identity; mapContext = identity.context; referenceContext = identity.referenceContext;
			bool scanChanged = scan != identity.scan; scan = identity.scan;
			if (newFrame) { CancelTransitionInternal(); objects.RemoveLocalObjects(); }
			if (local != null && (MapSpaceReconciler.TryKnownOffset(local, remote.canonicalFrameId, out var known) || CanReuseEmptyDraft(local, out known)) && TryAdopt(local, remote, map, known))
				ActivateIncoming(identity, remote, newFrame, scanChanged, referencesChanged);
			else
			{
				transientSession = true; spaces.Load(remote); maps.Load(map); CacheSession(remote, map);
				ActivateIncoming(identity, remote, newFrame, scanChanged, referencesChanged);
				if (local != null) ReconcileSession(local, remote, map, operation, lifetime.Token);
				else if (beforeSessionSpace == null) AdoptNewSessionSpace(remote, map);
			}
		}
		private void ActivateIncoming(MapIdentity identity, MapSpace remote, bool newFrame, bool scanChanged, bool referencesChanged)
		{
			workflow.BeginAdoption(CurrentMap, newFrame); workflow.FinishAdoption();
			references.Inject(staged ?? CurrentSpace); references.ClearPendingSnapshots(); NotifySpace(); NotifyMap();
			if (newFrame) { colocationManager.InvalidateAlignment(); alignment.Activate(CurrentSpace, identity.method); }
			else if (!alignment.Busy && colocationManager.ActiveMethod != identity.method)
			{
				if (ColocationManager.UsesSavedReferences(identity.method))
				{ alignment.Begin(Guid.NewGuid(), CurrentSpace, identity.method, ReferenceTransitionIntent.ActivateTarget, false); alignment.SetCandidate(identity.spaceVersion, CurrentSpace); alignment.Commit(); }
				else alignment.Activate(CurrentSpace, identity.method);
			}
			else if (!alignment.Busy && referencesChanged) alignment.Activate(CurrentSpace, colocationManager.ActiveMethod, true);
			if (scanChanged && colocationManager.ActiveMethod == Method.TwoAprilTags)
			{ colocationManager.TwoTagProvider.ResetReferences(); colocationManager.InvalidateAlignment(); alignment.Activate(CurrentSpace, Method.TwoAprilTags); }
			if (newFrame || scanChanged) WorldFrameRebased.Invoke();
		}
		private static bool CanReuseEmptyDraft(MapSpace local, out Pose offset)
		{
			offset = Pose.identity;
			if (local.HasReferenceBasedData) return false;
			foreach (var id in local.mapIds) if (MapStore.Default.Read(id, out var map) != DocumentReadStatus.Found || !map.IsEmpty) return false;
			return true;
		}
		private bool TryAdopt(MapSpace local, MapSpace remote, GameMap incoming, Pose offset)
		{
			var owner = MapSpaceStore.Default.FindOwner(incoming.id);
			if (catalog.HasPending || MapSpaceReconciler.HasFrameContradiction(local, remote, offset) || (owner != null && owner.id != local.id)) return false;
			var adopted = MapSpaceReconciler.ImportReferences(local, remote, offset);
			CopyCompatiblePrivateAnchors(adopted, remote);
			var read = MapStore.Default.Read(incoming.id, out var previous);
			if (read == DocumentReadStatus.Unavailable) return false;
			var writes = MapSpaceReconciler.PrepareMapImport(adopted, incoming, previous);
			var map = writes[writes.Count - 1];
			if (!catalog.Commit(adopted, writes)) { autosave.Schedule(); return false; }
			spaces.Load(adopted); maps.Load(map); transientSession = false; return true;
		}
		private void AdoptNewSessionSpace(MapSpace remote, GameMap map)
		{
			// No local layouts to reframe. One associated entry is sufficient for all later visits.
			var fresh = MapSpace.Create(remote.name); fresh.canonicalFrameId = remote.canonicalFrameId;
			fresh.storageFrameId = remote.canonicalFrameId;
			if (TryAdopt(fresh, remote, map, Pose.identity)) { NotifySpace(); NotifyMap(); }
		}
		private async void ReconcileSession(MapSpace local, MapSpace remote, GameMap map, int operation, CancellationToken token)
		{
			if (!local.HasReferenceBasedData || !remote.HasReferenceBasedData) return;
			Guid context = referenceContext;
			var localReferences = ProbeReferenceSet(local); var remoteReferences = ProbeReferenceSet(remote);
			using var a = new MapSpaceReferenceObservation(localReferences, ReferenceMethod(localReferences), AnchorRegistry.Instance, colocationManager.TagProvider);
			using var b = new MapSpaceReferenceObservation(remoteReferences, ReferenceMethod(remoteReferences), AnchorRegistry.Instance, colocationManager.TagProvider);
			using var aLease = a.Observe(); using var bLease = b.Observe();
			List<ColocationConstraint> ac = new(), bc = new(); double stable = -1; Pose previous = default; int generation = colocationManager.TrackingGeneration;
			try
			{
				while (!token.IsCancellationRequested && SyncBus.Active && !SyncBus.IsAuthority && operation == incomingGeneration)
				{
					await Awaitable.NextFrameAsync(token);
					if (!IsCurrentReconciliation(remote, operation, context)) return;
					ac.Clear(); bc.Clear(); a.GetColocationConstraints(ac); b.GetColocationConstraints(bc);
					if (!TrackingReady() || !ColocationFit.TryEvaluate(ac, out var da, out var ea, out var aa) || !ColocationFit.TryEvaluate(bc, out var db, out var eb, out var ab) || ea > .05f || eb > .05f || aa > 5 || ab > 5)
					{ stable = -1; continue; }
					var offset = MapSpaceFrame.Compose(MapSpaceFrame.Compose(db, MapSpaceFrame.Inverse(da)), local.canonicalFromStorage);
					if (generation != colocationManager.TrackingGeneration || stable < 0 || !MapSpaceFrame.Near(offset, previous, .02f, 1))
					{ stable = Time.realtimeSinceStartupAsDouble; generation = colocationManager.TrackingGeneration; }
					previous = offset;
					if (Time.realtimeSinceStartupAsDouble - stable < 1) continue;
					if (TryFinishSessionReconciliation(local, remote, map, offset, operation, context)) { NotifySpace(); NotifyMap(); return; }
					return;
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}
		private bool IsCurrentReconciliation(MapSpace remote, int operation, Guid context) =>
			SyncBus.Active && !SyncBus.IsAuthority && operation == incomingGeneration && context == referenceContext &&
			sessionIdentity.spaceId == Guid.Parse(remote.id) && sessionIdentity.frameId == Guid.Parse(remote.canonicalFrameId) &&
			CanonicalFrameId == sessionIdentity.frameId;
		private bool TryFinishSessionReconciliation(MapSpace local, MapSpace remote, GameMap map, Pose offset, int operation, Guid context)
		{
			if (!IsCurrentReconciliation(remote, operation, context)) return false;
			// Native realization can finish while physical reconciliation awaits observations.
			// Merge it into the original host snapshot immediately before the catalog write.
			var latest = remote.Clone(); PreserveCurrentPrivateReferences(latest);
			return TryAdopt(local, latest, map, offset);
		}
		private static Method ReferenceMethod(MapSpace space) => space.anchors.Count > 0 ? Method.MetaSharedAnchor : Method.AprilTag;
		private static MapSpace ProbeReferenceSet(MapSpace space)
		{
			var copy = space.Clone();
			// Retained UUIDs still represent this storage frame. Keep tag definitions source-separated.
			if (copy.anchors.Count == 0) copy.anchors = space.AllAnchors().ToList();
			if (copy.anchors.Count == 0 && !copy.HasTags && copy.retainedReferences.Count > 0)
			{ var source = copy.retainedReferences.FirstOrDefault(r => r.tags.Count > 0); if (source != null) { copy.tags = new(source.tags); copy.tagSizeCm = source.tagSizeCm; } }
			return copy;
		}
		[Serializable] private sealed class CachedSession { public MapSpace space; public GameMap map; }
		private static bool CacheSession(MapSpace space, GameMap map) => space == null || map == null ||
			AtomicJsonFile.Write(Path.Combine(MapStore.CatalogRoot, "session-cache", space.id + ".json"), JsonUtility.ToJson(new CachedSession { space = space, map = map }, true));
		private void OnBusDeactivated()
		{
			incomingGeneration++; CancelTransitionInternal();
			if (workflow.Phase == MapPhase.Stopped) return;
			bool preserveFrame = !transientSession;
			int operation = workflow.BeginRestore();
			if (transientSession) { spaces.Load(beforeSessionSpace); maps.Load(beforeSessionMap); transientSession = false; }
			RebuildAfterSession(operation, lifetime.Token, preserveFrame);
		}
		private async void RebuildAfterSession(int operation, CancellationToken token, bool preserveFrame)
		{
			try
			{
				do { await Awaitable.NextFrameAsync(token); if (!workflow.IsCurrentRestore(operation)) return; }
				while (NetworkManager.Singleton && (NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress));
				workflow.EnterLocal(); var space = CurrentSpace; var map = CurrentMap;
				if (space != null && preserveFrame)
				{
					sessionIdentity = default; mapContext = Guid.NewGuid(); referenceContext = Guid.NewGuid();
					references.Inject(space); references.ClearPendingSnapshots(); NotifySpace();
					colocationManager.CommitMethod(colocationManager.ActiveMethod);
					alignment.Activate(space, colocationManager.ActiveMethod, true);
					SetObjectContext(); objects.Replace(map?.objects ?? new()); NotifyMap();
				}
				else if (space != null) ActivateSpace(space, map);
				else { references.ClearForNoMap(); colocationManager.ClearSpace(); objects.Replace(Array.Empty<MapObjectEntry>()); NotifySpace(); NotifyMap(); StartupProbe(token); }
			}
			catch (OperationCanceledException) { }
		}

		// Sharing support and room discovery are independent. Local anchors may work on an MDM headset.
		private void FallbackFromUnavailableSharing()
		{
			if (!Authority || HeadsetConfiguration.IsOperatorDevice || !TrackingReady() || catalog.HasPending || RoundInProgress) return;
			if (alignment.Busy && transitionCommand.target != Method.MetaSharedAnchor) return;
			var space = CurrentSpace;
			if (space?.preferredColocationMethod != Method.MetaSharedAnchor || SharedAnchorSupport != CapabilitySupport.Unsupported ||
				!MapSpaceStartup.ConfigureDraft(space, Method.AprilTag)) return;
			if (!MapSpaceStore.Default.Save(space)) { autosave.Schedule(); return; }
			CancelTransitionInternal(); spaces.Load(space); references.ClearPendingSnapshots(); references.Inject(space);
			NotifySpace(); alignment.Activate(space, Method.AprilTag); colocationManager.CommitMethod(Method.AprilTag);
			SaveCurrentMap();
		}

		// A healthy completed probe may create one reusable default draft. Failures never mean an empty room.
		private async void StartupProbe(CancellationToken token)
		{
			if (HeadsetConfiguration.IsOperatorDevice)
			{ if (CurrentSpace == null && !SyncBus.Active) { var last = MapSpaceStore.Default.Spaces.FirstOrDefault(); if (last != null) ChangeSpace(last.id); } return; }
#if UNITY_EDITOR
			if (MainXRRig.Instance && !XRSettings.enabled) { RestoreLastMapForSimulation(); return; }
#endif
			try
			{
				while (!token.IsCancellationRequested && Authority && CurrentSpace == null)
				{ await ProbeAndAutoLoad(token); if (CurrentSpace == null) await Awaitable.WaitForSecondsAsync(2f, token); }
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}
#if UNITY_EDITOR
		public bool RestoreLastMapForSimulation()
		{
			if (!MainXRRig.Instance || XRSettings.enabled || SyncBus.Active || CurrentSpace != null) return true;
			var last = MapSpaceStore.Default.Spaces.FirstOrDefault();
			// Restoring a space must restore its references and chosen provider together.
			// Only a brand-new simulation space starts with the system origin.
			return last != null ? ChangeSpace(last.id) : CreateSpace(false, Method.SystemDetermined);
		}
#endif
		public async Awaitable ProbeAndAutoLoad(CancellationToken token = default)
		{
			if (!Authority || CurrentSpace != null || catalog.HasPending || !TrackingReady()) return;
			int generation = incomingGeneration, tracking = colocationManager.TrackingGeneration;
			var result = await discovery.ProbeAsync(token);
			if (token.IsCancellationRequested || !Authority || CurrentSpace != null || generation != incomingGeneration || tracking != colocationManager.TrackingGeneration) return;
			if (result.outcome == SpaceProbeOutcome.Matches) ChangeSpace(result.best.id);
			else if (MapSpaceStartup.InitialMethod(result.outcome, SharedAnchorSupport, discovery.IsAvailable) is Method initialMethod)
			{
				if (!MapSpaceStore.Default.IsAvailable) return;
				var draft = MapSpaceStore.Default.Spaces.FirstOrDefault(s => s.automaticallyCreated && s.initializationPending && !s.HasReferenceBasedData);
				if (draft != null)
				{
					if (draft.preferredColocationMethod == Method.MetaSharedAnchor) MapSpaceStartup.ConfigureDraft(draft, initialMethod);
					if (MapSpaceStore.Default.Save(draft)) ChangeSpace(draft.id);
				}
				else CreateSpace(true, initialMethod);
			}
		}
		public async Awaitable ProbeAllMaps(CancellationToken token = default) { if (!SyncBus.Active) await discovery.ProbeAsync(token); }
	}
}
