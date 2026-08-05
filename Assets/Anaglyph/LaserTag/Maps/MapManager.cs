using Anaglyph.Debugging.Visuals;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// The session's map identity. Reference providers synchronize their own state, and map
	/// objects replicate through NGO, so only identity belongs here.
	/// </summary>
	public struct MapIdentity
	{
		public Guid id;
		public Guid version;
		public FixedString64Bytes name;
	}

	/// <summary>
	/// Persists Lasertag maps and adapts their saved reference records to the reusable
	/// XRTemplate reference providers. Providers own anchor operations, synchronization, and
	/// colocation behavior; this class only imports a loaded map and snapshots provider state
	/// back into that map.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MapManager : MonoBehaviour
	{
		public static MapManager Instance { get; private set; }

		[SerializeField] private SpatialAnchorConstraintProvider spatialAnchorProvider;
		[SerializeField] private TagConstraintProvider tagProvider;
		[SerializeField] private Colocator colocator;

		[Tooltip("Every placeable map object; also how a saved map's prefab ids resolve")]
		[SerializeField] private MapObjectDatabase objectDatabase;

		[Tooltip("How long the room probe lets each anchor try to localize")]
		[SerializeField] private float probeTimeoutSeconds = 8f;

		[Tooltip("Reference error above which a constraint does not count as agreeing with the fit")]
		[SerializeField] private float agreementMaxError = 0.3f;

		[Tooltip("How long an edit or provider change waits before it reaches disk")]
		[SerializeField] private float saveDebounceSeconds = 2f;

		[Tooltip("How long a map change waits for the new references to align before giving up")]
		[SerializeField] private float switchTimeoutSeconds = 20f;

		public GameMap CurrentMap { get; private set; }
		public event Action<GameMap> CurrentMapChanged = delegate { };

		/// <summary>Whether the session is mid-map-change, and so has no trustworthy frame.</summary>
		public bool IsChangingMap => mapChanging.Value;
		public event Action ChangingMapChanged = delegate { };

		public IReadOnlyDictionary<string, int> ProbeResults => probeResults;
		private readonly Dictionary<string, int> probeResults = new();
		public event Action ProbeResultsChanged = delegate { };

		private readonly SyncVariable<MapIdentity> mapIdentity = new("map.identity");
		private readonly SyncVariable<bool> mapChanging = new("map.changing");
		private readonly SyncEvent<MapObjectPlacement> placeRequest =
			new("map.object.place", EventRoute.ToAuthority);
		private readonly SyncEvent<ulong> removeRequest =
			new("map.object.remove", EventRoute.ToAuthority);

		private CancellationTokenSource lifetimeCtknSrc;
		private bool savePending;
		private bool quietSavePending;
		private bool frameContinuous;
		private bool isQuitting;

		private int agreeingReferenceCount;
		private float meanReferenceError;
		private readonly List<ColocationConstraint> referenceScratch = new();
		private readonly List<MapObject> objectRemovalScratch = new();

		private bool anchorSnapshotPending;
		private bool tagSnapshotPending;
		private bool taggedAnchorSnapshotPending;

		private bool authoritySessionStarted;
		private bool sessionMapAdopted;
		private float mapChangeStartedTime;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogError("A second MapManager is in the scene; destroying the duplicate. " +
					"Its map identity endpoint would displace the live one's.", this);
				Destroy(this);
				return;
			}

			Instance = this;
			lifetimeCtknSrc = new CancellationTokenSource();

			if (!spatialAnchorProvider)
				spatialAnchorProvider = FindFirstObjectByType<SpatialAnchorConstraintProvider>();
			if (!tagProvider)
				tagProvider = FindFirstObjectByType<TagConstraintProvider>();
			if (!colocator)
				colocator = FindFirstObjectByType<Colocator>();

			if (!objectDatabase)
				Debug.LogError("MapManager has no map object database.", this);

			mapIdentity.Register();
			mapIdentity.Changed += OnMapIdentityChanged;
			mapIdentity.Synced += OnMapIdentitySynced;

			mapChanging.Register();
			mapChanging.Changed += OnMapChangingChanged;

			placeRequest.Register();
			placeRequest.Received += OnPlaceRequested;
			removeRequest.Register();
			removeRequest.Received += OnRemoveRequested;

			if (spatialAnchorProvider)
			{
				spatialAnchorProvider.ConstraintsChanged += OnSpatialAnchorConstraintsChanged;
				spatialAnchorProvider.AnchorPersisted += OnSpatialAnchorPersisted;
			}

			if (tagProvider)
			{
				tagProvider.TagsChanged += OnTagReferencesChanged;
				tagProvider.AnchorsChanged += OnTaggedAnchorsChanged;
			}

			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MapObject.LocalEditOccurred += OnLocalEdit;
			MapObject.Added += OnMapObjectAdded;
			MapObject.Removed += OnMapObjectRemoved;
			MainXRRig.Recentered += OnRecentered;
		}

		private void Start()
		{
			if (spatialAnchorProvider && spatialAnchorProvider.IsAvailable)
				StartupProbe(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			// The duplicate rejected in Awake registered nothing; unwinding here would tear
			// down the live instance's subscriptions.
			if (Instance != this)
				return;

			Instance = null;

			SaveCurrentMapQuietly();

			MainXRRig.Recentered -= OnRecentered;
			MapObject.Removed -= OnMapObjectRemoved;
			MapObject.Added -= OnMapObjectAdded;
			MapObject.LocalEditOccurred -= OnLocalEdit;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= OnBusDeactivated;

			if (tagProvider)
			{
				tagProvider.AnchorsChanged -= OnTaggedAnchorsChanged;
				tagProvider.TagsChanged -= OnTagReferencesChanged;
			}

			if (spatialAnchorProvider)
			{
				spatialAnchorProvider.AnchorPersisted -= OnSpatialAnchorPersisted;
				spatialAnchorProvider.ConstraintsChanged -= OnSpatialAnchorConstraintsChanged;
			}

			removeRequest.Received -= OnRemoveRequested;
			removeRequest.Unregister();
			placeRequest.Received -= OnPlaceRequested;
			placeRequest.Unregister();

			mapChanging.Changed -= OnMapChangingChanged;
			mapChanging.Unregister();

			mapIdentity.Synced -= OnMapIdentitySynced;
			mapIdentity.Changed -= OnMapIdentityChanged;
			mapIdentity.Unregister();

			lifetimeCtknSrc?.Cancel();
			lifetimeCtknSrc?.Dispose();
			lifetimeCtknSrc = null;
		}

		private void OnApplicationQuit()
		{
			// Map objects are still alive during OnApplicationQuit, so this is the last save
			// that can record them; every save after it runs against a world being torn down.
			isQuitting = true;
			SaveCurrentMap(snapshotObjects: true);
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
				SaveCurrentMap();
		}

		private void OnApplicationFocus(bool focused)
		{
			if (!focused)
				frameContinuous = false;
		}

		private void OnRecentered()
		{
			frameContinuous = false;
		}

		private void Update()
		{
			UpdateAgreement();

			if (!AnaglyphDebugging.DebugMode || CurrentMap == null)
				return;

			foreach (MapAnchorEntry anchor in CurrentMap.anchors)
				DebugAxisVisual.DrawDebugAxis(
					anchor.canonPose.position, anchor.canonPose.rotation, Color.cyan);

			foreach (MapTagEntry mapTag in CurrentMap.tags)
				DebugAxisVisual.DrawDebugAxis(
					mapTag.canonPose.position, mapTag.canonPose.rotation, Color.magenta);
		}

		private void LateUpdate()
		{
			ApplyPendingProviderSnapshots();
			UpdateMapChange();
		}

		// ------- world-frame trust -------------------------------

		public bool CheckWorldFrameIsTrusted()
		{
			// Every peer distrusts the frame for the whole of a map change. Until the incoming
			// references have been fitted, world space still describes the map being left, and
			// anything that writes durable world-space data would write it in the wrong frame.
			if (mapChanging.Value)
				return false;

			return CheckFrameAgreement();
		}

		private bool CheckFrameAgreement()
		{
			if (CurrentMap == null)
				return false;
			if (frameContinuous)
				return true;
			if (CurrentMap.anchors.Count == 0 && CurrentMap.tags.Count == 0)
				return true;
			if (!ColocationManager.IsColocated)
				return false;

			// Tag mode can only realize this device's tag-backed anchors. Roaming anchors
			// remain in the map for shared-anchor mode, but must not raise the agreement
			// threshold for a provider that can never expose them.
			bool usingTagProvider = ColocationManager.Instance != null &&
			                        ColocationManager.Instance.UsingTagProvider;
			int availableAnchorCount = 0;
			foreach (MapAnchorEntry anchor in CurrentMap.anchors)
				if (!usingTagProvider || anchor.tagId >= 0)
					availableAnchorCount++;

			// Never zero. A map that references anything at all has to be held up by at least
			// one agreeing constraint — with a floor of zero, a device that has realized none
			// of a tag map's anchors yet would report the frame it happens to be standing in
			// as trustworthy, and mint anchors into the map against it.
			int required = Mathf.Clamp(availableAnchorCount, 1, 2);
			return agreeingReferenceCount >= required &&
			       meanReferenceError <= agreementMaxError;
		}

		private void UpdateAgreement()
		{
			referenceScratch.Clear();

			if (colocator)
				colocator.GetCurrentConstraints(referenceScratch);

			if (referenceScratch.Count == 0)
			{
				agreeingReferenceCount = 0;
				meanReferenceError = 0f;
				return;
			}

			// Agreement is per constraint: a reference agrees only if the fit lands it on its own
			// canon pose. Counting the available constraints instead would let a single reference
			// that is metres out hide inside a mean taken over many good ones.
			int agreeing = 0;
			float errorSum = 0f;
			foreach (ColocationConstraint reference in referenceScratch)
			{
				float error = Vector3.Distance(
					reference.observed.position, reference.canon.position);

				errorSum += error;
				if (error <= agreementMaxError)
					agreeing++;
			}

			agreeingReferenceCount = agreeing;
			meanReferenceError = errorSum / referenceScratch.Count;
		}

		/// <summary>
		/// Discards the agreement measured against references that are being replaced. Without
		/// this, a check running later in the same frame as the swap answers from the outgoing
		/// map's fit — which is exactly when a map change asks whether it can stop holding.
		/// </summary>
		private void InvalidateFrameAgreement()
		{
			agreeingReferenceCount = 0;
			meanReferenceError = float.MaxValue;
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
			CurrentMap = map;
			frameContinuous = false;
			ClearPendingProviderSnapshots();
			MapStore.MarkUsed(map);
			InstantiateMapObjects(map);
			InjectMapIntoProviders(map);
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
			if (mapChanging.Value)
				return "Already changing map";
			if (MatchReferee.State == MatchState.Playing ||
			    MatchReferee.State == MatchState.Countdown)
				return "Not during a round";

			// Tag mode has no provider to select for a map with no registered tags, so
			// switching to one would end colocation for the whole session.
			if (ColocationManager.Instance != null &&
			    ColocationManager.Instance.Method == ColocationManager.ColocationMethod.AprilTag &&
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

			SaveCurrentMap();

			mapChanging.Value = true;
			mapChangeStartedTime = Time.time;

			RemoveMapObjects();

			CurrentMap = target;
			frameContinuous = false;
			ClearPendingProviderSnapshots();
			InvalidateFrameAgreement();
			target.lastUsed = DateTime.UtcNow.Ticks;

			InjectMapIntoProviders(target);
			InstantiateMapObjects(target);

			// Deliberately without an object snapshot: the outgoing objects are destroyed but
			// not collected until the end of the frame, so MapObject.All still lists them. The
			// debounced save that the spawns and despawns schedule records what actually
			// resulted. This call is here for the version commit and the identity publish.
			SaveCurrentMap(snapshotObjects: false);

			CurrentMapChanged.Invoke(target);
			return true;
		}

		private void UpdateMapChange()
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !mapChanging.Value)
				return;

			if (CheckFrameAgreement())
			{
				mapChanging.Value = false;
				return;
			}

			if (Time.time - mapChangeStartedTime < switchTimeoutSeconds)
				return;

			// Releasing the hold is not a claim that the frame is good — CheckWorldFrameIsTrusted
			// still fails on its own terms, and everything gated on it stays gated. It stops the
			// session sitting in a state it has no way out of.
			Debug.LogWarning($"Map change did not align within {switchTimeoutSeconds}s. " +
				"Releasing the hold; the world frame remains untrusted.");
			mapChanging.Value = false;
		}

		private void OnMapChangingChanged(bool _, bool __) => ChangingMapChanged.Invoke();

		public void UnloadCurrentMap()
		{
			if (CurrentMap != null)
				SaveCurrentMap();

			CurrentMap = null;
			frameContinuous = false;
			ClearPendingProviderSnapshots();
			ClearProviderStateForNoMap();
			RemoveMapObjects();
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
			probeResults.Remove(id);
			ProbeResultsChanged.Invoke();

			if (spatialAnchorProvider)
				foreach (string orphan in orphanedAnchors)
					if (TryGuidFromString(orphan, out Guid guid))
						_ = spatialAnchorProvider.EraseAsync(guid);
		}

		/// <summary>
		/// The current map, minting one if this device is allowed to. A joiner that has not yet
		/// adopted the session's map must not: its copy would be replaced moments later, and
		/// injecting it clears the tag anchors this device has already realized.
		/// </summary>
		private GameMap EnsureCurrentMap()
		{
			if (CurrentMap != null)
				return CurrentMap;

			if (SyncBus.Active && !SyncBus.IsAuthority)
				return null;

			CurrentMap = MapStore.CreateNew();
			frameContinuous = true;
			ClearPendingProviderSnapshots();
			InjectMapIntoProviders(CurrentMap);
			CurrentMapChanged.Invoke(CurrentMap);
			return CurrentMap;
		}

		private void OnLocalEdit() => MarkMapContentChanged();

		private void MarkMapContentChanged()
		{
			if (EnsureCurrentMap() == null)
				return;

			// Dirty means "diverged from the version the authority published", which drives
			// fork-on-conflict. A client's in-session edits are replicated rather than
			// divergent, and TryAdopt discards any version they mint.
			if (!SyncBus.Active || SyncBus.IsAuthority)
				MapStore.MarkEdited(CurrentMap);

			ScheduleSave();
		}

		// Map objects arrive and leave without a local edit — a peer placed one, or a map
		// finished loading. Keeping the map's object list current as they do is what stops
		// session teardown from being the only chance to record them.
		private void OnMapObjectAdded(MapObject _) => ScheduleQuietSave();
		private void OnMapObjectRemoved(MapObject _) => ScheduleQuietSave();

		private async void ScheduleSave()
		{
			if (savePending || lifetimeCtknSrc == null) return;
			savePending = true;

			try
			{
				await Awaitable.WaitForSecondsAsync(saveDebounceSeconds, lifetimeCtknSrc.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			finally
			{
				savePending = false;
			}

			SaveCurrentMap();
		}

		private async void ScheduleQuietSave()
		{
			if (quietSavePending || lifetimeCtknSrc == null) return;
			quietSavePending = true;

			try
			{
				await Awaitable.WaitForSecondsAsync(saveDebounceSeconds, lifetimeCtknSrc.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}
			finally
			{
				quietSavePending = false;
			}

			if (CurrentMap == null)
				return;

			if (CanSnapshotObjects)
				SnapshotObjects(CurrentMap);

			MapStore.Save(CurrentMap);
		}

		public void SaveCurrentMap() => SaveCurrentMap(CanSnapshotObjects);

		private void SaveCurrentMap(bool snapshotObjects)
		{
			if (CurrentMap == null)
				return;

			if (snapshotObjects)
				SnapshotObjects(CurrentMap);

			if (SyncBus.Active && SyncBus.IsAuthority)
			{
				// MapStore owns content-version generation. Commit the dirty state first so
				// the identity advertises that newly minted version, then persist the final
				// clean/base state below. Offline saves deliberately remain dirty and keep
				// their existing fork-on-conflict semantics.
				if (CurrentMap.dirty)
					MapStore.Save(CurrentMap);

				PublishIdentity();
				CurrentMap.baseVersion = CurrentMap.version;
				CurrentMap.dirty = false;
			}

			MapStore.Save(CurrentMap);
		}

		private void SaveCurrentMapQuietly()
		{
			if (CurrentMap != null)
				MapStore.Save(CurrentMap);
		}

		/// <summary>
		/// Whether <see cref="MapObject.All"/> currently describes the world. It does not while
		/// the app is quitting or a network shutdown is despawning objects: the list empties
		/// through teardown rather than through an edit, and snapshotting it then would persist
		/// a map with no objects in it.
		/// </summary>
		private bool CanSnapshotObjects
		{
			get
			{
				if (isQuitting)
					return false;

				NetworkManager manager = NetworkManager.Singleton;
				return manager == null || !manager.ShutdownInProgress;
			}
		}

		private static void SnapshotObjects(GameMap map)
		{
			map.objects.Clear();
			foreach (MapObject obj in MapObject.All)
			{
				if (!obj)
					continue;
				if (string.IsNullOrEmpty(obj.PrefabId))
				{
					Debug.LogWarning($"Map object '{obj.name}' has no prefab id; not saving it.");
					continue;
				}

				Transform transform = obj.transform;
				map.objects.Add(new MapObjectEntry
				{
					prefabId = obj.PrefabId,
					pose = new Pose(transform.position, transform.rotation),
				});
			}
		}

		private void InstantiateMapObjects(GameMap map)
		{
			foreach (MapObjectEntry entry in map.objects)
			{
				MapObject prefab = objectDatabase != null
					? objectDatabase.FindPrefab(entry.prefabId)
					: null;

				if (prefab == null)
				{
					Debug.LogWarning($"Map references unknown prefab '{entry.prefabId}'.");
					continue;
				}

				Instantiate(prefab.gameObject, entry.pose.position, entry.pose.rotation);
			}
		}

		private void RemoveMapObjects()
		{
			objectRemovalScratch.Clear();
			objectRemovalScratch.AddRange(MapObject.All);

			foreach (MapObject obj in objectRemovalScratch)
				if (obj)
					obj.RemoveIfPermitted();

			objectRemovalScratch.Clear();
		}

		// ------- map object placement ----------------------------

		private struct MapObjectPlacement
		{
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		/// <summary>
		/// Places a map object. Only the session authority spawns them — that is what lets one
		/// peer clear and repopulate the world when the map changes — so in a session this is a
		/// request, and offline it is a plain instantiate.
		/// </summary>
		public void RequestPlaceObject(MapObject prefab, Vector3 position, Quaternion rotation)
		{
			if (prefab == null)
				return;

			if (!SyncBus.Active)
			{
				Instantiate(prefab.gameObject, position, rotation);
				return;
			}

			if (string.IsNullOrEmpty(prefab.PrefabId))
			{
				Debug.LogError($"Map object prefab '{prefab.name}' has no prefab id, so no peer " +
					"can resolve it.", prefab);
				return;
			}

			MapObjectPlacement placement = new() { pose = new Pose(position, rotation) };
			placement.prefabId.CopyFromTruncated(prefab.PrefabId);
			placeRequest.Raise(placement);
		}

		/// <summary>Removes a map object, through the authority while a session is up.</summary>
		public void RequestRemoveObject(MapObject obj)
		{
			if (obj == null)
				return;

			// Local-only leftovers never reached the network, so no peer has to be told.
			if (!SyncBus.Active || !obj.NetworkObject.IsSpawned)
			{
				Destroy(obj.gameObject);
				return;
			}

			removeRequest.Raise(obj.NetworkObject.NetworkObjectId);
		}

		private void OnPlaceRequested(ulong sender, MapObjectPlacement placement)
		{
			if (objectDatabase == null || NetworkManager.Singleton == null)
				return;

			string prefabId = placement.prefabId.ToString();
			MapObject prefab = objectDatabase.FindPrefab(prefabId);
			if (prefab == null)
			{
				Debug.LogWarning($"Peer {sender} asked to place unknown prefab '{prefabId}'.");
				return;
			}

			NetworkObject.InstantiateAndSpawn(prefab.gameObject, NetworkManager.Singleton,
				ownerClientId: SyncBus.LocalClientId,
				position: placement.pose.position, rotation: placement.pose.rotation);

			MarkMapContentChanged();
		}

		private void OnRemoveRequested(ulong sender, ulong networkObjectId)
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || manager.SpawnManager == null)
				return;

			if (!manager.SpawnManager.SpawnedObjects.TryGetValue(
				    networkObjectId, out NetworkObject spawned))
				return;

			// Only map objects are removable this way; the id arrives from a peer and would
			// otherwise be a despawn primitive for any NetworkObject in the session.
			if (!spawned.TryGetComponent(out MapObject mapObject))
			{
				Debug.LogWarning($"Peer {sender} asked to remove a non-map object.");
				return;
			}

			mapObject.RemoveIfPermitted();
			MarkMapContentChanged();
		}

		// ------- provider persistence adapter --------------------

		private void InjectMapIntoProviders(GameMap map)
		{
			List<AnchorConstraintData> anchors = new(map.anchors.Count);
			List<TaggedAnchorConstraintData> taggedAnchors = new();
			List<TagConstraintData> tags = new(map.tags.Count);

			foreach (MapAnchorEntry entry in map.anchors)
			{
				if (!TryGuidFromString(entry.guid, out Guid guid))
					continue;

				anchors.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));
				if (entry.tagId >= 0)
					taggedAnchors.Add(new TaggedAnchorConstraintData(
						guid, entry.tagId, entry.canonPose));
			}

			foreach (MapTagEntry entry in map.tags)
				tags.Add(new TagConstraintData(entry.id, entry.canonPose));

			if (spatialAnchorProvider)
				spatialAnchorProvider.SetConstraints(anchors);
			if (tagProvider)
				tagProvider.SetConstraints(tags, taggedAnchors);
		}

		private void InjectAnchorsIntoAnchorProvider()
		{
			if (CurrentMap == null || !spatialAnchorProvider)
				return;

			List<AnchorConstraintData> anchors = new(CurrentMap.anchors.Count);
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (TryGuidFromString(entry.guid, out Guid guid))
					anchors.Add(new AnchorConstraintData(guid, entry.canonPose, entry.tagId));

			spatialAnchorProvider.SetConstraints(anchors);
		}

		private void InjectTagsIntoProvider()
		{
			if (CurrentMap == null || !tagProvider)
				return;

			List<TagConstraintData> tags = new(CurrentMap.tags.Count);
			foreach (MapTagEntry entry in CurrentMap.tags)
				tags.Add(new TagConstraintData(entry.id, entry.canonPose));

			tagProvider.SetRegisteredTags(tags);
		}

		private void ClearProviderStateForNoMap()
		{
			if (SyncBus.IsAuthority)
			{
				if (spatialAnchorProvider)
					spatialAnchorProvider.SetConstraints(Array.Empty<AnchorConstraintData>());
				if (tagProvider)
					tagProvider.SetConstraints(Array.Empty<TagConstraintData>(),
						Array.Empty<TaggedAnchorConstraintData>());
			}
			else if (tagProvider)
			{
				tagProvider.SetLocalAnchors(Array.Empty<TaggedAnchorConstraintData>());
			}
		}

		private bool AnchorProviderOwnsCurrentState => CurrentMap != null &&
			SessionReferencesBelongToCurrentMap &&
			(SyncBus.Active
				? ColocationManager.Instance != null &&
				  ColocationManager.Instance.Method == ColocationManager.ColocationMethod.MetaSharedAnchor
				: spatialAnchorProvider && spatialAnchorProvider.IsRunning);

		private bool TagProviderOwnsCurrentState => CurrentMap != null &&
			SessionReferencesBelongToCurrentMap &&
			(SyncBus.Active
				? ColocationManager.Instance != null &&
				  ColocationManager.Instance.Method == ColocationManager.ColocationMethod.AprilTag
				: tagProvider && tagProvider.IsRunning);

		private bool SessionReferencesBelongToCurrentMap
		{
			get
			{
				if (!SyncBus.Active || SyncBus.IsAuthority)
					return true;
				if (!sessionMapAdopted || CurrentMap == null)
					return false;

				Guid identityId = mapIdentity.Value.id;
				return identityId != Guid.Empty &&
				       CurrentMap.id == identityId.ToString("N");
			}
		}

		// Providers raise a change per entry, so injecting a map raises one per anchor. Record
		// which snapshots are stale and take them once at the end of the frame: reacting per
		// entry would both re-serialize the map to disk once per anchor and let a snapshot
		// observe a provider halfway through an import.

		private void OnSpatialAnchorConstraintsChanged() => anchorSnapshotPending = true;
		private void OnTagReferencesChanged() => tagSnapshotPending = true;
		private void OnTaggedAnchorsChanged() => taggedAnchorSnapshotPending = true;

		private void ClearPendingProviderSnapshots()
		{
			anchorSnapshotPending = false;
			tagSnapshotPending = false;
			taggedAnchorSnapshotPending = false;
		}

		private void ApplyPendingProviderSnapshots()
		{
			bool changed = false;

			if (anchorSnapshotPending)
			{
				anchorSnapshotPending = false;
				if (AnchorProviderOwnsCurrentState)
				{
					SnapshotAnchorProviderToMap();
					changed = true;
				}
			}

			if (tagSnapshotPending)
			{
				tagSnapshotPending = false;
				if (TagProviderOwnsCurrentState)
				{
					SnapshotTagProviderToMap();
					changed = true;
				}
			}

			if (taggedAnchorSnapshotPending)
			{
				taggedAnchorSnapshotPending = false;
				if (TagProviderOwnsCurrentState)
				{
					SnapshotTaggedAnchorsToMap();
					if (!SyncBus.Active)
						InjectAnchorsIntoAnchorProvider();
					changed = true;
				}
			}

			if (changed)
				ScheduleQuietSave();
		}

		private void SnapshotAnchorProviderToMap()
		{
			if (CurrentMap == null || !spatialAnchorProvider)
				return;

			// The map's anchor list is the union of both providers' realizations, so this
			// snapshot may only prune what this provider itself dropped. Tag anchors are
			// private per device: on a joiner the authority's constraints never describe
			// this headset's own, and clearing here would erase them from its copy.
			HashSet<string> present = new();
			foreach (Guid guid in spatialAnchorProvider.Constraints.Keys)
				present.Add(GuidToString(guid));

			CurrentMap.anchors.RemoveAll(entry =>
				entry.tagId < 0 && !present.Contains(entry.guid));

			foreach ((Guid guid, AnchorConstraintState state) in spatialAnchorProvider.Constraints)
			{
				CurrentMap.SetAnchorWithTag(
					GuidToString(guid), state.canonPose, state.bindingId);
			}
		}

		private void SnapshotTagProviderToMap()
		{
			if (CurrentMap == null || !tagProvider)
				return;

			CurrentMap.tags.Clear();
			foreach ((int tagId, Pose canon) in tagProvider.RegisteredTags)
				CurrentMap.SetTag(tagId, canon);
		}

		private void SnapshotTaggedAnchorsToMap()
		{
			if (CurrentMap == null || !tagProvider)
				return;

			List<TaggedAnchorConstraintData> realized = new();
			tagProvider.GetLocalAnchorConstraints(realized);

			// Mirror of SnapshotAnchorProviderToMap: this provider owns only the tag
			// realizations. Roaming anchors (tagId -1) belong to the anchor provider and
			// survive a map being tagged later, so they must not be swept up here.
			HashSet<string> present = new();
			foreach (TaggedAnchorConstraintData entry in realized)
				present.Add(GuidToString(entry.guid));

			List<string> dropped = new();
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (entry.tagId >= 0 && !present.Contains(entry.guid))
					dropped.Add(entry.guid);

			CurrentMap.anchors.RemoveAll(entry => dropped.Contains(entry.guid));

			foreach (TaggedAnchorConstraintData entry in realized)
				CurrentMap.SetAnchorWithTag(
					GuidToString(entry.guid), entry.canonPose, entry.tagId);

			// The provider stopped realizing these — their tag was unregistered, here or by the
			// session authority — so nothing will ask the device for them again.
			foreach (string guid in dropped)
				EraseTagAnchorSaveIfOrphaned(guid);
		}

		/// <summary>
		/// Erases a dropped tag anchor's local save. A guid some other map still references — a
		/// fork keeps its parent's anchors — has to stay on the device. Call this only once the
		/// current map no longer lists the anchor, so it does not veto its own erase.
		/// </summary>
		private void EraseTagAnchorSaveIfOrphaned(string guid)
		{
			if (!tagProvider || MapStore.IsAnchorReferenced(guid))
				return;

			if (TryGuidFromString(guid, out Guid parsed))
				_ = tagProvider.EraseAsync(parsed);
		}

		private void OnSpatialAnchorPersisted(Guid _)
		{
			ScheduleQuietSave();
		}

		// ------- session flows -----------------------------------

		private void AuthoritySessionStart()
		{
			if (authoritySessionStarted) return;
			authoritySessionStarted = true;

			GameMap map = EnsureCurrentMap();
			if (map == null)
				return;

			map.lastUsed = DateTime.UtcNow.Ticks;
			SaveCurrentMap();

			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj)
					obj.SpawnIfLocal();
		}

		private void OnBusDeactivated()
		{
			authoritySessionStarted = false;
			sessionMapAdopted = false;
			SaveCurrentMap();
			if (CurrentMap != null)
				InjectMapIntoProviders(CurrentMap);
			RebuildLocalObjectsAfterSession();
		}

		/// <summary>
		/// Authority moved. A promoted peer has to run the flow it skipped when it joined as a
		/// client; a demoted one goes back to following the published identity, which it must
		/// re-adopt before its provider snapshots count as describing the session's map.
		/// </summary>
		private void OnAuthorityChanged(bool isAuthority)
		{
			if (!SyncBus.Active)
				return;

			authoritySessionStarted = false;
			sessionMapAdopted = false;

			if (isAuthority)
				AuthoritySessionStart();
		}

		private async void RebuildLocalObjectsAfterSession()
		{
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

			RemoveMapObjects();
			InstantiateMapObjects(CurrentMap);
		}

		private void OnMapIdentitySynced()
		{
			if (SyncBus.Active && SyncBus.IsAuthority)
				AuthoritySessionStart();
			else
				TryAdopt();
		}

		private void OnMapIdentityChanged(MapIdentity _, MapIdentity __) => TryAdopt();

		private void TryAdopt()
		{
			if (!SyncBus.Active || SyncBus.IsAuthority)
				return;

			MapIdentity identity = mapIdentity.Value;
			if (identity.id == Guid.Empty)
				return;

			string id = identity.id.ToString("N");
			string version = identity.version.ToString("N");
			string name = identity.name.ToString();
			bool firstAdopt = !sessionMapAdopted;
			sessionMapAdopted = true;

			if (CurrentMap != null && CurrentMap.id == id)
			{
				CurrentMap.version = version;
				CurrentMap.baseVersion = version;
				CurrentMap.name = name;
				CurrentMap.dirty = false;
				AdoptProviderStateIntoMap();

				if (firstAdopt)
					RemoveMapObjects();

				SaveCurrentMapQuietly();
				return;
			}

			if (CurrentMap != null)
			{
				SaveCurrentMap();
				CurrentMap = null;
			}

			RemoveMapObjects();

			GameMap adopted;
			if (MapStore.TryGet(id, out GameMap local))
			{
				if (local.dirty && local.version != version)
					MapStore.Fork(local);

				adopted = local;
				adopted.objects.Clear();
			}
			else
			{
				adopted = new GameMap { id = id };
			}

			adopted.name = name;
			adopted.version = version;
			adopted.baseVersion = version;
			adopted.dirty = false;
			adopted.lastUsed = DateTime.UtcNow.Ticks;

			CurrentMap = adopted;
			frameContinuous = false;
			ClearPendingProviderSnapshots();
			InvalidateFrameAgreement();
			AdoptProviderStateIntoMap();
			MapStore.Save(adopted);
			CurrentMapChanged.Invoke(adopted);
		}

		private void AdoptProviderStateIntoMap()
		{
			if (CurrentMap == null || ColocationManager.Instance == null)
				return;

			if (ColocationManager.Instance.Method == ColocationManager.ColocationMethod.AprilTag)
			{
				// Tag anchors are private per device. Restore this headset's saved realizations,
				// while registered tag poses come from the authority's provider snapshot.
				List<TaggedAnchorConstraintData> saved = new();
				foreach (MapAnchorEntry entry in CurrentMap.anchors)
					if (entry.tagId >= 0 && TryGuidFromString(entry.guid, out Guid guid))
						saved.Add(new TaggedAnchorConstraintData(
							guid, entry.tagId, entry.canonPose));

				if (tagProvider)
					tagProvider.SetLocalAnchors(saved);

				SnapshotTagProviderToMap();
				SnapshotTaggedAnchorsToMap();
			}
			else
			{
				SnapshotAnchorProviderToMap();
				// Preserve tag capability and parent metadata even while the anchor strategy is
				// selected; the inactive tag provider still owns/synchronizes its registered data.
				SnapshotTagProviderToMap();
			}

			// The snapshots above are this adoption's, taken deliberately; anything the
			// providers queued while they were being replaced describes the map we just left.
			ClearPendingProviderSnapshots();
		}

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
			InjectTagsIntoProvider();
			SaveCurrentMap();
			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		public bool UnregisterTag(int tagId)
		{
			if (SyncBus.Active || CurrentMap == null)
				return false;

			bool removed = CurrentMap.tags.RemoveAll(entry => entry.id == tagId) > 0;
			if (!removed)
				return false;

			// Collected before the prune, erased after it: the orphan check has to run against
			// the map that no longer lists them.
			List<string> dropped = new();
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (entry.tagId == tagId)
					dropped.Add(entry.guid);

			CurrentMap.anchors.RemoveAll(entry => entry.tagId == tagId);

			foreach (string guid in dropped)
				EraseTagAnchorSaveIfOrphaned(guid);

			MapStore.MarkEdited(CurrentMap);
			InjectMapIntoProviders(CurrentMap);
			SaveCurrentMap();
			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		// ------- probe -------------------------------------------

		private async void StartupProbe(CancellationToken ctkn)
		{
			try
			{
				await Awaitable.WaitForSecondsAsync(3f, ctkn);
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

		public async Awaitable ProbeAndAutoLoad(CancellationToken ctkn = default)
		{
			if (!spatialAnchorProvider || !spatialAnchorProvider.IsAvailable)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();
				if (map.anchors.Count == 0)
					continue;

				HashSet<Guid> localized = await spatialAnchorProvider.ProbeAsync(
					AnchorGuidsOf(map), probeTimeoutSeconds, ctkn);
				probeResults[map.id] = localized.Count;
				ProbeResultsChanged.Invoke();

				if (localized.Count == 0)
					continue;

				if (CurrentMap == null && !SyncBus.Active)
					LoadMap(map.id);

				return;
			}
		}

		public async Awaitable ProbeAllMaps(CancellationToken ctkn = default)
		{
			if (!spatialAnchorProvider || !spatialAnchorProvider.IsAvailable)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();
				if (map.anchors.Count == 0)
				{
					probeResults[map.id] = 0;
					continue;
				}

				HashSet<Guid> localized = await spatialAnchorProvider.ProbeAsync(
					AnchorGuidsOf(map), probeTimeoutSeconds, ctkn);
				probeResults[map.id] = localized.Count;
				ProbeResultsChanged.Invoke();
			}
		}

		private static List<Guid> AnchorGuidsOf(GameMap map)
		{
			List<Guid> guids = new(map.anchors.Count);
			foreach (MapAnchorEntry entry in map.anchors)
				if (TryGuidFromString(entry.guid, out Guid guid))
					guids.Add(guid);
			return guids;
		}

		// ------- identity and helpers ----------------------------

		private void PublishIdentity()
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || CurrentMap == null)
				return;

			if (!TryGuidFromString(CurrentMap.id, out Guid id) ||
			    !TryGuidFromString(CurrentMap.version, out Guid version))
			{
				Debug.LogError($"Map '{CurrentMap.name}' has an unusable id or version; " +
					"peers cannot be told which map this session is using.");
				return;
			}

			FixedString64Bytes name = default;
			name.CopyFromTruncated(CurrentMap.name ?? "");
			mapIdentity.Value = new MapIdentity
			{
				id = id,
				version = version,
				name = name,
			};
		}

		private static string GuidToString(Guid guid) => guid.ToString("N");

		/// <summary>
		/// Parses a persisted guid. Map files can be hand-edited or half-written, and MapStore
		/// goes to some trouble to keep a damaged one loadable — throwing here mid-import would
		/// undo that and leave the providers holding half a map.
		/// </summary>
		private static bool TryGuidFromString(string value, out Guid guid)
		{
			if (Guid.TryParseExact(value, "N", out guid))
				return true;

			Debug.LogWarning($"Ignoring malformed anchor guid '{value}' in a saved map.");
			return false;
		}
	}
}
