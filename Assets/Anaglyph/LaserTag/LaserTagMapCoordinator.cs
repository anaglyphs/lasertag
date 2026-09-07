using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode.SyncVariables;
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
		private readonly HashSet<string> anchorsToErase = new();
		private readonly MapWorkflow workflow = new();
		private int frameRebasedOn = -1;
		private string registrationRequestedFor;

		private struct MethodRequest
		{
			public Guid mapId;
			public ColocationManager.ColocationMethod method;
		}

		private struct MethodRejection
		{
			public ulong requester;
			public FixedString128Bytes reason;
		}

		private readonly SyncEvent<MethodRequest> methodRequest = new("map.colocation.request", EventRoute.ToAuthority);
		private readonly SyncEvent<MethodRejection> methodRejection = new("map.colocation.rejected", EventRoute.ViaAuthority);
		private readonly SyncVariable<bool> preparingMethod = new("map.colocation.preparing");
		private CancellationTokenSource methodPreparation;
		private double methodPreparationDeadline;
		private ulong methodRequester;
		private bool applyMapPreference;
		private ulong preferenceRequester;
		public bool IsChangingColocation => preparingMethod.Value;

		public MapPhase Phase => workflow.Phase;
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
					RejectMethod(methodRequester, "Anchor preparation timed out; the previous method is still active");
				}
			}
			ApplyReferenceChanges();
			if (applyMapPreference && workflow.Phase == MapPhase.Hosting && !IsChangingColocation && !RoundInProgress)
			{
				applyMapPreference = false;
				ApplyPreferredMethod(preferenceRequester);
			}
			if (workflow.Phase != MapPhase.SwitchingMap)
				return;

			if (objects.IsReplacementComplete && CheckFrameAgreement())
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

		private void NotifyMapChanged()
		{
			GameMap map = CurrentMap;
			objects.SetMapId(map?.id);
			var previousAvailableMethod = colocationManager != null ? colocationManager.PreferredSessionMethod : default;
			colocationManager?.ConfigureMap(map != null, map != null && map.HasTags,
				map != null && !map.IsEmpty, map != null && map.anchors.Count > 0,
				map != null ? map.preferredColocationMethod : default, CheckWorldFrameIsTrusted);
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

		private void Rebase()
		{
			frameRebasedOn = Time.frameCount;
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
			sessionUsesTags: SessionUsesTags);

		public bool CheckWorldFrameIsTrusted() => Policy.FrameIsTrusted;
		public bool CheckCanEditMap() => Policy.EditBlocker == null;

		private bool CheckFrameAgreement()
		{
			if (!maps.HasMap || Time.frameCount == frameRebasedOn || !colocationManager)
				return false;
			int realizable = colocationManager.CountRealizableReferences();
			// Registered tags define a frame even before this headset has realized an anchor.
			if (colocationManager.UsingTagProvider && colocationManager.ActiveProvider.IsAvailable &&
			    maps.HasTags && !ColocationManager.IsColocated)
				return false;
			if (realizable == 0)
				return (colocationManager.ActiveProvider != null && !colocationManager.ActiveProvider.IsAvailable) ||
					MapPolicy.CanBootstrapFrame(maps.HasTags, maps.AnchorCount, maps.IsEmpty, SyncBus.Active, SyncBus.IsAuthority);
			FitAgreement agreement = colocationManager.Agreement;
			return ColocationManager.IsColocated && agreement.agreeingCount >= Mathf.Min(realizable, 2) && agreement.meanAgrees;
		}

		public string DescribeChangeBlocker(string id)
		{
			if (IsChangingColocation) return "Preparing the colocation method";
			MapStore.Default.TryGet(id, out GameMap target);
			return Policy.ChangeMapBlocker(target, maps.CurrentId == id, GetMapPresence(id));
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

		public string DescribeNewMapBlocker() => IsChangingColocation ? "Preparing the colocation method" : Policy.NewMapBlocker;
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
			? "No map selected"
			: Policy.DeleteMapBlocker(maps.CurrentId == id);

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

		private void QueueDroppedAnchors(GameMap before, GameMap after)
		{
			foreach (MapAnchorEntry anchor in before.anchors)
				if (!after.TryGetAnchor(anchor.guid, out _))
					anchorsToErase.Add(anchor.guid);
		}

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
			if (method != ColocationManager.ColocationMethod.AprilTag &&
				method != ColocationManager.ColocationMethod.MetaSharedAnchor) return "Unknown colocation method";
			string blocker = Policy.ColocationMethodBlocker(method == ColocationManager.ColocationMethod.AprilTag);
			if (blocker != null) return blocker;
			if (!colocationManager) return "No colocation manager";
			if (method == ColocationManager.ColocationMethod.MetaSharedAnchor)
			{
				if (!colocationManager.AnchorProvider) return "No shared-anchor provider";
				// A requester cannot judge the authority's runtime or private realizations.
				if (SyncBus.IsAuthority && SyncBus.Active && !colocationManager.AnchorProvider.CanShareAnchors)
					return "The host cannot share spatial anchors";
				if (SyncBus.IsAuthority && maps.AnchorCount == 0) return "Align to a registered tag first to create an anchor";
			}
			else if (!colocationManager.TagProvider) return "No AprilTag provider";
			return null;
		}

		public string DescribeColocationPreferenceBlocker() => Policy.ColocationPreferenceBlocker;

		public bool SetPreferredColocationMethod(ColocationManager.ColocationMethod method)
		{
			if ((method != ColocationManager.ColocationMethod.AprilTag &&
				method != ColocationManager.ColocationMethod.MetaSharedAnchor) ||
				DescribeColocationPreferenceBlocker() != null || !EnsureMap()) return false;
			if (SyncBus.Active)
			{
				methodRequest.Raise(new MethodRequest { mapId = Guid.Parse(maps.CurrentId), method = method });
				return true;
			}
			ApplyReferenceChanges();
			maps.SetPreferredColocationMethod(method);
			NotifyMapChanged();
			return SaveCurrentMap();
		}

		private void OnMethodRequested(ulong sender, MethodRequest request)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			string blocker = DescribeColocationPreferenceBlocker();
			if (maps.CurrentId != request.mapId.ToString("N")) blocker = "The session map changed; try again";
			if (request.method != ColocationManager.ColocationMethod.AprilTag &&
				request.method != ColocationManager.ColocationMethod.MetaSharedAnchor) blocker = "Unknown colocation method";
			if (blocker != null) { RejectMethod(sender, blocker); return; }
			ApplyReferenceChanges();
			maps.SetPreferredColocationMethod(request.method);
			if (!SaveCurrentMap()) { RejectMethod(sender, "The map preference could not be saved"); return; }
			NotifyMapChanged();
			preferenceRequester = sender;
			applyMapPreference = true;
		}

		private async void ApplyPreferredMethod(ulong sender)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !maps.HasMap) return;
			GameMap map = CurrentMap;
			MethodRequest request = new()
			{
				mapId = Guid.Parse(map.id),
				method = ColocationManager.CompatibleMethod(map.preferredColocationMethod,
					map.HasTags, !map.IsEmpty, map.anchors.Count > 0)
			};
			if (request.method == colocationManager.Method) return;
			string blocker = DescribeColocationMethodBlocker(request.method);
			if (blocker != null) { RejectMethod(sender, "Preference saved. " + blocker); return; }

			ColocationManager.ColocationMethod previous = colocationManager.Method;
			CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
			methodPreparation = operation;
			methodPreparationDeadline = Time.realtimeSinceStartupAsDouble + 20;
			methodRequester = sender;
			preparingMethod.Value = true;
			try
			{
				if (request.method == ColocationManager.ColocationMethod.MetaSharedAnchor)
				{
					HashSet<Guid> shared = new();
					await colocationManager.AnchorProvider.PrepareSharingAsync(SharedAnchorCandidates(), shared, operation.Token);
					operation.Token.ThrowIfCancellationRequested();
					if (!SyncBus.Active || !SyncBus.IsAuthority || methodPreparation != operation ||
						maps.CurrentId != request.mapId.ToString("N") || colocationManager.Method != previous) return;
					if (RoundInProgress) { RejectMethod(sender, "Wait until the round ends"); return; }
					// Tags keep correcting while uploads run. Commit the current poses and only
					// UUIDs that still belong to this map and were successfully shared.
					ApplyReferenceChanges();
					List<AnchorConstraintData> prepared = SharedAnchorCandidates();
					prepared.RemoveAll(entry => !shared.Contains(entry.guid));
					if (prepared.Count == 0)
					{
						RejectMethod(sender, "No tracked anchor could be shared. Check the host's alignment and internet connection");
						return;
					}
					// These dictionary writes precede the method commit on SyncBus's ordered
					// channel; late joiners receive both together in the combined snapshot.
					colocationManager.AnchorProvider.SetConstraints(prepared);
				}
				else ApplyReferenceChanges();

				colocationManager.CommitMethod(request.method);
			}
			catch (OperationCanceledException)
			{
				if (SyncBus.Active && SyncBus.IsAuthority && methodPreparation == operation)
					RejectMethod(sender, "Anchor preparation timed out; the previous method is still active");
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				if (SyncBus.Active && SyncBus.IsAuthority && methodPreparation == operation)
					RejectMethod(sender, "Could not prepare the colocation method; try again");
			}
			finally
			{
				if (methodPreparation == operation)
				{
					methodPreparation = null;
					if (SyncBus.Active && SyncBus.IsAuthority) preparingMethod.Value = false;
				}
				operation.Dispose();
			}
		}

		private List<AnchorConstraintData> SharedAnchorCandidates()
		{
			List<AnchorConstraintData> result = new();
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (MapGuid.TryParse(entry.guid, out Guid guid))
					result.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));
			return result;
		}

		private void CancelMethodPreparation()
		{
			CancellationTokenSource operation = methodPreparation;
			methodPreparation = null;
			operation?.Cancel();
		}

		private void RejectMethod(ulong requester, string reason)
		{
			MethodRejection rejection = new() { requester = requester };
			rejection.reason.CopyFromTruncated(reason);
			methodRejection.Raise(rejection);
		}

		private void OnMethodRejected(ulong _, MethodRejection rejection)
		{
			if (rejection.requester == SyncBus.LocalClientId)
				UserErrors.Raise("Colocation preference", rejection.reason.ToString());
		}

		private void OnPreparingMethodChanged(bool _, bool __) => ColocationSettingsChanged.Invoke();

		private void OnColocationMethodChanged()
		{
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

		public bool SessionIsWaitingOnFirstTag => Policy.NeedsFirstTag;
		private void RequestTagRegistrationIfNeeded()
		{
			if (!SessionIsWaitingOnFirstTag || registrationRequestedFor == maps.CurrentId)
				return;
			registrationRequestedFor = maps.CurrentId;
			MapEditor.MapEditor.RequestTagRegistration();
		}

		public string DescribeTagRegistrationBlocker() => Policy.TagRegistrationBlocker;

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

		public string DescribeTagSizeBlocker() => Policy.TagSizeBlocker;
		public bool SetTagSize(float centimeters)
		{
			if (centimeters <= 0f || DescribeTagSizeBlocker() != null || !EnsureMap())
				return false;
			return colocation.RequestTagSize(centimeters);
		}

		public const int MaxMapNameLength = 40;
		public string DescribeRenameBlocker() => Policy.RenameBlocker;
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

		private bool CanProbe => Policy.CanProbe && discovery.IsAvailable;
		private async void StartupProbe(CancellationToken token)
		{
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
