using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.Permissions;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.XR;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag
{
	/// <summary>Connects document workflows to scene projections, Unity lifecycle and game UI.</summary>
	[DefaultExecutionOrder(-100)]
	public class LaserTagMapCoordinator : MonoBehaviour
	{
		public static LaserTagMapCoordinator Instance { get; private set; }
		[SerializeField] private ColocationManager colocationManager;
		[SerializeField] private MapObjectDatabase objectDatabase;
		[SerializeField] private float probeTimeoutSeconds = 8f;
		[SerializeField] private float saveDebounceSeconds = 2f;
		[SerializeField] private float switchTimeoutSeconds = 20f;
		public enum AlignmentError { RequestRejected, SharingFailed, SharingUnsupported }
		public static event Action<AlignmentError> AlignmentErrorRaised = delegate { };
		public static event Action<GameMap> CurrentMapChanged = delegate { };
		public static event Action<MapSpace> CurrentSpaceChanged = delegate { };
		public static event Action WorldFrameRebased = delegate { };
		public static event Action ChangingMapChanged = delegate { };
		public static event Action ProbeResultsChanged = delegate { };
		public static event Action ColocationSettingsChanged = delegate { };

		private MapCatalog documents;
		private MapWorkingCopy mapDocument => documents.MapDocument;
		private MapSpaceWorkingCopy spaceDocument => documents.SpaceDocument;
		private MapSceneObjectDirector objects;
		private MapSpaceColocationAdapter references;
		private MapSpaceAlignmentController alignment;
		private SpaceReferenceWorkflow referenceWorkflow;
		private MapSessionWorkflow sessionWorkflow;
		private MapSessionSync session;
		private MapSpaceDiscovery discovery;
		private MapAutosave autosave;
		private readonly MapLifecycle lifecycle = new();
		private readonly MapVisitContext visit = new();
		private CancellationTokenSource lifetime;
		private bool shuttingDown;
		private Method observedSource;

		public MapPhase Phase => lifecycle.Phase;
		public GameMap CurrentMap => documents?.MapDocument.CurrentMap;
		public MapSpace CurrentSpace => documents?.SpaceDocument.CurrentSpace;
		public Guid ReferenceContext => visit.ReferenceContext;
		public Guid ScanContext => visit.ScanContext;
		public Guid CanonicalFrameId => Guid.TryParse(documents?.SpaceDocument.Frame.CanonicalId, out var id) ? id : Guid.Empty;
		public Guid SessionSpaceId => SyncBus.Active && !SyncBus.IsAuthority ? sessionWorkflow.RemoteIdentity.spaceId :
			Guid.TryParse(documents?.SpaceDocument.CurrentId, out var id) ? id : Guid.Empty;
		public bool IsChangingMap => session != null && session.IsChangingMap;
		public bool IsChangingColocation => alignment != null && alignment.Busy;
		public bool WaitingForAlignmentAuthor => referenceWorkflow?.WaitingForAlignmentAuthor == true;
		public ReferenceAlignmentTransition AlignmentTransition => alignment?.Transition;
		public AprilTagSetupSession TagSetup { get; private set; }
		public bool HasPendingSpaceAdoption => sessionWorkflow?.IsTransient == true;
		public bool HasPendingCatalogOperation => documents?.HasPending == true;
		public IReadOnlyDictionary<string, int> ProbeResults => discovery.Results;
		public MapPresence GetSpacePresence(string id) => discovery.GetPresence(id);
		public MapPresence GetMapPresence(string id) => GetSpacePresence(documents.SpaceStore.FindOwner(id)?.id);
		private bool Authority => !SyncBus.Active || SyncBus.IsAuthority;
		private bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;
		private bool CanManage => MapPolicy.CanManageMaps(Authority, lifecycle.Phase);
		private bool FrameReady => documents != null && spaceDocument.HasSpace && ColocationManager.IsColocated &&
			(!ColocationManager.UsesSavedReferences(colocationManager.ActiveMethod) || colocationManager.ReferenceAligned) &&
			(lifecycle.Phase is MapPhase.Local or MapPhase.Hosting or MapPhase.FollowingSession) &&
			(!SyncBus.Active || colocationManager.ActiveMethod == colocationManager.SelectedMethod ||
			 ColocationManager.UsesSavedReferences(colocationManager.ActiveMethod) && ColocationManager.UsesSavedReferences(colocationManager.SelectedMethod));
		public bool CanIntegrateEnvironment => FrameReady && TrackingReady() &&
			(colocationManager.ActiveMethod != Method.TwoAprilTags || spaceDocument.FirstTagId >= 0);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Instance = null;
			AlignmentErrorRaised = delegate { };
			CurrentMapChanged = delegate { };
			CurrentSpaceChanged = delegate { };
			WorldFrameRebased = delegate { };
			ChangingMapChanged = delegate { };
			ProbeResultsChanged = delegate { };
			ColocationSettingsChanged = delegate { };
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			lifetime = new();
			if (!colocationManager)
				colocationManager = FindFirstObjectByType<ColocationManager>();
			documents = new(MapStore.Default, MapSpaceStore.Default, MapStore.CatalogRoot, visit);
			autosave = new(saveDebounceSeconds, OnAutosave);
			objects = new(objectDatabase, MarkMapContentChanged, CheckCanEditMap, () => spaceDocument.Frame);
			references = new(colocationManager);
			alignment = new(colocationManager, TrackingReady);
			colocationManager.ManagedSelection = true;
			session = new();
			discovery = new(documents.SpaceStore, colocationManager.AnchorProvider, probeTimeoutSeconds, TrackingReady);
			ComposeWorkflows();
			TagSetup = new(this, referenceWorkflow.CanRegisterTags);
			referenceWorkflow.TagRegistrationAllowed = () => !TagSetup.IsActive || TagSetup.SizeConfirmed;
			referenceWorkflow.TagSetupOperation = () => TagSetup.IsActive ? TagSetup.State.operation : Guid.Empty;
			TagSetup.Register();
			objects.Register();
			references.Register();
			referenceWorkflow.Register();
			colocationManager.AnchorProvider.ErrorRaised += OnAnchorError;
			colocationManager.MethodChanged += OnSelectedMethodChanged;
			colocationManager.SourceChanged += OnAlignmentSourceChanged;
			references.Changed += ScheduleSave;
			session.AuthorityReady += OnAuthorityReady;
			session.Received += OnSessionMapReceived;
			session.ChangingMapChanged += RaiseChangingMapChanged;
			session.Register();
			discovery.ResultsChanged += RaiseProbeResultsChanged;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MapObject.LocalEditOccurred += MarkMapContentChanged;
			MapObject.Added += OnObjectChanged;
			MapObject.Removed += OnObjectChanged;
			MainXRRig.Recentered += OnRecentered;
		}

		private void ComposeWorkflows()
		{
			var token = lifetime?.Token ?? default;
			referenceWorkflow = new(documents, colocationManager, references, alignment,
				new MapSpaceTransitionSync(), visit, token, TrackingReady, () => SessionSpaceId);
			sessionWorkflow = new(documents, colocationManager, lifecycle,
				visit, token, referenceWorkflow.PreserveCurrentPrivateReferences, TrackingReady);
			referenceWorkflow.SpaceChanged += ApplySpaceConfiguration;
			referenceWorkflow.SettingsChanged += RaiseSettingsChanged;
			referenceWorkflow.FrameChanged += RaiseWorldFrameRebased;
			referenceWorkflow.DocumentsCommitted += PersistAndPublish;
			referenceWorkflow.PrivateReferencesChanged += SavePrivateReferences;
			referenceWorkflow.CaptureObjectsRequested += CaptureObjects;
			referenceWorkflow.SaveRequested += ScheduleSave;
			referenceWorkflow.TagRegistrationRequested += MapEditor.MapEditor.RequestTagRegistration;
			referenceWorkflow.RequestRejected += ReportAlignmentRejection;
			sessionWorkflow.ChangingFrame += OnIncomingFrameChanging;
			sessionWorkflow.Activated += ActivateIncoming;
			sessionWorkflow.DocumentsChanged += NotifyDocuments;
			sessionWorkflow.Restored += RestoreLocalProjection;
			sessionWorkflow.SaveRequested += ScheduleSave;
			documents.Committed += OnCatalogCommitted;
			documents.RetryRequested += ScheduleSave;
		}

		private void Start() => StartupProbe(lifetime.Token);
		private static void RaiseSettingsChanged() => ColocationSettingsChanged.Invoke();
		private static void RaiseProbeResultsChanged() => ProbeResultsChanged.Invoke();
		private static void RaiseWorldFrameRebased() => WorldFrameRebased.Invoke();
		private void RaiseChangingMapChanged() => ChangingMapChanged.Invoke();
		private void ScheduleSave() => autosave?.Schedule();

		private void OnRecentered()
		{
			alignment.Transition?.ResetEvidence();
			discovery.Invalidate();
			colocationManager.TrackingOriginChanged();
		}

		private void LateUpdate()
		{
			referenceWorkflow.CheckTransitionAuthor();
			FallbackFromUnavailableSharing();
			referenceWorkflow.Tick();
			TagSetup.Tick();
			if (lifecycle.Phase == MapPhase.SwitchingMap &&
				(objects.IsReplacementComplete || lifecycle.SwitchTimedOut(Time.unscaledTime, switchTimeoutSeconds)))
			{
				lifecycle.FinishSwitch();
				session.SetChanging(false);
			}
		}

		private void OnDestroy()
		{
			if (Instance != this)
				return;
			shuttingDown = true;
			TagSetup?.Dispose();
			SaveCurrentMap();
			lifetime.Cancel();
			lifecycle.Stop();
			referenceWorkflow.Dispose();
			alignment.Dispose();
			autosave.Dispose();
			colocationManager.AnchorProvider.ErrorRaised -= OnAnchorError;
			colocationManager.MethodChanged -= OnSelectedMethodChanged;
			colocationManager.SourceChanged -= OnAlignmentSourceChanged;
			session.AuthorityReady -= OnAuthorityReady;
			session.Received -= OnSessionMapReceived;
			session.ChangingMapChanged -= RaiseChangingMapChanged;
			session.Unregister();
			references.Changed -= ScheduleSave;
			references.Unregister();
			objects.Unregister();
			discovery.ResultsChanged -= RaiseProbeResultsChanged;
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MapObject.LocalEditOccurred -= MarkMapContentChanged;
			MapObject.Added -= OnObjectChanged;
			MapObject.Removed -= OnObjectChanged;
			MainXRRig.Recentered -= OnRecentered;
			lifetime.Dispose();
			Instance = null;
		}

		private void OnApplicationQuit()
		{
			shuttingDown = true;
			SaveCurrentMap();
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
				SaveCurrentMap();
		}

		private bool TrackingReady() => Application.isFocused && PlayerHeadsetStatus.HeadTrackingReady;
		public bool CheckWorldFrameIsTrusted() => FrameReady;
		public bool CheckReferenceFrameAgreement() => documents != null && spaceDocument.HasSpace && TrackingReady() && colocationManager.ReferenceAligned;
		public bool CheckCanEditMap() => MapPolicy.CanEditMap(IsChangingMap, FrameReady,
			HeadsetConfiguration.IsOperatorDevice && Authority && documents != null && spaceDocument.HasSpace);
		private CapabilitySupport SharedAnchorSupport
		{
			get
			{
				var capability = MetaPermissionChecks.CheckSharedSpatialAnchors();
				if (capability.vps == VpsStatus.Disabled || AnchorRegistry.Instance?.SharingDenied == true)
					return CapabilitySupport.Unsupported;
				return capability.support;
			}
		}

		private static string Localize(string key) => key == null ? null : MenuCopy.Get("Map", key);

		public string DescribeChangeBlocker(string id)
		{
			var owner = documents.SpaceStore.FindOwner(id);
			var blocker = MapPolicy.ChangeMapBlocker(CanManage, RoundInProgress, mapDocument.CurrentId == id,
				documents.MapStore.TryGet(id, out _) && owner != null);
			if (blocker != null)
				return Localize(blocker);
			return owner.id != spaceDocument.CurrentId ? DescribeSpaceChangeBlocker(owner.id) : null;
		}

		public bool ChangeMap(string id) => ReplaceMap(id);
		public bool LoadMap(string id) => !SyncBus.Active && ReplaceMap(id);
		public bool SwitchMap(string id) => ReplaceMap(id);

		private bool ReplaceMap(string id)
		{
			if (DescribeChangeBlocker(id) != null || !SaveCurrentMap() || !documents.MapStore.TryGet(id, out var target))
				return false;

			var owner = documents.SpaceStore.FindOwner(id);
			if (owner.id != spaceDocument.CurrentId)
				return ChangeSpace(owner.id, id);
			ActivateMap(target);
			return SaveCurrentMap();
		}

		private void ActivateMap(GameMap map, bool notify = true)
		{
			BeginMapChange();
			mapDocument.Load(map);
			mapDocument.MarkUsed();
			SetObjectContext();
			objects.Replace(map.objects);
			if (notify)
				NotifyMap();
		}

		private void BeginMapChange()
		{
			visit.MapContext = Guid.NewGuid();
			if (SyncBus.Active && SyncBus.IsAuthority)
			{
				lifecycle.BeginSwitch(Time.unscaledTime);
				session.SetChanging(true);
			}
		}

		private void SetObjectContext() => objects?.SetContext(mapDocument.CurrentId, visit.MapContext);

		private void NotifyMap()
		{
			SetObjectContext();
			CurrentMapChanged.Invoke(CurrentMap);
		}

		private void ApplySpaceConfiguration()
		{
			var space = CurrentSpace;
			if (colocationManager.TagProvider)
				colocationManager.TagProvider.ReferenceContext = visit.ReferenceContext;
			colocationManager.ConfigureSpace(space, referenceWorkflow.CanMintAnchors);
			CurrentSpaceChanged.Invoke(space);
			RaiseSettingsChanged();
		}

		private void NotifyDocuments()
		{
			ApplySpaceConfiguration();
			NotifyMap();
		}

		public string DescribeNewMapBlocker() => Localize(MapPolicy.NewMapBlocker(CanManage, RoundInProgress, spaceDocument.HasSpace));
		public CatalogOperationResult NewMap() => DescribeNewMapBlocker() == null && SaveCurrentMap()
			? documents.NewMap() : new(CatalogOperationStatus.Rejected);
		public CatalogOperationResult DuplicateMap(string id) => DescribeNewMapBlocker() == null && SaveCurrentMap()
			? documents.DuplicateMap(id) : new(CatalogOperationStatus.Rejected);
		public bool UnloadCurrentMap()
		{
			if (SyncBus.Active || !SaveCurrentMap())
				return false;
			ClearMapProjection();
			return true;
		}

		private void ClearMapProjection()
		{
			mapDocument.Unload();
			visit.MapContext = Guid.NewGuid();
			SetObjectContext();
			objects.Replace(Array.Empty<MapObjectEntry>());
			NotifyMap();
		}

		public string DescribeDeleteBlocker(string id) => Localize(MapPolicy.DeleteMapBlocker(id, mapDocument.CurrentId, SyncBus.Active));
		public CatalogOperationResult DeleteMap(string id) => DescribeDeleteBlocker(id) == null && SaveCurrentMap()
			? documents.DeleteMap(id) : new(CatalogOperationStatus.Rejected);
		public string DescribeSpaceChangeBlocker(string id) => Localize(MapPolicy.SpaceChangeBlocker(CanManage,
			RoundInProgress, IsChangingColocation, HeadsetConfiguration.IsOperatorDevice, GetSpacePresence(id)));
		public bool ChangeSpace(string id) => ChangeSpace(id, null);
		private bool ChangeSpace(string id, string mapId)
		{
			if (DescribeSpaceChangeBlocker(id) != null || !documents.SpaceStore.TryGet(id, out var target) || !SaveCurrentMap())
				return false;
			if (id == spaceDocument.CurrentId)
				return mapId == null || ReplaceMap(mapId);

			var map = documents.SelectMap(target, mapId);
			if (map == null)
				return documents.ActivateSpaceWithNewMap(target).IsCompleted;
			ActivateSpace(target, map);
			return SaveCurrentMap();
		}

		public CatalogOperationResult NewSpace() => HeadsetConfiguration.IsOperatorDevice && CanManage && !RoundInProgress && SaveCurrentMap()
			? documents.CreateSpace(false, Method.MetaSharedAnchor) : new(CatalogOperationStatus.Rejected);
		private void ActivateSpace(MapSpace space, GameMap map)
		{
			referenceWorkflow.CancelTransitionInternal();
			sessionWorkflow.ResetForLocalSpace();
			BeginMapChange();
			visit.ReferenceContext = Guid.NewGuid();
			visit.ScanContext = Guid.NewGuid();
			spaceDocument.Load(space);
			spaceDocument.MarkUsed();
			mapDocument.Load(map);
			mapDocument.MarkUsed();
			colocationManager.InvalidateAlignment();
			references.Inject(space);
			references.ClearPendingSnapshots();
			ApplySpaceConfiguration();
			alignment.Activate(space, colocationManager.PreferredAvailableMethod);
			if (SyncBus.Active && SyncBus.IsAuthority)
				colocationManager.CommitMethod(colocationManager.PreferredAvailableMethod);
			colocationManager.AnchorProvider?.InvalidateReferenceContext();
			SetObjectContext();
			objects.Replace(map?.objects ?? new());
			WorldFrameRebased.Invoke();
			NotifyMap();
		}

		public string DescribeDeleteSpaceBlocker(string id) => Localize(MapPolicy.DeleteSpaceBlocker(CanManage,
			RoundInProgress, IsChangingColocation, SyncBus.Active && spaceDocument.CurrentId == id));
		public CatalogOperationResult DeleteSpace(string id) => DescribeDeleteSpaceBlocker(id) == null && SaveCurrentMap()
			? documents.DeleteSpace(id) : new(CatalogOperationStatus.Rejected);
		private void OnCatalogCommitted(CatalogChange change)
		{
			sessionWorkflow.ApplyCatalogChange(change);
			if (change.Kind == CatalogChangeKind.SpaceDeleted)
				discovery?.Forget(change.Space.id);
			if (change.Kind is CatalogChangeKind.MapDeleted or CatalogChangeKind.SpaceDeleted)
			{
				ApplyCatalogDeletion(change);
				return;
			}
			if (!CanManage || change.MapContext != visit.MapContext || change.ReferenceContext != visit.ReferenceContext)
			{
				PreserveCommittedMembership(change);
				return;
			}
			switch (change.Kind)
			{
				case CatalogChangeKind.Membership:
					spaceDocument.Load(change.Space);
					ApplySpaceConfiguration();
					break;
				case CatalogChangeKind.MapCreated:
					spaceDocument.Load(change.Space);
					ActivateMap(change.Map, notify: false);
					ApplySpaceConfiguration();
					NotifyMap();
					SaveCurrentMap();
					break;
				case CatalogChangeKind.SpaceActivated:
					ActivateSpace(change.Space, change.Map);
					SaveCurrentMap();
					break;
			}
		}

		private void ApplyCatalogDeletion(CatalogChange change)
		{
			if (sessionWorkflow.UsesSessionDocuments)
				return;
			if (change.Kind == CatalogChangeKind.MapDeleted)
			{
				if (mapDocument.CurrentId == change.DeletedMapId)
					ClearMapProjection();
				PreserveCommittedMembership(change);
				return;
			}
			if (spaceDocument.CurrentId == change.Space.id)
				ClearSpaceProjection();
			SaveCurrentMap();
			if (!spaceDocument.HasSpace && CanManage)
				StartupProbe(lifetime?.Token ?? default);
		}

		private void PreserveCommittedMembership(CatalogChange change)
		{
			var current = CurrentSpace;
			if (change.Kind == CatalogChangeKind.SpaceDeleted || current == null ||
				current.id != change.Space.id || current.storageFrameId != change.Space.storageFrameId)
				return;
			current.mapIds = new(change.Space.mapIds);
			spaceDocument.ApplySnapshot(current);
			ApplySpaceConfiguration();
		}

		private void ClearSpaceProjection()
		{
			referenceWorkflow.CancelTransitionInternal();
			mapDocument.Unload();
			spaceDocument.Unload();
			references.ClearForNoMap();
			colocationManager.ClearSpace();
			visit.MapContext = Guid.NewGuid();
			visit.ReferenceContext = Guid.NewGuid();
			visit.ScanContext = Guid.NewGuid();
			SetObjectContext();
			objects.Replace(Array.Empty<MapObjectEntry>());
			WorldFrameRebased.Invoke();
			NotifyDocuments();
		}

		public bool ResetSpaceAlignment()
		{
			if (SyncBus.Active || !spaceDocument.HasSpace || IsChangingColocation || !SaveCurrentMap())
				return false;
			var space = CurrentSpace;
			foreach (var anchor in space.AllAnchors())
				documents.QueueAnchorErasure(anchor.guid);
			space.tags.Clear();
			space.anchors.Clear();
			space.localAnchors.Clear();
			space.retainedReferences.Clear();
			space.associations.Clear();
			space.knownFrames.Clear();
			space.previousCanonicalFrameId = null;
			space.canonicalFrameId = Guid.NewGuid().ToString("N");
			space.frameRevision++;
			space.firstTagId = -1;
			space.secondTagId = -1;
			space.initializationPending = true;
			space.hasPendingSetup = false;
			space.referenceSourceId = space.id;
			space.referenceVersion = Guid.NewGuid().ToString("N");
			space.preferredColocationMethod = Method.MetaSharedAnchor;
			space.MarkChanged();
			if (!documents.SpaceStore.Save(space))
				return false;
			ActivateSpace(space, CurrentMap);
			return SaveCurrentMap();
		}

		public bool UndoSpaceRebase()
		{
			if (SyncBus.Active || !spaceDocument.HasSpace || !SaveCurrentMap())
				return false;
			var restored = MapSpaceReconciler.Undo(CurrentSpace);
			if (!documents.SpaceStore.Save(restored))
				return false;
			ActivateSpace(restored, CurrentMap);
			return true;
		}

		public bool RenameSpace(string name)
		{
			if (!CanManage || string.IsNullOrWhiteSpace(name))
				return false;
			spaceDocument.Rename(TrimName(name));
			ApplySpaceConfiguration();
			return SaveCurrentMap();
		}

		public const int MaxMapNameLength = 40;
		private static string TrimName(string name)
		{
			var trimmed = name.Trim();
			return trimmed.Substring(0, Mathf.Min(MaxMapNameLength, trimmed.Length));
		}

		public string DescribeRenameBlocker() => Localize(MapPolicy.RenameBlocker(Authority));
		public bool RenameMap(string name)
		{
			if (DescribeRenameBlocker() != null || string.IsNullOrWhiteSpace(name) || !mapDocument.HasMap)
				return false;
			mapDocument.Rename(TrimName(name));
			NotifyMap();
			return SaveCurrentMap();
		}

		public bool RequestPlaceObject(MapObject prefab, Vector3 position, Quaternion rotation) =>
			CheckCanEditMap() && mapDocument.HasMap && objects.RequestPlace(prefab, position, rotation);
		public bool RequestRemoveObject(MapObject obj) => CheckCanEditMap() && objects.RequestRemove(obj);

		public void CommitObjectMove(MapObject obj)
		{
			if (CheckCanEditMap())
				objects.RequestMove(obj);
		}

		private void OnObjectChanged(MapObject _) => ScheduleSave();
		private void MarkMapContentChanged()
		{
			if (!Authority || !mapDocument.HasMap)
				return;
			CaptureObjects();
			ScheduleSave();
		}

		private void CaptureObjects()
		{
			if (MapPolicy.CanCaptureScene(Authority, lifecycle.Phase) && mapDocument.HasMap && objects.TryCapture(out var snapshot))
				mapDocument.SetObjects(snapshot);
		}

		public bool SaveCurrentMap()
		{
			if (documents == null)
				return true;
			if (documents.HasPending)
				return false;
			CaptureObjects();
			if (sessionWorkflow.IsTransient)
				return sessionWorkflow.SaveTransientSession();
			if (!documents.SaveDocuments(SyncBus.Active && SyncBus.IsAuthority))
			{
				if (!shuttingDown)
					ScheduleSave();
				return false;
			}
			PublishCurrentDocuments();
			documents.EraseUnreferencedAnchors(references.EraseAnchorSave);
			return true;
		}

		private void PublishCurrentDocuments() => session.Publish(CurrentMap, CurrentSpace, colocationManager.SelectedMethod,
			visit.MapContext, visit.ReferenceContext, visit.ScanContext);
		private void PersistAndPublish() => SaveCurrentMap();
		private void SavePrivateReferences()
		{
			if (sessionWorkflow.IsTransient)
				sessionWorkflow.SaveTransientSession();
			else if (!spaceDocument.Save())
				ScheduleSave();
		}

		private void OnAutosave()
		{
			if (!documents.RetryPending())
			{
				ScheduleSave();
				return;
			}
			sessionWorkflow.RetryAdoption();
			if (lifecycle.Phase != MapPhase.Stopped)
				SaveCurrentMap();
		}

		public void CancelAlignmentTransition() => referenceWorkflow.CancelAlignmentTransition();
		public string DescribeColocationPreferenceBlocker() => Localize(referenceWorkflow.DescribeColocationPreferenceBlocker());
		public string DescribeColocationMethodBlocker(Method method) => Localize(referenceWorkflow.DescribeColocationMethodBlocker(method));
		public bool SetPreferredColocationMethod(Method method) => referenceWorkflow.SetPreferredColocationMethod(method);
		public bool SessionIsWaitingOnFirstTag => referenceWorkflow?.SessionIsWaitingOnFirstTag == true;
		public string DescribeTagSetupBlocker() => DescribeTagRegistrationBlocker();
		public string DescribeTagRegistrationBlocker() => TagSetup?.IsActive == true && !TagSetup.SizeConfirmed
			? Localize("tag-setup.measure-first") : Localize(referenceWorkflow.DescribeTagRegistrationBlocker());
		public string DescribeTagRemovalBlocker() => Localize(referenceWorkflow.DescribeTagRemovalBlocker());
		public string DescribeTagSizeBlocker() => Localize(referenceWorkflow.DescribeTagSizeBlocker());
		public float EffectiveTagSizeCm => referenceWorkflow?.EffectiveTagSizeCm ?? MapSpace.DefaultTagSizeCm;
		public bool RegisterTag(int id, Pose worldPose) => DescribeTagRegistrationBlocker() == null && referenceWorkflow.RegisterTag(id, worldPose);
		public bool UnregisterTag(int id) => referenceWorkflow.UnregisterTag(id);
		public void UnregisterAllTags() => referenceWorkflow.UnregisterAllTags();
		public bool SetTagSize(float value) => referenceWorkflow.SetTagSize(value);
		public void ChooseAnotherTagPair() => referenceWorkflow.ChooseAnotherTagPair();
		private static void ReportAlignmentRejection() => AlignmentErrorRaised.Invoke(AlignmentError.RequestRejected);
		private static void OnAnchorError(SpatialAnchorColocationConstraintProvider.Error error) =>
			AlignmentErrorRaised.Invoke(error == SpatialAnchorColocationConstraintProvider.Error.SharingFailed
				? AlignmentError.SharingFailed : AlignmentError.SharingUnsupported);
		private void OnAlignmentSourceChanged()
		{
			var activeMethod = colocationManager.ActiveMethod;
			if (activeMethod != observedSource &&
				(!ColocationManager.UsesSavedReferences(activeMethod) || !ColocationManager.UsesSavedReferences(observedSource)))
				WorldFrameRebased.Invoke();
			observedSource = activeMethod;
			RaiseSettingsChanged();
		}

		private void OnSelectedMethodChanged()
		{
			if (spaceDocument.HasSpace && SyncBus.Active && SyncBus.IsAuthority)
				SaveCurrentMap();
			RaiseSettingsChanged();
		}

		private void OnBusActivated()
		{
			if (!SyncBus.IsAuthority && lifecycle.Phase == MapPhase.Local && mapDocument.HasMap)
				mapDocument.SetObjects(objects.CaptureLocal());
			CaptureObjects();
			SaveCurrentMap();
			sessionWorkflow.BeginSession();
			discovery.Invalidate();
		}

		private void OnAuthorityReady()
		{
			lifecycle.BeginHosting(false, Time.unscaledTime);
			if (!spaceDocument.HasSpace)
			{
				var lastSpace = documents.SpaceStore.Spaces.FirstOrDefault();
				if (HeadsetConfiguration.IsOperatorDevice && lastSpace != null)
					ChangeSpace(lastSpace.id);
				else if (!HeadsetConfiguration.IsOperatorDevice)
					StartupProbe(lifetime.Token);
			}
			if (!mapDocument.HasMap && spaceDocument.HasSpace)
				NewMap();
			if (spaceDocument.HasSpace)
			{
				ApplySpaceConfiguration();
				SaveCurrentMap();
				objects.SpawnLocalObjects();
			}
		}

		private void OnAuthorityChanged(bool authority)
		{
			referenceWorkflow.CancelTransitionInternal();
			sessionWorkflow.InvalidateIncoming();
			if (authority)
			{
				visit.ReferenceContext = Guid.NewGuid();
				visit.ScanContext = Guid.NewGuid();
				WorldFrameRebased.Invoke();
				referenceWorkflow.ClearPublishedTransition();
				OnAuthorityReady();
			}
			else
				lifecycle.EnterSession(false);
		}

		private void OnSessionMapReceived(MapIdentity identity, List<MapObjectEntry> placements, MapSpace remote) =>
			sessionWorkflow.Receive(identity, placements, remote);
		private void OnIncomingFrameChanging()
		{
			referenceWorkflow.CancelTransitionInternal();
			objects.RemoveLocalObjects();
		}

		private void ActivateIncoming(MapIdentity identity, bool newFrame, bool scanChanged, bool referencesChanged)
		{
			var space = CurrentSpace;
			references.Inject(referenceWorkflow.Candidate ?? space);
			references.ClearPendingSnapshots();
			NotifyDocuments();
			if (newFrame)
			{
				colocationManager.InvalidateAlignment();
				alignment.Activate(space, identity.method);
			}
			else if (!alignment.Busy && colocationManager.ActiveMethod != identity.method)
			{
				if (ColocationManager.UsesSavedReferences(identity.method))
				{
					alignment.Begin(Guid.NewGuid(), space, identity.method, ReferenceTransitionIntent.ActivateTarget, false);
					alignment.SetCandidate(identity.spaceVersion, space);
					alignment.Commit();
				}
				else
					alignment.Activate(space, identity.method);
			}
			else if (!alignment.Busy && referencesChanged)
				alignment.Activate(space, colocationManager.ActiveMethod, true);
			if (scanChanged && colocationManager.ActiveMethod == Method.TwoAprilTags)
			{
				colocationManager.TwoTagProvider.ResetReferences();
				colocationManager.InvalidateAlignment();
				alignment.Activate(space, Method.TwoAprilTags);
			}
			if (newFrame || scanChanged)
				WorldFrameRebased.Invoke();
		}

		private void OnBusDeactivated()
		{
			referenceWorkflow.CancelTransitionInternal();
			sessionWorkflow.RestoreAfterSession();
		}

		private void RestoreLocalProjection(bool preserveFrame)
		{
			var space = CurrentSpace;
			var map = CurrentMap;
			if (space != null && preserveFrame)
			{
				visit.MapContext = Guid.NewGuid();
				visit.ReferenceContext = Guid.NewGuid();
				references.Inject(space);
				references.ClearPendingSnapshots();
				ApplySpaceConfiguration();
				colocationManager.CommitMethod(colocationManager.ActiveMethod);
				alignment.Activate(space, colocationManager.ActiveMethod, true);
				SetObjectContext();
				objects.Replace(map?.objects ?? new());
				NotifyMap();
			}
			else if (space != null)
				ActivateSpace(space, map);
			else
			{
				references.ClearForNoMap();
				colocationManager.ClearSpace();
				objects.Replace(Array.Empty<MapObjectEntry>());
				NotifyDocuments();
				StartupProbe(lifetime.Token);
			}
		}

		// Sharing support and room discovery are independent. Local anchors may work on an MDM headset.
		private void FallbackFromUnavailableSharing()
		{
			if (!Authority || HeadsetConfiguration.IsOperatorDevice || !TrackingReady() || documents.HasPending || RoundInProgress)
				return;
			if (alignment.Busy && referenceWorkflow.TargetMethod != Method.MetaSharedAnchor)
				return;
			var space = CurrentSpace;
			if (space?.preferredColocationMethod != Method.MetaSharedAnchor || SharedAnchorSupport != CapabilitySupport.Unsupported ||
				!MapSpaceStartup.ConfigureDraft(space, Method.AprilTag))
				return;
			if (!documents.SpaceStore.Save(space))
			{
				ScheduleSave();
				return;
			}
			referenceWorkflow.CancelTransitionInternal();
			spaceDocument.Load(space);
			references.ClearPendingSnapshots();
			references.Inject(space);
			ApplySpaceConfiguration();
			alignment.Activate(space, Method.AprilTag);
			colocationManager.CommitMethod(Method.AprilTag);
			SaveCurrentMap();
		}

		// A healthy completed probe may create one reusable default draft. Failures never mean an empty room.
		private async void StartupProbe(CancellationToken token)
		{
			if (HeadsetConfiguration.IsOperatorDevice)
			{
				if (CurrentSpace == null && !SyncBus.Active)
				{
					var lastSpace = documents.SpaceStore.Spaces.FirstOrDefault();
					if (lastSpace != null)
						ChangeSpace(lastSpace.id);
				}
				return;
			}
#if UNITY_EDITOR
			if (MainXRRig.Instance && !XRSettings.enabled)
			{
				RestoreLastMapForSimulation();
				return;
			}
#endif
			try
			{
				while (!token.IsCancellationRequested && Authority && CurrentSpace == null)
				{
					await ProbeAndAutoLoad(token);
					if (CurrentSpace == null)
						await Awaitable.WaitForSecondsAsync(2f, token);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

#if UNITY_EDITOR
		public bool RestoreLastMapForSimulation()
		{
			if (!MainXRRig.Instance || XRSettings.enabled || SyncBus.Active || CurrentSpace != null)
				return true;
			var lastSpace = documents.SpaceStore.Spaces.FirstOrDefault();
			// Restoring a space must restore its references and chosen provider together.
			// Only a brand-new simulation space starts with the system origin.
			return lastSpace != null
				? ChangeSpace(lastSpace.id) : documents.CreateSpace(false, Method.SystemDetermined).IsCompleted;
		}
#endif

		public async Awaitable ProbeAndAutoLoad(CancellationToken token = default)
		{
			if (!Authority || CurrentSpace != null || documents.HasPending || !TrackingReady())
				return;
			int sessionGeneration = sessionWorkflow.Generation;
			int trackingGeneration = colocationManager.TrackingGeneration;
			var result = await discovery.ProbeAsync(token);
			if (token.IsCancellationRequested || !Authority || CurrentSpace != null ||
				sessionGeneration != sessionWorkflow.Generation || trackingGeneration != colocationManager.TrackingGeneration)
				return;
			if (result.outcome == SpaceProbeOutcome.Matches)
				ChangeSpace(result.best.id);
			else if (MapSpaceStartup.InitialMethod(result.outcome, SharedAnchorSupport, discovery.IsAvailable) is Method initialMethod)
			{
				if (!documents.SpaceStore.IsAvailable)
					return;
				var draft = documents.SpaceStore.Spaces.FirstOrDefault(space =>
					space.automaticallyCreated && space.initializationPending && !space.HasReferenceBasedData);
				if (draft != null)
				{
					if (draft.preferredColocationMethod == Method.MetaSharedAnchor)
						MapSpaceStartup.ConfigureDraft(draft, initialMethod);
					if (documents.SpaceStore.Save(draft))
						ChangeSpace(draft.id);
				}
				else
					documents.CreateSpace(true, initialMethod);
			}
		}

		public async Awaitable ProbeAllMaps(CancellationToken token = default)
		{
			if (!SyncBus.Active)
				await discovery.ProbeAsync(token);
		}
	}
}
