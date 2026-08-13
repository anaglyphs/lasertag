using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Owns the map this device currently has loaded: which one it is, when it is persisted, and
	/// what happens to the world and the colocation providers as it changes.
	///
	/// The work itself is split across collaborators it owns — <see cref="MapObjectDirector"/>
	/// for the gameplay objects, <see cref="MapColocationAdapter"/> for the reference providers,
	/// <see cref="MapSessionSync"/> for the session's map identity, <see cref="MapDiscovery"/>
	/// for working out which maps belong to this room. They are plain objects rather than
	/// components: each has exactly one owner, and there is nothing for a scene to configure that
	/// this class does not already hold.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MapManager : MonoBehaviour
	{
		public static MapManager Instance { get; private set; }

		[Tooltip("Fronts the colocation providers and the colocator; the map layer wires through it")]
		[SerializeField] private ColocationManager colocationManager;

		[Tooltip("Every placeable map object; also how a saved map's prefab ids resolve")]
		[SerializeField] private MapObjectDatabase objectDatabase;

		[Tooltip("How long the room probe lets each anchor try to localize")]
		[SerializeField] private float probeTimeoutSeconds = 8f;

		[Tooltip("How long an edit or provider change waits before it reaches disk")]
		[SerializeField] private float saveDebounceSeconds = 2f;

		[Tooltip("How long a map change waits for the new references to align before giving up")]
		[SerializeField] private float switchTimeoutSeconds = 20f;

		public GameMap CurrentMap { get; private set; }
		public event Action<GameMap> CurrentMapChanged = delegate { };

		/// <summary>
		/// World space has been re-based onto a different map's references. Every pose this
		/// device holds shifts by a rigid transform, so anything storing world-space data
		/// measured in the outgoing frame — a scanned environment above all — now describes the
		/// room in the wrong place and has to be dropped.
		///
		/// Deliberately not raised for a map created in place: that map adopts the frame the
		/// device is already in, so nothing moves.
		/// </summary>
		public event Action WorldFrameRebased = delegate { };

		private MapObjectDirector objects;
		private MapColocationAdapter colocation;
		private MapSessionSync sessionSync;
		private MapDiscovery discovery;
		private MapAutosave autosave;

		private CancellationTokenSource lifetimeCtknSrc;
		private int frameRebasedOn = -1;

		internal MapObjectDirector Objects => objects;
		internal MapColocationAdapter Colocation => colocation;

		public bool IsChangingMap => sessionSync.IsChangingMap;
		public event Action ChangingMapChanged
		{
			add => sessionSync.ChangingMapChanged += value;
			remove => sessionSync.ChangingMapChanged -= value;
		}

		public IReadOnlyDictionary<string, int> ProbeResults => discovery.Results;

		/// <summary>
		/// Whether a saved map's references were found in the space this device is standing in.
		/// The one rule for "belongs here": the map list hides what it answers
		/// <see cref="MapPresence.Elsewhere"/> for, and the startup auto-load only takes a map it
		/// answers <see cref="MapPresence.Here"/> for.
		/// </summary>
		public MapPresence GetMapPresence(string id) => discovery.GetPresence(id);
		public event Action ProbeResultsChanged
		{
			add => discovery.ResultsChanged += value;
			remove => discovery.ResultsChanged -= value;
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogError("A second MapManager is in the scene; destroying the duplicate.", this);
				Destroy(this);
				return;
			}

			Instance = this;
			lifetimeCtknSrc = new CancellationTokenSource();

			// ColocationManager wakes first (-150) and owns the provider references, so the map
			// layer takes them from it rather than keeping a second set of its own to drift.
			if (!colocationManager)
				colocationManager = FindFirstObjectByType<ColocationManager>();

			if (!colocationManager)
				Debug.LogError("MapManager found no ColocationManager; maps cannot colocate.", this);

			if (!objectDatabase)
				Debug.LogError("MapManager has no map object database.", this);

			autosave = new MapAutosave(saveDebounceSeconds, SaveCurrentMap);
			objects = new MapObjectDirector(objectDatabase, MarkMapContentChanged);
			colocation = new MapColocationAdapter(
				colocationManager, () => CurrentMap, MapIsSessionMap, autosave.Schedule);
			sessionSync = new MapSessionSync(this, switchTimeoutSeconds);
			discovery = new MapDiscovery(
				colocationManager != null ? colocationManager.AnchorProvider : null,
				probeTimeoutSeconds);

			objects.Register();
			colocation.Register();
			sessionSync.Register();

			SyncBus.Deactivated += OnBusDeactivated;
			MapObject.LocalEditOccurred += MarkMapContentChanged;
			MapObject.Added += OnMapObjectChanged;
			MapObject.Removed += OnMapObjectChanged;
		}

		private void Start()
		{
			if (discovery.IsAvailable)
				StartupProbe(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			// The duplicate rejected in Awake registered nothing; unwinding here would tear
			// down the live instance's subscriptions.
			if (Instance != this)
				return;

			Instance = null;

			if (CurrentMap != null)
				MapStore.Save(CurrentMap);

			MapObject.Removed -= OnMapObjectChanged;
			MapObject.Added -= OnMapObjectChanged;
			MapObject.LocalEditOccurred -= MarkMapContentChanged;
			SyncBus.Deactivated -= OnBusDeactivated;

			sessionSync.Unregister();
			colocation.Unregister();
			objects.Unregister();
			autosave.Dispose();

			lifetimeCtknSrc?.Cancel();
			lifetimeCtknSrc?.Dispose();
			lifetimeCtknSrc = null;
		}

		private void OnApplicationQuit()
		{
			objects.Snapshot(CurrentMap);
			objects.MarkQuitting();
			SaveCurrentMap();
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
				SaveCurrentMap();
		}

		private void LateUpdate()
		{
			colocation.ApplyPendingSnapshots();
			sessionSync.TickMapChange(CheckFrameAgreement());
		}

		// ------- world-frame trust -------------------------------

		public bool CheckWorldFrameIsTrusted()
		{
			// Every peer distrusts the frame for the whole of a map change. Until the incoming
			// references have been fitted, world space still describes the map being left, and
			// anything that writes durable world-space data would write it in the wrong frame.
			if (sessionSync.IsChangingMap)
				return false;

			return CheckFrameAgreement();
		}

		private bool CheckFrameAgreement()
		{
			if (CurrentMap == null)
				return false;

			// Discards the agreement measured against references that are being replaced. Without
			// this, a check running later in the same frame as a swap answers from the outgoing
			// map's fit — which is exactly when a map change asks whether it can stop holding.
			if (Time.frameCount == frameRebasedOn)
				return false;

			if (!colocationManager)
				return false;

			// Nothing the active provider can realize means there is nothing to check the frame
			// against, so the frame the device is standing in is this map's frame.
			int realizable = colocationManager.CountRealizableReferences(CurrentMap);
			if (realizable == 0)
				return true;

			if (!ColocationManager.IsColocated)
				return false;

			// Two references pin a frame; asking for more would stall a map that only ever had
			// one. Never zero: a map held up by nothing would report whichever frame the device
			// happens to be standing in as trustworthy, and mint anchors into it.
			int required = Mathf.Min(realizable, 2);
			FitAgreement agreement = colocationManager.Agreement;

			return agreement.agreeingCount >= required && agreement.meanAgrees;
		}

		/// <summary>
		/// Whether the colocation providers hold this map because the session published it, rather
		/// than because this joiner had it loaded before it arrived.
		/// </summary>
		private bool MapIsSessionMap(GameMap map)
		{
			if (!SyncBus.Active || SyncBus.IsAuthority)
				return true;

			return sessionSync.SessionMapAdopted && map != null &&
			       map.id == sessionSync.SessionMapId;
		}

		// ------- current map lifecycle ---------------------------

		public bool LoadMap(string id)
		{
			if (SyncBus.Active)
			{
				Debug.LogWarning("Cannot load a map while in a session.");
				return false;
			}

			if (!MapStore.TryGet(id, out GameMap map))
				return false;

			UnloadCurrentMap();
			RebaseOnto(map);
			MapStore.MarkUsed(map);
			objects.Instantiate(map);
			colocation.Inject(map);
			CurrentMapChanged.Invoke(map);
			return true;
		}

		/// <summary>
		/// Loads a map offline, or changes the session's map in place while hosting one.
		/// </summary>
		public bool ChangeMap(string id) => SyncBus.Active ? SwitchMap(id) : LoadMap(id);

		/// <summary>
		/// Why <see cref="ChangeMap"/> would refuse right now, or null if it would proceed. The
		/// UI uses this to disable and explain the control rather than duplicate the rules.
		/// </summary>
		public string DescribeChangeBlocker(string id)
		{
			if (!MapStore.TryGet(id, out GameMap map))
				return "Map is missing";
			if (CurrentMap != null && CurrentMap.id == id)
				return "Already loaded";

			if (!SyncBus.Active)
				return null;

			if (!SyncBus.IsAuthority)
				return "Only the host can change the map";
			if (MatchReferee.State == MatchState.Playing ||
			    MatchReferee.State == MatchState.Countdown)
				return "Not during a round";

			// Tag mode has no provider to select for a map with no registered tags, so switching
			// to one would end colocation for the whole session.
			if (colocationManager != null &&
			    colocationManager.Method == ColocationManager.ColocationMethod.AprilTag &&
			    !map.HasTags)
				return "Session uses tags; this map has none";

			return null;
		}

		/// <summary>
		/// Changes the session's map without ending the session. Every peer re-aligns onto the
		/// incoming map's references, so this is only for another map of the same physical room:
		/// the world frame shifts by whatever rigid transform separates the two maps' canon
		/// frames, and the session holds until that fit lands.
		///
		/// Order is the contract. The providers' constraints are broadcast before the identity
		/// that commits the change, and SyncBus applies every endpoint in the authority's own
		/// order, so a peer reacting to the new identity already holds the references it names.
		///
		/// A change already in flight is superseded rather than refused. The hold is exactly when
		/// a host discovers the incoming map's references will never localize here, and that is
		/// the moment they need to pick a different map.
		/// </summary>
		public bool SwitchMap(string id)
		{
			string blocker = DescribeChangeBlocker(id);
			if (blocker != null)
			{
				Debug.LogWarning($"Cannot change map: {blocker}.");
				return false;
			}

			if (!SyncBus.Active)
				return LoadMap(id);

			if (!MapStore.TryGet(id, out GameMap target))
				return false;

			// A map still being aligned to has nothing worth recording: its objects and references
			// were injected moments ago, and every world pose since is measured in a frame that was
			// never fitted. Saving would write that frame into the map being abandoned.
			if (!sessionSync.IsChangingMap)
				SaveCurrentMap();

			sessionSync.BeginMapChange();
			objects.RemoveAll();

			RebaseOnto(target);
			target.lastUsed = DateTime.UtcNow.Ticks;

			colocation.Inject(target);
			objects.Instantiate(target);

			// The outgoing objects are destroyed but not collected until the end of the frame, so
			// MapObject.All still lists them and a snapshot now would record the wrong world. The
			// spawns and despawns schedule a save that records what actually resulted; this call
			// is here for the version commit and the identity publish.
			SaveWithoutObjectSnapshot();

			CurrentMapChanged.Invoke(target);
			return true;
		}

		public void UnloadCurrentMap()
		{
			if (CurrentMap != null)
				SaveCurrentMap();

			CurrentMap = null;
			colocation.ClearPendingSnapshots();
			colocation.ClearForNoMap();
			objects.RemoveAll();
			CurrentMapChanged.Invoke(null);
		}

		public void DeleteMap(string id)
		{
			if (SyncBus.Active && CurrentMap != null && CurrentMap.id == id)
			{
				Debug.LogWarning("Cannot delete the session's map while in a session.");
				return;
			}

			if (CurrentMap != null && CurrentMap.id == id)
				UnloadCurrentMap();

			List<string> orphanedAnchors = new();
			MapStore.Delete(id, orphanedAnchors);
			discovery.Forget(id);

			foreach (string orphan in orphanedAnchors)
				colocation.EraseAnchorSave(orphan);
		}

		/// <summary>
		/// The current map, minting one if this device is allowed to. A joiner that has not yet
		/// adopted the session's map must not: its copy would be replaced moments later, and
		/// injecting it clears the tag anchors this device has already realized.
		/// </summary>
		internal GameMap EnsureCurrentMap()
		{
			if (CurrentMap != null)
				return CurrentMap;

			if (SyncBus.Active && !SyncBus.IsAuthority)
				return null;

			CurrentMap = MapStore.CreateNew();
			colocation.ClearPendingSnapshots();
			colocation.Inject(CurrentMap);
			CurrentMapChanged.Invoke(CurrentMap);
			return CurrentMap;
		}

		/// <summary>
		/// Adopts a map's frame as this device's world frame. Every pose measured in the outgoing
		/// frame is invalidated, and agreement is not answered again until the next frame.
		/// </summary>
		internal void RebaseOnto(GameMap map)
		{
			CurrentMap = map;
			frameRebasedOn = Time.frameCount;
			colocation.ClearPendingSnapshots();
			WorldFrameRebased.Invoke();
		}

		internal void SetCurrentMapSilently(GameMap map) => CurrentMap = map;
		internal void RaiseCurrentMapChanged() => CurrentMapChanged.Invoke(CurrentMap);

		// ------- persistence -------------------------------------

		private void MarkMapContentChanged()
		{
			if (EnsureCurrentMap() == null)
				return;

			// Dirty means "diverged from the version the authority published", which drives
			// fork-on-conflict. A client's in-session edits are replicated rather than divergent,
			// and adoption discards any version they mint.
			if (!SyncBus.Active || SyncBus.IsAuthority)
				MapStore.MarkEdited(CurrentMap);

			autosave.Schedule();
		}

		// Map objects arrive and leave without a local edit — a peer placed one, or a map
		// finished loading. Keeping the map's object list current as they do is what stops
		// session teardown from being the only chance to record them.
		private void OnMapObjectChanged(MapObject _) => autosave.Schedule();

		public void SaveCurrentMap()
		{
			if (CurrentMap == null)
				return;

			if (objects.CanSnapshot)
				objects.Snapshot(CurrentMap);

			SaveWithoutObjectSnapshot();
		}

		private void SaveWithoutObjectSnapshot()
		{
			if (CurrentMap == null)
				return;

			if (SyncBus.Active && SyncBus.IsAuthority)
			{
				// Settle the version before the identity that advertises it goes out. Only a
				// diverged map earns a new one; offline saves deliberately stay dirty and keep
				// their fork-on-conflict semantics.
				if (CurrentMap.dirty)
					MapStore.MintVersion(CurrentMap);

				// Unconditional. This is the only signal that tells a joiner which map the session
				// is using, and nothing starts its adoption without it — so a host whose map was
				// never edited has to publish too.
				sessionSync.PublishIdentity();
				CurrentMap.baseVersion = CurrentMap.version;
				CurrentMap.dirty = false;
			}

			MapStore.Save(CurrentMap);
		}

		private void OnBusDeactivated()
		{
			SaveCurrentMap();

			if (CurrentMap != null)
				colocation.Inject(CurrentMap);

			RebuildLocalObjectsAfterSession();
		}

		/// <summary>
		/// Re-creates the map's objects as plain local ones once the session's network objects
		/// have finished despawning.
		/// </summary>
		private async void RebuildLocalObjectsAfterSession()
		{
			if (lifetimeCtknSrc == null)
				return;

			try
			{
				await Awaitable.NextFrameAsync(lifetimeCtknSrc.Token);
				await Awaitable.NextFrameAsync(lifetimeCtknSrc.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (CurrentMap == null || SyncBus.Active)
				return;

			objects.RemoveAll();
			objects.Instantiate(CurrentMap);
		}

		// ------- map objects -------------------------------------

		public void RequestPlaceObject(MapObject prefab, Vector3 position, Quaternion rotation) =>
			objects.RequestPlace(prefab, position, rotation);

		public void RequestRemoveObject(MapObject obj) => objects.RequestRemove(obj);

		// ------- tag authoring -----------------------------------

		public bool RegisterTag(int tagId, Pose worldPose)
		{
			if (SyncBus.Active)
				return false;

			if (EnsureCurrentMap() == null)
				return false;

			if (!CheckWorldFrameIsTrusted())
				return false;

			CurrentMap.SetTag(tagId, worldPose);
			MapStore.MarkEdited(CurrentMap);
			colocation.InjectTags(CurrentMap);
			SaveCurrentMap();
			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		public bool UnregisterTag(int tagId)
		{
			if (SyncBus.Active || CurrentMap == null)
				return false;

			if (CurrentMap.tags.RemoveAll(entry => entry.id == tagId) == 0)
				return false;

			// Collected before the prune, erased after it: the orphan check has to run against
			// the map that no longer lists them.
			List<string> dropped = new();
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (entry.tagId == tagId)
					dropped.Add(entry.guid);

			CurrentMap.anchors.RemoveAll(entry => entry.tagId == tagId);

			foreach (string guid in dropped)
				colocation.EraseTagAnchorSaveIfOrphaned(guid);

			MapStore.MarkEdited(CurrentMap);
			colocation.Inject(CurrentMap);
			SaveCurrentMap();
			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		// ------- discovery ---------------------------------------

		private async void StartupProbe(CancellationToken ctkn)
		{
			try
			{
				await Awaitable.WaitForSecondsAsync(5f, ctkn);
				await ProbeAndAutoLoad(ctkn);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		/// <summary>Loads the most recently used map that localizes in this physical space.</summary>
		public async Awaitable ProbeAndAutoLoad(CancellationToken ctkn = default)
		{
			GameMap found = await discovery.ProbeAsync(stopAtFirstLocalized: true, ctkn);

			if (found != null && CurrentMap == null && !SyncBus.Active)
				LoadMap(found.id);
		}

		/// <summary>Refreshes <see cref="ProbeResults"/> for every saved map.</summary>
		public async Awaitable ProbeAllMaps(CancellationToken ctkn = default)
		{
			await discovery.ProbeAsync(stopAtFirstLocalized: false, ctkn);
		}
	}
}
