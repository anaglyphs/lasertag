using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Project composition and policy: connects the map document, object runtime, colocation,
	/// session transport and editor. The MapManager itself has none of these dependencies.
	///
	/// MapWorkflow tracks which transition is in progress; MapPolicy decides what is allowed.
	/// This class performs the allowed actions in order: capture runtime changes, save the
	/// document, publish it when hosting, then request cleanup of unused anchor saves.
	/// Shared map content and this headset's anchor records are captured separately: a
	/// follower may update its own anchors without authoring the session's object placements.
	/// </summary>
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
		public static event Action WorldFrameRebased = delegate { };
		public static event Action ChangingMapChanged = delegate { };
		public static event Action ProbeResultsChanged = delegate { };
		public static event Action ColocationSettingsChanged = delegate { };

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Instance = null;
			CurrentMapChanged = delegate { };
			WorldFrameRebased = delegate { };
			ChangingMapChanged = delegate { };
			ProbeResultsChanged = delegate { };
			ColocationSettingsChanged = delegate { };
		}

		private MapManager maps;
		private MapObjectDirector objects;
		private MapColocationAdapter colocation;
		private MapSessionSync session;
		private MapDiscovery discovery;
		private MapAutosave autosave;
		private CancellationTokenSource lifetime;
		// Candidates only: dropping a reference in memory does not yet make its saved anchor safe to erase.
		private readonly HashSet<string> anchorsToErase = new();
		private readonly MapWorkflow workflow = new();
		private int frameRebasedOn = -1;
		private string registrationRequestedFor;

		private struct MethodRequest
		{
			// Empty for an automatic retry of the available map preference; nonempty for a user's request.
			public Guid requestId;
			public Guid mapId;
			public ColocationManager.ColocationMethod method;
		}

		private struct MethodRejection
		{
			public ulong requester;
			public Guid requestId;
			public Guid mapId;
			public FixedString128Bytes reason;
		}

		private readonly SyncEvent<MethodRequest> methodRequest = new("map.colocation.request", EventRoute.ToAuthority);
		private readonly SyncEvent<MethodRejection> methodRejection = new("map.colocation.rejected", EventRoute.ViaAuthority);
		private readonly SyncVariable<bool> preparingMethod = new("map.colocation.preparing");
		private CancellationTokenSource methodPreparation;
		private double methodPreparationDeadline;
		private ulong methodRequester;
		private MethodRequest methodPreparationRequest;
		private Guid latestMethodRequest;
		// Defer automatic method changes until LateUpdate, after map/provider callbacks have settled.
		private bool applyMapPreference;
		private ulong preferenceRequester;
		public bool IsChangingColocation => preparingMethod.Value;

		public MapPhase Phase => workflow.Phase;
		/// <summary>A detached copy. Change the document through MapManager, not by mutating this result.</summary>
		public GameMap CurrentMap => maps?.CurrentMap;
		public bool IsChangingMap => session != null && session.IsChangingMap;
		public IReadOnlyDictionary<string, int> ProbeResults => discovery.Results;
		public MapPresence GetMapPresence(string id) => discovery.GetPresence(id);

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			lifetime = new CancellationTokenSource();
			if (!colocationManager)
				colocationManager = FindFirstObjectByType<ColocationManager>();
			maps = new MapManager(MapStore.Default);
			autosave = new MapAutosave(saveDebounceSeconds, OnAutosave);
			objects = new MapObjectDirector(objectDatabase, MarkMapContentChanged, CheckCanEditMap);
			colocation = new MapColocationAdapter(colocationManager);
			session = new MapSessionSync();
			discovery = new MapDiscovery(MapStore.Default, colocationManager ? colocationManager.AnchorProvider : null, probeTimeoutSeconds);

			ConfigureAnchorMinter();
			objects.Register();
			colocation.Register();
			colocationManager.MethodChanged += OnColocationMethodChanged;
			if (colocationManager.TagProvider)
				colocationManager.TagProvider.RegistrationGate = ValidateTagRegistration;
			methodRequest.Register();
			methodRequest.Received += OnMethodRequested;
			methodRejection.Register();
			methodRejection.Validate = (sender, _) => sender == SyncBus.LocalClientId;
			methodRejection.Received += OnMethodRejected;
			preparingMethod.Register();
			preparingMethod.Validate = (_, _) => false;
			preparingMethod.Changed += OnPreparingMethodChanged;
			colocation.Changed += autosave.Schedule;
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
		}

		private void Start() => StartupProbe(lifetime.Token);
		private void RaiseChangingMapChanged()
		{
			if (IsChangingMap && (workflow.Phase is MapPhase.FollowingSession or MapPhase.AdoptingSessionMap))
				workflow.AwaitSessionMap();

			ChangingMapChanged.Invoke();
			if (!IsChangingMap)
				RequestTagRegistrationIfNeeded();
		}

		private static void RaiseProbeResultsChanged() => ProbeResultsChanged.Invoke();

		private void OnDestroy()
		{
			if (Instance != this)
				return;
			maps.Save(); // Preserve the last document; teardown is not an authored deletion.
			CancelMethodPreparation();
			ClearAnchorMinter();
			colocationManager.MethodChanged -= OnColocationMethodChanged;
			if (colocationManager.TagProvider) colocationManager.TagProvider.RegistrationGate = null;
			preparingMethod.Changed -= OnPreparingMethodChanged;
			preparingMethod.Unregister();
			methodRequest.Received -= OnMethodRequested;
			methodRequest.Unregister();
			methodRejection.Received -= OnMethodRejected;
			methodRejection.Unregister();
			workflow.Stop();
			Instance = null;
			lifetime.Cancel();
			lifetime.Dispose();
			autosave.Dispose();
			MapObject.LocalEditOccurred -= MarkMapContentChanged;
			MapObject.Added -= OnObjectChanged;
			MapObject.Removed -= OnObjectChanged;
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			discovery.ResultsChanged -= RaiseProbeResultsChanged;
			session.AuthorityReady -= OnAuthorityReady;
			session.Received -= OnSessionMapReceived;
			session.ChangingMapChanged -= RaiseChangingMapChanged;
			session.Unregister();
			colocation.Changed -= autosave.Schedule;
			colocation.Unregister();
			objects.Unregister();
		}

		private void OnApplicationQuit()
		{
			if (Instance != this)
				return;
			SaveCurrentMap();
			workflow.Stop();
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused && Instance == this)
				SaveCurrentMap();
		}

		private void LateUpdate()
		{
			// Native uploads cannot be interrupted. Release the session hold at the deadline;
			// cancellation and the operation identity reject any eventual native completion.
			if (methodPreparation != null && Time.realtimeSinceStartupAsDouble >= methodPreparationDeadline)
			{
				CancelMethodPreparation();
				if (SyncBus.Active && SyncBus.IsAuthority)
				{
					preparingMethod.Value = false;
					RejectMethod(methodRequester, MenuCopy.Get("Game", "alignment.timeout"), methodPreparationRequest);
				}
			}
			ApplyReferenceChanges();
			if (applyMapPreference && workflow.Phase == MapPhase.Hosting && !IsChangingColocation && !RoundInProgress)
			{
				applyMapPreference = false;
				ApplyPreferredMethod(preferenceRequester, new MethodRequest {
					mapId = Guid.Parse(CurrentMap.id), method = colocationManager.PreferredSessionMethod
				});
			}
			if (workflow.Phase != MapPhase.SwitchingMap)
				return;

			if (objects.IsReplacementComplete && (HeadsetConfiguration.IsOperatorDevice || CheckFrameAgreement()))
			{
				FinishMapChange();
				return;
			}

			if (workflow.SwitchTimedOut(Time.unscaledTime, switchTimeoutSeconds))
			{
				Debug.LogWarning("Map change timed out. Editing still requires local alignment.");
				FinishMapChange();
			}
		}

		private void FinishMapChange()
		{
			workflow.FinishSwitch();
			session.SetChanging(false);
		}

		/// <summary>
		/// Reconfigures runtime consumers before announcing the map. During a map switch this
		/// also commits the compatible session method; otherwise a changed available method
		/// schedules a separate live handoff. This is more than an event notification.
		/// </summary>
		private void NotifyMapChanged()
		{
			GameMap map = CurrentMap;
			objects.SetMapId(map?.id);
			var previousAvailableMethod = colocationManager != null ? colocationManager.PreferredSessionMethod : default;
			colocationManager?.ConfigureMap(map != null, map != null && map.HasTags,
				map != null && !map.IsEmpty, map != null && map.anchors.Count > 0,
				map != null ? map.preferredColocationMethod : default, CheckWorldFrameIsTrusted,
				map != null && map.systemFrameForTagSetup);
			if (SyncBus.Active && SyncBus.IsAuthority && map != null && colocationManager != null)
			{
				if (workflow.Phase == MapPhase.SwitchingMap)
					colocationManager.CommitMethod(colocationManager.PreferredSessionMethod);
				else if (previousAvailableMethod != colocationManager.PreferredSessionMethod)
				{
					// Retry when references make another method available, not on every pose correction.
					applyMapPreference = true;
					preferenceRequester = SyncBus.LocalClientId;
				}
			}
			CurrentMapChanged.Invoke(map);
		}

		// The canonical map frame changed. Invalidate this frame's alignment result and tell
		// consumers to discard state based on the previous frame; this does not itself move objects.
		private void Rebase()
		{
			frameRebasedOn = Time.frameCount;
			colocationManager?.AnchorProvider?.InvalidateReferenceContext();
			WorldFrameRebased.Invoke();
		}

		private bool SessionUsesTags => colocationManager != null &&
			colocationManager.Method == ColocationManager.ColocationMethod.AprilTag;

		private MapPolicy Policy => new(
			phase: workflow.Phase,
			hasMap: maps.HasMap,
			empty: maps.IsEmpty,
			hasTags: maps.HasTags,
			frameAgrees: CheckFrameAgreement(),
			sessionHolding: IsChangingMap || IsChangingColocation,
			roundInProgress: RoundInProgress,
			sessionUsesTags: SessionUsesTags,
			operatorManagedSession: HeadsetConfiguration.SessionIsOperatorManaged,
			hasAnchors: maps.AnchorCount > 0,
			twoTagFrame: colocationManager != null &&
				colocationManager.SelectedMethod == ColocationManager.ColocationMethod.TwoAprilTags,
			systemDeterminedFrame: colocationManager != null &&
				colocationManager.SelectedMethod == ColocationManager.ColocationMethod.SystemDetermined);

		public bool CheckWorldFrameIsTrusted() => Policy.FrameIsTrusted;
		public bool CheckCanEditMap() => Policy.EditBlocker == null;

		/// <summary>
		/// Checks local alignment, independently of workflow permissions. With no realizable
		/// references, the bootstrap policy may allow work.
		/// Otherwise require the solver's mean fit and up to two agreeing references.
		/// </summary>
		public bool CheckReferenceFrameAgreement() => CheckFrameAgreement();

		private bool CheckFrameAgreement()
		{
			if (maps == null || !maps.HasMap || Time.frameCount == frameRebasedOn || !colocationManager)
				return false;
			int realizable = colocationManager.CountRealizableReferences();
			// Registered tags define a frame even before this headset has realized an anchor.
			if (colocationManager.UsingTagProvider && colocationManager.ActiveProvider.IsAvailable &&
			    maps.HasTags && !ColocationManager.IsColocated)
				return false;
			if (colocationManager.UsingAnchorProvider && realizable == 0 && SyncBus.Active)
				return maps.IsEmpty && !maps.HasTags && maps.AnchorCount == 0 &&
					colocationManager.AnchorProvider.IsLocalMinter;
			if (realizable == 0)
				return MapPolicy.CanBootstrapFrame(maps.HasTags, maps.AnchorCount, maps.IsEmpty, SyncBus.Active, SyncBus.IsAuthority);
			FitAgreement agreement = colocationManager.Agreement;
			return ColocationManager.IsColocated && agreement.agreeingCount >= Mathf.Min(realizable, 2) && agreement.meanAgrees;
		}

		public string DescribeChangeBlocker(string id)
		{
			if (IsChangingColocation) return MenuCopy.Get("Game", "blocker.preparing-the-colocation-method");
			MapStore.Default.TryGet(id, out GameMap target);
			return MenuCopy.Get("Game", Policy.ChangeMapBlocker(target, maps.CurrentId == id, GetMapPresence(id)));
		}

		private static bool RoundInProgress => MatchReferee.State == MatchState.Playing || MatchReferee.State == MatchState.Countdown;
		public bool ChangeMap(string id) => ReplaceMap(id);
		public bool LoadMap(string id) => workflow.Phase == MapPhase.Local && ReplaceMap(id);
		public bool SwitchMap(string id) => ReplaceMap(id);

		private bool ReplaceMap(string id)
		{
			string blocker = DescribeChangeBlocker(id);
			if (blocker != null)
			{
				Debug.LogWarning($"Cannot change map: {blocker}.");
				return false;
			}
			if (!MapStore.Default.TryGet(id, out GameMap target) || !SaveCurrentMap())
				return false;
			BeginChange();
			maps.Load(target);
			maps.MarkUsed();
			colocation.ClearPendingSnapshots();
			Rebase();
			colocation.Inject(CurrentMap);
			objects.Replace(target.objects);
			NotifyMapChanged();
			return SaveCurrentMap();
		}

		private void BeginChange()
		{
			if (workflow.Phase == MapPhase.Local)
			{
				workflow.EnterLocal();
				return;
			}

			workflow.BeginSwitch(Time.unscaledTime);
			session.SetChanging(true);
		}

		public string DescribeNewMapBlocker() => IsChangingColocation ? MenuCopy.Get("Game", "blocker.preparing-the-colocation-method") : MenuCopy.Get("Game", Policy.NewMapBlocker);
		public bool NewMap()
		{
			if (DescribeNewMapBlocker() != null)
				return false;
			if (workflow.Phase == MapPhase.Local)
				return UnloadCurrentMap();
			if (!SaveCurrentMap())
				return false;

			BeginChange();
			maps.Create();
			colocation.ClearPendingSnapshots();
			colocation.Inject(CurrentMap);
			objects.Replace(Array.Empty<MapObjectEntry>());
			NotifyMapChanged();
			bool saved = SaveCurrentMap();
			RequestTagRegistrationIfNeeded();
			return saved;
		}

		public bool UnloadCurrentMap()
		{
			if (workflow.Phase != MapPhase.Local || !SaveCurrentMap())
				return false;
			workflow.EnterLocal();
			maps.Unload();
			colocation.ClearPendingSnapshots();
			colocation.ClearForNoMap();
			objects.Replace(Array.Empty<MapObjectEntry>());
			NotifyMapChanged();
			return true;
		}

		public string DescribeDeleteBlocker(string id) => id == null
			? MenuCopy.Get("Game", "blocker.no-map-selected")
			: MenuCopy.Get("Game", Policy.DeleteMapBlocker(maps.CurrentId == id));

		public bool DeleteMap(string id)
		{
			if (DescribeDeleteBlocker(id) != null)
				return false;
			if (maps.CurrentId == id && !SaveCurrentMap())
				return false;
			List<string> orphaned = new();
			if (!MapStore.Default.Delete(id, orphaned))
				return false;
			if (maps.CurrentId == id)
			{
				workflow.EnterLocal();
				maps.Unload();
				colocation.ClearPendingSnapshots();
				colocation.ClearForNoMap();
				objects.Replace(Array.Empty<MapObjectEntry>());
				NotifyMapChanged();
			}
			discovery.Forget(id);
			foreach (string guid in orphaned)
				colocation.EraseAnchorSave(guid);
			return true;
		}

		// A blank offline workspace has no document until an allowed action needs one.
		private bool EnsureMap()
		{
			if (maps.HasMap)
				return true;
			if (!Policy.CanCreateMap)
				return false;
			maps.Create();
			colocation.ClearPendingSnapshots();
			colocation.Inject(CurrentMap);
			NotifyMapChanged();
			return true;
		}

		private void MarkMapContentChanged()
		{
			if (!EnsureMap())
				return;
			CaptureAuthoredObjects();
			autosave.Schedule();
		}

		private void OnObjectChanged(MapObject _) => autosave.Schedule();

		/// <summary>
		/// Drains provider changes into the in-memory map. Tags are authored content; anchor
		/// records also include device-local realizations. Saving and erasing happen later.
		/// </summary>
		private void ApplyReferenceChanges()
		{
			if (!Policy.CanRecordReferences || !maps.HasMap || !colocationManager)
				return;
			if (!colocation.HasPendingSnapshots)
				return;

			GameMap before = CurrentMap;
			GameMap snapshot = colocation.TakePendingSnapshot(before.Clone(), ReferenceCapture);
			if (snapshot == null)
				return;

			bool contentChanged = false;
			if (Policy.CanCaptureScene)
				contentChanged = maps.SetTags(snapshot.tags, snapshot.tagSizeCm);

			bool anchorsChanged = maps.SetAnchors(snapshot.anchors);
			QueueDroppedAnchors(before, snapshot);
			if (contentChanged || (before.anchors.Count == 0) != (snapshot.anchors.Count == 0))
				NotifyMapChanged();
			if (contentChanged || anchorsChanged)
				autosave.Schedule();
		}

		// Offline, capture each running provider. In a session, capture the selected provider's
		// anchors so an inactive provider's stale state cannot prune the active method's records.
		private MapReferenceCapture ReferenceCapture
		{
			get
			{
				bool local = workflow.Phase == MapPhase.Local;
				bool anchorsRunning = colocationManager.AnchorProvider && colocationManager.AnchorProvider.IsRunning;
				bool tagsRunning = colocationManager.TagProvider && colocationManager.TagProvider.IsRunning;
				return new MapReferenceCapture
				{
					Anchors = local ? anchorsRunning : colocationManager.UsingAnchorProvider,
					TaggedAnchors = local ? tagsRunning : colocationManager.UsingTagProvider,
					Tags = Policy.CanCaptureScene,
					MirrorLocalAnchors = local
				};
			}
		}

		/// <summary>
		/// Records UUIDs removed from two snapshots of the SAME map. This does not erase anything:
		/// the old on-disk document may still need them if the next save fails, and another map
		/// (including a fork) may share them. Loading a different map is not a reference deletion.
		/// </summary>
		private void QueueDroppedAnchors(GameMap before, GameMap after)
		{
			foreach (MapAnchorEntry anchor in before.anchors)
				if (!after.TryGetAnchor(anchor.guid, out _))
					anchorsToErase.Add(anchor.guid);
		}

		/// <summary>
		/// Captures allowed runtime changes, saves, then publishes shared content if hosting.
		/// Only a successful save permits checking queued anchors for erasure. A failed save
		/// leaves those candidates queued and schedules another attempt.
		/// </summary>
		public bool SaveCurrentMap()
		{
			ApplyReferenceChanges();
			CaptureAuthoredObjects();
			if (!maps.Save(Policy.PublishesContent))
			{
				autosave.Schedule();
				return false;
			}

			if (Policy.PublishesContent)
				session.Publish(CurrentMap);

			EraseOrphanedAnchors();
			return true;
		}

		private void CaptureAuthoredObjects()
		{
			if (!Policy.CanCaptureScene || !maps.HasMap)
				return;
			if (objects.TryCapture(out List<MapObjectEntry> snapshot))
				maps.SetObjects(snapshot);
		}

		private void OnAutosave()
		{
			if (workflow.Phase == MapPhase.AdoptingSessionMap)
			{
				CompleteAdoption();
				return;
			}

			if (workflow.Phase != MapPhase.Stopped)
				SaveCurrentMap();
		}

		/// <summary>
		/// After saving, checks deletion candidates against ALL saved maps on this device.
		/// Only UUIDs no saved map references are sent to the provider for erasure. This is a
		/// best-effort request: provider erasure is asynchronous and is not awaited here.
		/// </summary>
		private void EraseOrphanedAnchors()
		{
			foreach (string guid in anchorsToErase)
				if (!MapStore.Default.IsAnchorReferenced(guid))
					colocation.EraseAnchorSave(guid);

			anchorsToErase.Clear();
		}

		private void OnBusActivated()
		{
			if (!SyncBus.IsAuthority && Policy.CanCaptureScene)
			{
				// Preserve pre-session objects before entering the awaiting state.
				if (maps.HasMap)
					maps.SetObjects(objects.CaptureLocal());
				maps.Save();
			}

			workflow.EnterSession(SyncBus.IsAuthority);
			registrationRequestedFor = null;
		}

		private void OnAuthorityReady()
		{
			workflow.BeginHosting(IsChangingMap, Time.unscaledTime);
			if (!EnsureMap())
				return;

			maps.MarkUsed();
			SaveCurrentMap();
			objects.SpawnLocalObjects();
			RequestTagRegistrationIfNeeded();
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			CancelMethodPreparation();
			if (isAuthority) preparingMethod.Value = false;
			if (!isAuthority)
				workflow.EnterSession(authority: false);
		}

		private void OnSessionMapReceived(MapIdentity identity, List<MapObjectEntry> placements)
		{
			string id = identity.id.ToString("N");
			string version = identity.version.ToString("N");
			GameMap current = CurrentMap;
			bool following = workflow.Phase == MapPhase.FollowingSession;
			bool sameMap = current != null && current.id == id;
			if (following && sameMap && current.version == version)
				return;

			GameMap received = sameMap ? current : ReadLocalCopy(id);
			received.name = identity.name.ToString();
			received.version = version;
			received.preferredColocationMethod = identity.preferredColocationMethod;
			received.systemFrameForTagSetup = identity.systemFrameForTagSetup;
			received.objects = placements;
			bool changesFrame = !workflow.HasSessionFrame || !sameMap;
			received = colocation.AdoptProviderState(received, changesFrame);
			workflow.BeginAdoption(received, changesFrame);
			CompleteAdoption();
		}

		private static GameMap ReadLocalCopy(string id)
		{
			if (MapStore.Default.TryGet(id, out GameMap saved))
				return saved;

			return new GameMap { id = id };
		}

		/// <summary>
		/// Persists the received session map before making it current. Until that succeeds,
		/// the workflow retains the incoming snapshot for retry and blocks editing. Networked
		/// objects arrive separately; this path removes the offline objects when changing frames.
		/// </summary>
		private bool CompleteAdoption()
		{
			GameMap incoming = workflow.IncomingMap;
			GameMap previous = CurrentMap;
			bool changesFrame = workflow.AdoptionChangesFrame;

			// Both storage failures stay in AdoptingSessionMap and can be retried explicitly.
			if (previous != null && previous.id != incoming.id && !maps.Save())
			{
				autosave.Schedule();
				return false;
			}
			if (!maps.TryAdopt(incoming))
			{
				autosave.Schedule();
				return false;
			}

			if (previous != null && previous.id == incoming.id)
				QueueDroppedAnchors(previous, incoming);
			if (incoming.systemFrameForTagSetup && (previous == null || !previous.systemFrameForTagSetup))
				registrationRequestedFor = null;

			workflow.FinishAdoption();
			if (changesFrame)
			{
				objects.RemoveLocalObjects();
				Rebase();
			}

			NotifyMapChanged();
			RequestTagRegistrationIfNeeded();
			autosave.Schedule();
			return true;
		}

		private void OnBusDeactivated()
		{
			latestMethodRequest = Guid.Empty;
			applyMapPreference = false;
			CancelMethodPreparation();
			if (workflow.Phase == MapPhase.Stopped)
				return;

			int operation = workflow.BeginRestore();
			registrationRequestedFor = null;
			maps.Save();
			if (maps.HasMap)
				colocation.Inject(CurrentMap);

			RebuildAfterSession(operation, lifetime.Token);
		}

		// NGO must finish destroying session objects before their local replacements are built.
		// The workflow operation number prevents an old disconnect from rebuilding over a newer session.
		private async void RebuildAfterSession(int operation, CancellationToken token)
		{
			try
			{
				do
				{
					await Awaitable.NextFrameAsync(token);
					if (!workflow.IsCurrentRestore(operation))
						return;
				}
				while (NetworkIsShuttingDown);

				objects.Replace(CurrentMap?.objects ?? new List<MapObjectEntry>());
				workflow.EnterLocal();
				NotifyMapChanged();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private static bool NetworkIsShuttingDown
		{
			get
			{
				NetworkManager manager = NetworkManager.Singleton;
				return manager != null && (manager.IsListening || manager.ShutdownInProgress);
			}
		}

		// ------- session colocation method -------------------------------------

		public string DescribeColocationMethodBlocker(ColocationManager.ColocationMethod method)
		{
			if (!ColocationManager.IsValidMethod(method)) return MenuCopy.Get("Game", "blocker.unknown-colocation-method");
			bool targetHasReferences = !ColocationManager.UsesSavedReferences(method) ||
				(method == ColocationManager.ColocationMethod.AprilTag ? maps.HasTags : maps.AnchorCount > 0 ||
					(SyncBus.Active && colocationManager != null && colocationManager.AnchorProvider != null &&
					 colocationManager.AnchorProvider.HasMinter)) ||
				RequiresTagSetup(method);
			string blocker = MenuCopy.Get("Game", Policy.ColocationMethodBlocker(targetHasReferences));
			if (blocker != null) return blocker;
			if (!colocationManager) return MenuCopy.Get("Game", "blocker.no-colocation-manager");
			if (method == ColocationManager.ColocationMethod.MetaSharedAnchor)
			{
				if (!colocationManager.AnchorProvider) return MenuCopy.Get("Game", "blocker.no-shared-anchor-provider");
				if (SyncBus.Active && !colocationManager.AnchorProvider.HasMinter)
					return MenuCopy.Get("Game", "alignment.waiting-for-anchor-headset");
				if (!SyncBus.Active && maps.AnchorCount == 0 && !maps.IsEmpty)
					return MenuCopy.Get("Game", "blocker.align-to-a-registered-tag-first-to-create-an-anchor");
			}
			else if (method == ColocationManager.ColocationMethod.AprilTag && !colocationManager.TagProvider)
				return MenuCopy.Get("Game", "blocker.no-apriltag-provider");
			else if (method == ColocationManager.ColocationMethod.TwoAprilTags && colocationManager.TwoTagProvider == null)
				return MenuCopy.Get("Game", "blocker.no-apriltag-provider");
			return null;
		}

		public string DescribeColocationPreferenceBlocker() => MenuCopy.Get("Game",
			Policy.ColocationPreferenceBlockerFor(HeadsetConfiguration.IsOperatorDevice));

		private bool RequiresTagSetup(ColocationManager.ColocationMethod requested) =>
			colocationManager != null && ColocationManager.RequiresTagSetup(
				colocationManager.SelectedMethod, requested, maps.HasTags);

		public bool SetPreferredColocationMethod(ColocationManager.ColocationMethod method)
		{
			if (!ColocationManager.IsValidMethod(method) ||
				DescribeColocationPreferenceBlocker() != null || !EnsureMap()) return false;
			if (SyncBus.Active)
			{
				latestMethodRequest = Guid.NewGuid();
				methodRequest.Raise(new MethodRequest { requestId = latestMethodRequest, mapId = Guid.Parse(maps.CurrentId), method = method });
				return true;
			}
			ApplyReferenceChanges();
			GameMap previous = CurrentMap;
			maps.SetPreferredColocationMethod(method, RequiresTagSetup(method));
			NotifyMapChanged();
			bool saved = SaveCurrentMap();
			if (saved)
			{
				if (!previous.systemFrameForTagSetup && CurrentMap.systemFrameForTagSetup)
					registrationRequestedFor = null;
				RequestTagRegistrationIfNeeded();
			}
			else
			{
				maps.SetPreferredColocationMethod(previous.preferredColocationMethod, previous.systemFrameForTagSetup);
				NotifyMapChanged();
			}
			return saved;
		}

		private void OnMethodRequested(ulong sender, MethodRequest request)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			string blocker = MenuCopy.Get("Game", Policy.ColocationPreferenceBlockerFor(
				HeadsetConfiguration.IsOperatorDevice && sender == SyncBus.LocalClientId));
			if (maps.CurrentId != request.mapId.ToString("N")) blocker = MenuCopy.Get("Game", "alignment.map-changed");
			if (!ColocationManager.IsValidMethod(request.method)) blocker = MenuCopy.Get("Game", "blocker.unknown-colocation-method");
			if (blocker != null) { RejectMethod(sender, blocker, request); return; }
			ApplyReferenceChanges();
			ApplyPreferredMethod(sender, request);
		}

		/// <summary>
		/// Authority-side live handoff: validate, prepare references, save an explicit preference,
		/// then commit the session method. The old method stays selected during preparation.
		/// An automatic request uses the currently compatible method without rewriting the
		/// map's authored preference; an explicit request saves the user's chosen method.
		/// </summary>
		private async void ApplyPreferredMethod(ulong sender, MethodRequest request)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !maps.HasMap) return;
			bool explicitlyRequested = request.requestId != Guid.Empty;
			if (request.method == colocationManager.Method)
			{
				// The active fallback may already match the choice, while the saved preference differs.
				if (explicitlyRequested) SaveMethodPreference(sender, request);
				return;
			}
			string blocker = DescribeColocationMethodBlocker(request.method);
			if (blocker != null) { RejectMethod(sender, blocker, request); return; }

			// Keep the known system frame live while the headset records its first tag in it.
			// This is saved setup intent, not an in-flight native operation: do not hold edits
			// or start a timeout. The tag revision makes PreferredSessionMethod change, which
			// schedules the normal authority handoff once the new reference is available.
			if (RequiresTagSetup(request.method))
			{
				if (explicitlyRequested && SaveMethodPreference(sender, request))
					RequestTagRegistrationIfNeeded();
				return;
			}

			ColocationManager.ColocationMethod previous = colocationManager.Method;
			CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
			methodPreparation = operation;
			methodPreparationDeadline = Time.realtimeSinceStartupAsDouble + 20;
			methodRequester = sender;
			methodPreparationRequest = request;
			// Hold conflicting session actions until this operation commits, fails, or times out.
			preparingMethod.Value = true;
			try
			{
				if (request.method == ColocationManager.ColocationMethod.MetaSharedAnchor)
				{
					List<AnchorConstraintData> prepared =
						await colocationManager.AnchorProvider.PrepareSessionSharingAsync(operation.Token);
					operation.Token.ThrowIfCancellationRequested();
					// Uploads yield control: this peer may have disconnected, lost authority, changed
					// maps, or superseded this operation. None of those completions may commit here.
					if (!SyncBus.Active || !SyncBus.IsAuthority || methodPreparation != operation ||
						maps.CurrentId != request.mapId.ToString("N") || colocationManager.Method != previous) return;
					if (RoundInProgress) { RejectMethod(sender, MenuCopy.Get("Game", "blocker.wait-until-the-round-ends"), request); return; }
					// Prepared UUIDs may be private realizations of the remote minter's tags.
					// Their latest canonical poses arrived with the acknowledged uploads.
					ApplyReferenceChanges();
					if (prepared.Count == 0)
					{
						RejectMethod(sender, MenuCopy.Get("Game", "alignment.share-failed"), request);
						return;
					}
					// These dictionary writes precede the method commit on SyncBus's ordered
					// channel; late joiners receive both together in the combined snapshot.
					if (explicitlyRequested && !SaveMethodPreference(sender, request)) return;
					colocationManager.AnchorProvider.SetConstraints(prepared);
				}
				else ApplyReferenceChanges();

				// Persist the user's choice before switching everyone; a save failure keeps the old method.
				if (request.method != ColocationManager.ColocationMethod.MetaSharedAnchor &&
					explicitlyRequested && !SaveMethodPreference(sender, request)) return;
				colocationManager.CommitMethod(request.method);
			}
			catch (OperationCanceledException)
			{
				if (SyncBus.Active && SyncBus.IsAuthority && methodPreparation == operation)
					RejectMethod(sender, MenuCopy.Get("Game", "alignment.timeout"), request);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				if (SyncBus.Active && SyncBus.IsAuthority && methodPreparation == operation)
					RejectMethod(sender, MenuCopy.Get("Game", "alignment.prepare-failed"), request);
			}
			finally
			{
				// A late completion owns its token source, but must not clear a newer operation's hold.
				if (methodPreparation == operation)
				{
					methodPreparation = null;
					colocationManager.AnchorProvider?.FinishSessionSharing();
					if (SyncBus.Active && SyncBus.IsAuthority) preparingMethod.Value = false;
				}
				operation.Dispose();
			}
		}

		private bool SaveMethodPreference(ulong sender, MethodRequest request)
		{
			GameMap previous = CurrentMap;
			maps.SetPreferredColocationMethod(request.method, RequiresTagSetup(request.method));
			if (!SaveCurrentMap())
			{
				maps.SetPreferredColocationMethod(previous.preferredColocationMethod, previous.systemFrameForTagSetup);
				RejectMethod(sender, MenuCopy.Get("Game", "alignment.save-failed"), request);
				return false;
			}
			if (!previous.systemFrameForTagSetup && CurrentMap.systemFrameForTagSetup)
				registrationRequestedFor = null;
			NotifyMapChanged();
			return true;
		}

		private readonly List<KeyValuePair<ulong, HeadsetReadiness>> anchorCandidates = new();

		private void ConfigureAnchorMinter()
		{
			var provider = colocationManager.AnchorProvider;
			if (provider == null) return;
			provider.SelectMinter = SelectAnchorMinter;
			provider.LocalReadinessGate = () => !HeadsetConfiguration.IsOperatorDevice &&
				(!SyncBus.Active || (PlayerAvatar.Local != null && PlayerAvatar.Local.HeadsetStatus != null &&
					PlayerAvatar.Local.HeadsetStatus.Readiness.CanMintSharedAnchors));
			provider.ValidateRemoteMint = sender => !IsChangingMap && !IsChangingColocation &&
				colocationManager.UsingAnchorProvider && ReadyForAnchorWork(sender, allowBootstrap: true);
			provider.PreparationGate = () => !IsChangingMap && CheckFrameAgreement();
			provider.PreparationMintingGate = () => !maps.HasTags && CheckFrameAgreement();
			provider.SharingCandidates = LocalSharingCandidates;
			provider.ValidatePreparedAnchor = (sender, data) => ReadyForAnchorWork(sender, allowBootstrap: true) &&
				(data.bindingId < 0 ? !maps.HasTags : CurrentMap.TryGetTag(data.bindingId, out _));
		}

		private void ClearAnchorMinter()
		{
			var provider = colocationManager != null ? colocationManager.AnchorProvider : null;
			if (provider == null) return;
			provider.SelectMinter = null;
			provider.LocalReadinessGate = null;
			provider.ValidateRemoteMint = null;
			provider.PreparationGate = null;
			provider.PreparationMintingGate = null;
			provider.SharingCandidates = null;
			provider.ValidatePreparedAnchor = null;
		}

		private ulong? SelectAnchorMinter()
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || !maps.HasMap) return null;
			anchorCandidates.Clear();
			foreach (var pair in PlayerAvatar.All)
				if (pair.Value != null && pair.Value.IsSpawned && pair.Value.HeadsetStatus != null &&
					manager.ConnectedClientsIds.Contains(pair.Key))
					anchorCandidates.Add(new(pair.Key, pair.Value.HeadsetStatus.Readiness));
			var assignment = colocationManager.AnchorProvider.Minter;
			return AnchorMinterPolicy.Select(assignment.assigned ? assignment.clientId : null,
				manager.CurrentSessionOwner, HeadsetConfiguration.SessionIsOperatorManaged,
				Guid.Parse(maps.CurrentId), colocationManager.SelectedMethod, anchorCandidates);
		}

		private bool ReadyForAnchorWork(ulong sender, bool allowBootstrap)
		{
			if (!maps.HasMap || !PlayerAvatar.All.TryGetValue(sender, out var avatar) ||
				avatar == null || !avatar.IsSpawned || avatar.HeadsetStatus == null) return false;
			HeadsetReadiness readiness = avatar.HeadsetStatus.Readiness;
			return readiness.CanMintSharedAnchors && readiness.mapId == Guid.Parse(maps.CurrentId) &&
				readiness.method == colocationManager.SelectedMethod &&
				(readiness.referenceFrameTrusted || (allowBootstrap && maps.IsEmpty && !maps.HasTags && maps.AnchorCount == 0));
		}

		private IReadOnlyList<AnchorConstraintData> LocalSharingCandidates()
		{
			List<AnchorConstraintData> result = new();
			if (colocationManager.UsingTagProvider)
			{
				List<Anaglyph.XR.SharedSpaces.AprilTags.TaggedAnchorConstraintData> local = new();
				colocationManager.TagProvider.GetLocalAnchorConstraints(local);
				foreach (var entry in local) result.Add(new(entry.guid, entry.canonPose, entry.tagId));
			}
			else if (colocationManager.UsingAnchorProvider)
				foreach (var entry in colocationManager.AnchorProvider.Constraints)
					result.Add(new(entry.Key, entry.Value.canonPose, entry.Value.bindingId));
			return result;
		}

		private void CancelMethodPreparation()
		{
			// Detach before cancellation resumes the awaiter. Its finally block disposes the token
			// source; the caller handles the synchronized hold for its timeout/session-exit path.
			CancellationTokenSource operation = methodPreparation;
			methodPreparation = null;
			operation?.Cancel();
			colocationManager?.AnchorProvider?.FinishSessionSharing();
		}

		private void RejectMethod(ulong requester, string reason, MethodRequest request)
		{
			if (request.requestId == Guid.Empty) return;
			MethodRejection rejection = new() { requester = requester, requestId = request.requestId, mapId = request.mapId };
			rejection.reason.CopyFromTruncated(reason);
			methodRejection.Raise(rejection);
		}

		private void OnMethodRejected(ulong _, MethodRejection rejection)
		{
			if (rejection.requester != SyncBus.LocalClientId || rejection.requestId != latestMethodRequest ||
				CurrentMap?.id != rejection.mapId.ToString("N")) return;
			UserErrors.RaiseLocalized(UserErrorArea.Game, "error.alignment-title", "error.alignment-details", rejection.reason.ToString());
		}

		private void OnPreparingMethodChanged(bool _, bool __) => ColocationSettingsChanged.Invoke();

		private void OnColocationMethodChanged()
		{
			latestMethodRequest = Guid.Empty;
			if (SyncBus.Active && maps.HasMap && Policy.CanRecordReferences)
			{
				GameMap before = CurrentMap;
				GameMap after = colocation.AdoptProviderState(before.Clone(), restoreLocalAnchors: false);
				maps.SetAnchors(after.anchors);
				QueueDroppedAnchors(before, after);
				autosave.Schedule();
			}
			ColocationSettingsChanged.Invoke();
		}

		public bool RequestPlaceObject(MapObject prefab, Vector3 position, Quaternion rotation)
		{
			if (!CheckCanEditMap() || prefab == null || !EnsureMap())
				return false;
			return objects.RequestPlace(prefab, position, rotation);
		}

		public bool RequestRemoveObject(MapObject obj) => CheckCanEditMap() && objects.RequestRemove(obj);
		public void CommitObjectMove(MapObject obj)
		{
			if (CheckCanEditMap())
				objects.RequestMove(obj);
		}

		public bool SessionIsWaitingOnFirstTag => Policy.NeedsFirstTag ||
			(colocationManager != null && colocationManager.IsSettingUpTags &&
			 Policy.CanRecordReferences && !IsChangingMap && !IsChangingColocation);
		private void RequestTagRegistrationIfNeeded()
		{
			if (!SessionIsWaitingOnFirstTag || registrationRequestedFor == maps.CurrentId)
				return;
			registrationRequestedFor = maps.CurrentId;
			MapEditor.MapEditor.RequestTagRegistration();
		}

		public string DescribeTagSetupBlocker() =>
			MenuCopy.Get("Game", Policy.ReferenceSetupBlocker) ?? DescribeTagRegistrationBlocker();

		public string DescribeTagRegistrationBlocker() => MenuCopy.Get("Game", Policy.TagRegistrationBlocker);

		private bool ValidateTagRegistration(ulong sender, int tagId, Pose pose)
		{
			if (!SyncBus.Active || sender == SyncBus.LocalClientId)
				return DescribeTagRegistrationBlocker() == null;
			if (workflow.Phase != MapPhase.Hosting || IsChangingMap || IsChangingColocation) return false;
			// A first tag can define a genuinely blank world. Once content exists, the
			// registering headset must already have aligned to that content's frame.
			return maps.IsEmpty ||
				(Player.PlayerAvatar.All.TryGetValue(sender, out Player.PlayerAvatar player) && player.IsAligned);
		}
		public bool RegisterTag(int tagId, Pose worldPose)
		{
			if (DescribeTagRegistrationBlocker() != null || !EnsureMap())
				return false;
			return colocation.RequestRegisterTag(tagId, worldPose);
		}

		public bool UnregisterTag(int tagId)
		{
			GameMap map = CurrentMap;
			if (Policy.TagRemovalBlocker != null || map == null || !map.TryGetTag(tagId, out _))
				return false;

			return colocation.RequestUnregisterTag(tagId);
		}

		public string DescribeTagRemovalBlocker() => MenuCopy.Get("Game", Policy.TagRemovalBlocker);

		public void UnregisterAllTags()
		{
			GameMap map = CurrentMap;
			if (Policy.TagRemovalBlocker != null || map == null)
				return;

			foreach (MapTagEntry tag in map.tags.ToArray())
				colocation.RequestUnregisterTag(tag.id);
		}

		public float EffectiveTagSizeCm
		{
			get
			{
				if (CurrentMap is { tagSizeCm: > 0f } map)
					return map.tagSizeCm;
				if (colocationManager && colocationManager.TagProvider)
					return colocationManager.TagProvider.TagSizeCm;

				return 0f;
			}
		}

		public string DescribeTagSizeBlocker() => MenuCopy.Get("Game", Policy.TagSizeBlocker);
		public bool SetTagSize(float centimeters)
		{
			if (centimeters <= 0f || DescribeTagSizeBlocker() != null || !EnsureMap())
				return false;
			return colocation.RequestTagSize(centimeters);
		}

		public const int MaxMapNameLength = 40;
		public string DescribeRenameBlocker() => MenuCopy.Get("Game", Policy.RenameBlocker);
		public bool RenameMap(string name)
		{
			name = name?.Trim();
			if (DescribeRenameBlocker() != null || string.IsNullOrEmpty(name) || !EnsureMap())
				return false;
			if (name.Length > MaxMapNameLength)
				name = name.Substring(0, MaxMapNameLength);
			if (!maps.Rename(name))
				return false;
			NotifyMapChanged();
			return SaveCurrentMap();
		}

#if UNITY_EDITOR
		public bool RestoreLastMapForSimulation()
		{
			if (MainXRRig.Instance == null || UnityEngine.XR.XRSettings.enabled ||
				maps.HasMap || !Policy.CanProbe) return true;
			foreach (GameMap map in MapStore.Default.GetByLastUsed())
				return LoadMap(map.id);
			return true;
		}
#endif

		private bool CanProbe => Policy.CanProbe && discovery.IsAvailable;
		private async void StartupProbe(CancellationToken token)
		{
#if UNITY_EDITOR
			if (MainXRRig.Instance != null && !UnityEngine.XR.XRSettings.enabled)
			{
				RestoreLastMapForSimulation();
				return;
			}
#endif
			try
			{
				await Awaitable.WaitForSecondsAsync(1f, token);
				await ProbeAndAutoLoad(token);
			}
			catch (OperationCanceledException) { }
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		public async Awaitable ProbeAndAutoLoad(CancellationToken ctkn = default)
		{
			if (!CanProbe)
				return;
			GameMap found = await discovery.ProbeAsync(stopAtFirstLocalized: true, ctkn);
			if (found != null && !maps.HasMap && Policy.CanProbe)
				LoadMap(found.id);
		}

		public async Awaitable ProbeAllMaps(CancellationToken ctkn = default)
		{
			if (CanProbe)
				await discovery.ProbeAsync(stopAtFirstLocalized: false, ctkn);
		}
	}
}
