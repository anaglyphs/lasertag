using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Debugging.Visuals;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// The session's map on the wire. Objects replicate through NGO and anchor/tag canon
	/// poses through their own dictionaries, so identity is all that needs to ride here.
	/// default(MapIdentity) means "no map".
	/// </summary>
	public struct MapIdentity
	{
		public Guid id;
		public Guid version;
		public FixedString64Bytes name;
		public bool hasTags;
	}

	/// <summary>
	/// Owns maps at runtime: the current map, its anchors, and every flow that reads or
	/// writes them. Colocators consume anchors from here (via
	/// <see cref="IColocationReferenceSource"/>); they no longer own them.
	///
	/// Shared anchors are transport, not storage: every anchor this device ends up with —
	/// minted locally or downloaded from a peer — is saved to local storage and recorded in
	/// this device's copy of the map. The cloud is only how an anchor gets between headsets
	/// the first time.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MapManager : MonoBehaviour, IColocationReferenceSource
	{
		public static MapManager Instance { get; private set; }

		[SerializeField] private MapObject[] objectPrefabs = Array.Empty<MapObject>();

		[Tooltip("Distance from all other anchors the headset needs to be to mint a new one")]
		[SerializeField] private float newAnchorDist = 6f;

		[SerializeField] private LayerMask anchorPlacementRaycastLayerMask = Physics.DefaultRaycastLayers;

		[Tooltip("How long the room probe lets each anchor try to localize")]
		[SerializeField] private float probeTimeoutSeconds = 8f;

		[Tooltip("Mean reference error above which the fit does not count as agreeing")]
		[SerializeField] private float agreementMaxError = 0.3f;

		[Tooltip("Tag observations averaged before an anchor's canon pose is rewritten")]
		[SerializeField] private int tagCorrectionSamples = 30;

		public GameMap CurrentMap { get; private set; }
		public event Action<GameMap> CurrentMapChanged = delegate { };

		/// <summary>Probe results per map id: how many of its anchors localized here.</summary>
		public IReadOnlyDictionary<string, int> ProbeResults => probeResults;
		private readonly Dictionary<string, int> probeResults = new();
		public event Action ProbeResultsChanged = delegate { };

		// ------- runtime state -------------------------------------

		private AnchorRegistry registry;
		private ARAnchorManager anchorManager;
		private MetaOpenXRAnchorSubsystem metaAnchorSubsystem;

		private readonly Dictionary<SerializableGuid, AnchorLease> leases = new();

		private readonly SyncVariable<MapIdentity> mapIdentity = new("map.identity");
		private readonly SyncDictionary<SerializableGuid, Pose> canonAnchors = new("map.anchors.canon");
		private readonly SyncDictionary<int, Pose> canonTags = new("map.tags.canon");

		public SyncDictionary<int, Pose> CanonTags => canonTags;

		private CancellationTokenSource lifetimeCtknSrc;

		private bool mintInFlight;
		private bool savePending;

		// The frame a map was authored in stays trustworthy as long as tracking has been
		// physically continuous since its creation — no sleep, no recenter. This is what
		// lets a brand-new map register its first references at all: with nothing to align
		// to yet, continuity is the only ground truth there is.
		private bool frameContinuous;

		private int agreeingReferenceCount;
		private float meanReferenceError;
		private readonly List<ColocationReference> referenceScratch = new();

		private class TagCorrection
		{
			public Vector3 positionSum;
			public Vector4 rotationSum;
			public int samples;
		}

		private readonly Dictionary<int, TagCorrection> tagCorrections = new();
		private readonly HashSet<int> tagAnchorMintsInFlight = new();

		// ------- lifecycle -----------------------------------------

		private void Awake()
		{
			Instance = this;
			lifetimeCtknSrc = new CancellationTokenSource();

			mapIdentity.Register();
			canonAnchors.Register();
			canonTags.Register();

			mapIdentity.Changed += OnMapIdentityChanged;
			mapIdentity.Synced += OnMapIdentitySynced;
			canonAnchors.Changed += OnCanonAnchorsChanged;
			canonTags.Changed += OnCanonTagsChanged;

			SyncBus.Deactivated += OnBusDeactivated;
			MapObject.LocalEditOccurred += OnLocalEdit;
			MainXRRig.Recentered += OnRecentered;

#if !UNITY_EDITOR
			anchorManager = FindFirstObjectByType<ARAnchorManager>();
			metaAnchorSubsystem = (MetaOpenXRAnchorSubsystem)anchorManager.subsystem;
			registry = new AnchorRegistry(anchorManager, metaAnchorSubsystem);
#endif
		}

		private void Start()
		{
			MintLoop(lifetimeCtknSrc.Token);

			if (registry != null)
				StartupProbe(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			SaveCurrentMap();

			MainXRRig.Recentered -= OnRecentered;
			MapObject.LocalEditOccurred -= OnLocalEdit;
			SyncBus.Deactivated -= OnBusDeactivated;

			canonTags.Changed -= OnCanonTagsChanged;
			canonAnchors.Changed -= OnCanonAnchorsChanged;
			mapIdentity.Synced -= OnMapIdentitySynced;
			mapIdentity.Changed -= OnMapIdentityChanged;

			canonTags.Unregister();
			canonAnchors.Unregister();
			mapIdentity.Unregister();

			lifetimeCtknSrc?.Cancel();
			ReleaseAllLeases();
			registry?.Dispose();
		}

		private void OnApplicationPause(bool paused)
		{
			if (paused)
				SaveCurrentMap();
		}

		private void OnApplicationFocus(bool focused)
		{
			// Sleep pauses tracking; whatever frame authoring was happening in slid away.
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

			if (AnaglyphDebugging.DebugMode && CurrentMap != null)
			{
				foreach (MapAnchorEntry anchor in CurrentMap.anchors)
					DebugAxisVisual.DrawDebugAxis(
						anchor.canonPose.position, anchor.canonPose.rotation, Color.cyan);

				foreach (MapTagEntry tag in CurrentMap.tags)
					DebugAxisVisual.DrawDebugAxis(
						tag.canonPose.position, tag.canonPose.rotation, Color.magenta);
			}
		}

		// ------- world-frame trust ---------------------------------

		/// <summary>
		/// Whether durable world-space data (new anchors, canon rewrites, map edits meant to
		/// land in a real frame) may be written right now.
		///
		/// A fresh map with no references yet is trusted by definition — its frame IS the
		/// current one. A map with references requires the colocator to be localized against
		/// enough agreeing references, not just localized at all: if the wrong map loaded
		/// optimistically, minting into it would permanently pollute its anchor set.
		/// </summary>
		public bool WorldFrameTrusted
		{
			get
			{
				if (CurrentMap == null)
					return false;

				// Authoring in an unbroken tracking session: the map's frame IS this frame.
				if (frameContinuous)
					return true;

				// An empty map has no frame yet; its first reference defines one.
				if (CurrentMap.anchors.Count == 0 && CurrentMap.tags.Count == 0)
					return true;

				if (!ColocationManager.IsColocated)
					return false;

				int required = Mathf.Min(2, CurrentMap.anchors.Count);
				return agreeingReferenceCount >= required &&
				       meanReferenceError <= agreementMaxError;
			}
		}

		private void UpdateAgreement()
		{
			referenceScratch.Clear();
			GetColocationReferences(referenceScratch);

			agreeingReferenceCount = referenceScratch.Count;

			if (referenceScratch.Count == 0)
			{
				meanReferenceError = 0f;
				return;
			}

			// References are already expressed post-alignment, so observed-vs-canon distance
			// is directly the fit residual.
			float errorSum = 0f;
			foreach (ColocationReference reference in referenceScratch)
				errorSum += Vector3.Distance(reference.observed.position, reference.canon.position);

			meanReferenceError = errorSum / referenceScratch.Count;
		}

		// ------- colocation references -----------------------------

		public void GetColocationReferences(List<ColocationReference> results)
		{
			if (CurrentMap == null)
				return;

			foreach ((SerializableGuid guid, AnchorLease lease) in leases)
			{
				AnchorHandle handle = lease.Handle;
				if (handle.state != AnchorHandle.State.Active) continue;
				if (handle.anchor.trackingState != TrackingState.Tracking) continue;
				if (!CurrentMap.TryGetAnchor(GuidToString(guid), out MapAnchorEntry entry)) continue;

				Transform anchorTransform = handle.anchor.transform;
				Pose observed = new(anchorTransform.position, anchorTransform.rotation);

				results.Add(new ColocationReference(observed, entry.canonPose));
			}
		}

		// ------- current map lifecycle -----------------------------

		/// <summary>
		/// Loads a map, adopting its frame: existing map objects are torn down and the map's
		/// own are instantiated at their canon poses, and its anchors are committed to real
		/// ARAnchors for the per-frame fit. Only callable while disconnected — in a session
		/// the map is dictated by the session.
		/// </summary>
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
			frameContinuous = false; // a loaded map's frame must be earned by localizing
			MapStore.MarkUsed(map);

			InstantiateMapObjects(map);

			foreach (MapAnchorEntry entry in map.anchors)
				EnsureLease(GuidFromString(entry.guid), AnchorSource.Local);

			MirrorTagsToCanon();

			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		/// <summary>Unloads without loading another: tears down objects, releases anchors.</summary>
		public void UnloadCurrentMap()
		{
			if (CurrentMap != null)
				SaveCurrentMap();

			CurrentMap = null;
			frameContinuous = false;
			tagCorrections.Clear();
			ReleaseAllLeases();
			DestroyMapObjects(localOnly: false);
			MirrorTagsToCanon();

			CurrentMapChanged.Invoke(null);
		}

		public void DeleteMap(string id)
		{
			if (CurrentMap != null && CurrentMap.id == id)
				UnloadCurrentMap();

			List<string> orphanedAnchors = new();
			MapStore.Delete(id, orphanedAnchors);

			// Erase local saves nothing references anymore — but only those; an anchor a
			// surviving map (e.g. a fork) still uses must stay on the device.
			if (registry != null)
				foreach (string orphan in orphanedAnchors)
					EraseAnchorQuietly(GuidFromString(orphan));
		}

		private async void EraseAnchorQuietly(SerializableGuid guid)
		{
			try
			{
				await registry.TryEraseSavedAsync(guid);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private void OnLocalEdit()
		{
			// Placing the first map object creates a map, authored in the current frame.
			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				CurrentMapChanged.Invoke(CurrentMap);
			}

			MapStore.MarkEdited(CurrentMap);
			ScheduleSave();
		}

		private async void ScheduleSave()
		{
			if (savePending) return;
			savePending = true;

			try
			{
				await Awaitable.WaitForSecondsAsync(2f, lifetimeCtknSrc.Token);
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

		/// <summary>
		/// Snapshots live objects into the current map and persists it. While hosting, a
		/// content change also republishes identity — publishing is the sync point that
		/// makes this copy the shared baseline, so dirty clears.
		/// </summary>
		public void SaveCurrentMap()
		{
			if (CurrentMap == null)
				return;

			SnapshotObjects(CurrentMap);
			MapStore.Save(CurrentMap);

			if (SyncBus.Active && SyncBus.IsAuthority)
			{
				PublishIdentity();
				CurrentMap.baseVersion = CurrentMap.version;
				CurrentMap.dirty = false;
				MapStore.Save(CurrentMap);
			}
		}

		// Saves that only touch this device's realization of the map (anchor records, canon
		// rewrites). Never dirties: anchors are not content and must not force forks or mint
		// new content versions.
		private void SaveCurrentMapQuietly()
		{
			if (CurrentMap == null)
				return;

			MapStore.Save(CurrentMap);
		}

		private void SnapshotObjects(GameMap map)
		{
			map.objects.Clear();

			foreach (MapObject obj in MapObject.All)
			{
				if (string.IsNullOrEmpty(obj.PrefabId))
				{
					Debug.LogWarning($"Map object '{obj.name}' has no prefab id; not saving it.");
					continue;
				}

				Transform t = obj.transform;
				map.objects.Add(new MapObjectEntry
				{
					prefabId = obj.PrefabId,
					pose = new Pose(t.position, t.rotation),
				});
			}
		}

		private void InstantiateMapObjects(GameMap map)
		{
			foreach (MapObjectEntry entry in map.objects)
			{
				MapObject prefab = FindPrefab(entry.prefabId);

				if (prefab == null)
				{
					Debug.LogWarning($"Map references unknown prefab '{entry.prefabId}'.");
					continue;
				}

				Instantiate(prefab.gameObject, entry.pose.position, entry.pose.rotation);
			}
		}

		private MapObject FindPrefab(string prefabId)
		{
			foreach (MapObject prefab in objectPrefabs)
				if (prefab != null && prefab.PrefabId == prefabId)
					return prefab;

			return null;
		}

		private static void DestroyMapObjects(bool localOnly)
		{
			// Copy: destroying mutates MapObject.All.
			List<MapObject> objects = new(MapObject.All);

			foreach (MapObject obj in objects)
			{
				if (localOnly && !obj.IsLocalOnly)
					continue;

				Destroy(obj.gameObject);
			}
		}

		// ------- session flows -------------------------------------

		// Set once the authority-side session start ran; adoption uses its own idempotence.
		private bool authoritySessionStarted;

		// Runs on the Synced phase, not Activated: Synced fires after every Activated handler,
		// so the session's colocation method (written by ColocationManager on Activated) is
		// already in place regardless of subscription order.
		private void AuthoritySessionStart()
		{
			if (authoritySessionStarted) return;
			authoritySessionStarted = true;

			// A host with no map still needs anchors for joiners to colocate against, and
			// the first placed object needs somewhere to land: hosting implies a map.
			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				CurrentMapChanged.Invoke(CurrentMap);
			}

			CurrentMap.lastUsed = DateTime.UtcNow.Ticks;

			// Snapshot + persist + publish identity; publishing makes this copy the shared
			// baseline, so dirty clears in here.
			SaveCurrentMap();

			// Everything placed before hosting joins the session.
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				obj.SpawnIfLocal();

			if (ColocationManager.Instance.Method == ColocationManager.ColocationMethod.MetaSharedAnchor)
				PublishAndShareAnchors();

			MirrorTagsToCanon();
		}

		/// <summary>
		/// The canon-tags dictionary always reflects the current map's registered tags from
		/// wherever this peer may write them: the session authority publishes its map's tags
		/// to everyone, and offline every peer is its own authority, which is what lets the
		/// tag colocator read one dictionary in both worlds. Non-authority peers never write
		/// — their canon tags arrive through the sync.
		/// </summary>
		private void MirrorTagsToCanon()
		{
			if (SyncBus.Active && !SyncBus.IsAuthority)
				return;

			canonTags.Clear();

			if (CurrentMap == null)
				return;

			foreach (MapTagEntry tag in CurrentMap.tags)
				canonTags.Set(tag.id, tag.canonPose);
		}

		/// <summary>
		/// Eager re-share of the whole loaded anchor set, at session start. Shares from an
		/// earlier session have expired, so a loaded map's anchors must be re-uploaded to be
		/// downloadable by this session's first-time joiners.
		///
		/// The session is NOT gated on any of it: this device's anchors are already on disk,
		/// and so are those of any peer that has played this map before. Canon poses publish
		/// regardless; a failed share only affects a first-time joiner's download of that one
		/// anchor.
		/// </summary>
		private void PublishAndShareAnchors()
		{
			if (metaAnchorSubsystem != null)
			{
				Supported shareSupport = metaAnchorSubsystem.isSharedAnchorsSupported;
				if (shareSupport != Supported.Supported)
				{
					Debug.LogWarning($"Shared anchors are not enabled/supported! {shareSupport}");

					UserErrors.Raise("Shared spatial anchors unavailable",
						$"This headset reports shared anchor support as '{shareSupport}'. " +
						"Joiners will not be able to align. Try AprilTag colocation instead.");
				}
			}

			foreach (MapAnchorEntry entry in CurrentMap.anchors)
			{
				SerializableGuid guid = GuidFromString(entry.guid);

				canonAnchors.RequestSet(guid, entry.canonPose);
				EnsureLease(guid, AnchorSource.Local);
				ShareWhenActive(guid);
			}
		}

		private async void ShareWhenActive(SerializableGuid guid)
		{
			try
			{
				CancellationToken ctkn = lifetimeCtknSrc.Token;

				if (!leases.TryGetValue(guid, out AnchorLease lease))
					return;

				while (lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);

					if (!leases.TryGetValue(guid, out AnchorLease current) || current != lease)
						return; // released or replaced while waiting
				}

				await ShareAnchor(lease.Handle.anchor, ctkn);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private async Awaitable<bool> ShareAnchor(ARAnchor anchor, CancellationToken ctkn)
		{
			const float retrySeconds = 3f;
			// Retries are silent until it's clear this isn't a transient hiccup
			const int attemptsBeforeTellingUser = 3;
			// A rejected write retries identically to a network failure, so don't hang on it
			// forever — the canon pose is already published and the local save already exists.
			const int maxAttempts = 5;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				ctkn.ThrowIfCancellationRequested();

				metaAnchorSubsystem.sharedAnchorsGroupId = anchor.trackableId;
				XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);

				ctkn.ThrowIfCancellationRequested();

				if (!result.IsError())
					return true;

				// nativeStatusCode is the raw XrResult — the only place a network failure and
				// a rejected write (e.g. re-sharing into a group another device created) are
				// distinguishable.
				Debug.LogWarning($"Failed to share anchor {anchor.trackableId}: {result} " +
					$"(native {result.nativeStatusCode})");

				if (attempt == attemptsBeforeTellingUser)
					UserErrors.Raise("Couldn't share a spatial anchor",
						"Shared spatial anchors are uploaded through Meta's servers, so this " +
						"headset needs a working internet connection. Joiners that have " +
						"played this map before are unaffected.");

				await Awaitable.WaitForSecondsAsync(retrySeconds, ctkn);
			}

			return false;
		}

		private void OnBusDeactivated()
		{
			authoritySessionStarted = false;

			// NGO tears the session's objects down around now; persist what we saw last and
			// rebuild the world locally so the map survives the session ending.
			SaveCurrentMap();
			RebuildLocalObjectsAfterSession();
		}

		private async void RebuildLocalObjectsAfterSession()
		{
			try
			{
				// Let netcode finish despawning/destroying before rebuilding.
				await Awaitable.NextFrameAsync(lifetimeCtknSrc.Token);
				await Awaitable.NextFrameAsync(lifetimeCtknSrc.Token);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			if (CurrentMap == null || SyncBus.Active)
				return;

			DestroyMapObjects(localOnly: false);
			InstantiateMapObjects(CurrentMap);

			// Session teardown reset the sync endpoints; restore the offline mirror so the
			// tag colocator keeps its canon poses.
			MirrorTagsToCanon();
		}

		// ------- adopt (joiner side) -------------------------------

		private void OnMapIdentitySynced()
		{
			if (SyncBus.Active && SyncBus.IsAuthority)
				AuthoritySessionStart();
			else
				TryAdopt();
		}

		private void OnMapIdentityChanged(MapIdentity oldValue, MapIdentity newValue) => TryAdopt();

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

			// Same map already adopted: an identity update mid-session is the host saving new
			// content. The session mirrors it into us continuously, so it is a sync point:
			// move the baseline forward and stay clean.
			if (CurrentMap != null && CurrentMap.id == id)
			{
				CurrentMap.version = version;
				CurrentMap.baseVersion = version;
				CurrentMap.name = name;
				CurrentMap.dirty = false;
				SaveCurrentMapQuietly();
				return;
			}

			// Different map: unload ours first — there is only one world space.
			if (CurrentMap != null)
				UnloadCurrentMap();
			else
				DestroyMapObjects(localOnly: true); // never inject stale local objects into a session

			GameMap adopted;

			if (MapStore.TryGet(id, out GameMap local))
			{
				// Fork-on-edit: an edited local copy survives under a new id; the received
				// version takes over this id. An unedited copy is simply replaced.
				if (local.dirty && local.version != version)
					MapStore.Fork(local);

				adopted = local;
				adopted.objects.Clear(); // re-recorded from the live session on save
				adopted.tags.Clear();    // re-recorded from canonTags below
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
			frameContinuous = false; // this peer localizes into the host's frame
			MapStore.Save(adopted);

			// Anchors and tags stream in through their dictionaries; reconcile whatever
			// already arrived (snapshots land before Synced fires).
			ReconcileCanonAnchors();
			foreach ((int tagId, Pose pose) in canonTags)
				CurrentMap.SetTag(tagId, pose);

			CurrentMapChanged.Invoke(CurrentMap);
		}

		// ------- canon anchor transport ----------------------------

		private void OnCanonAnchorsChanged(SyncDictionary<SerializableGuid, Pose>.EventData data)
		{
			if (CurrentMap == null)
				return;

			switch (data.op)
			{
				case SyncDictionaryOp.Set:
					AdoptCanonAnchor(data.eventKey, data.eventValue);
					break;

				case SyncDictionaryOp.Snapshot:
					ReconcileCanonAnchors();
					break;

				case SyncDictionaryOp.Remove:
				case SyncDictionaryOp.Clear:
					// Canon anchors are never removed mid-session; the durable copy is the
					// map file, so a session reset does not erase anything.
					break;
			}
		}

		private void ReconcileCanonAnchors()
		{
			foreach ((SerializableGuid guid, Pose pose) in canonAnchors)
				AdoptCanonAnchor(guid, pose);
		}

		private void AdoptCanonAnchor(SerializableGuid guid, Pose canonPose)
		{
			if (CurrentMap == null)
				return;

			CurrentMap.SetAnchor(GuidToString(guid), canonPose);

			// Local storage is tried first; the cloud download only happens for anchors this
			// device has never saved.
			EnsureLease(guid, AnchorSource.Any);
		}

		private void OnCanonTagsChanged(SyncDictionary<int, Pose>.EventData data)
		{
			if (CurrentMap == null || SyncBus.IsAuthority)
				return;

			switch (data.op)
			{
				case SyncDictionaryOp.Set:
					CurrentMap.SetTag(data.eventKey, data.eventValue);
					break;

				case SyncDictionaryOp.Snapshot:
					foreach ((int tagId, Pose pose) in canonTags)
						CurrentMap.SetTag(tagId, pose);
					break;
			}
		}

		// ------- anchor leases -------------------------------------

		private void EnsureLease(SerializableGuid guid, AnchorSource source)
		{
			if (registry == null || leases.ContainsKey(guid))
				return;

			AnchorLease lease = registry.Acquire(guid, source);
			leases[guid] = lease;
			SaveAndRecordWhenActive(guid, lease);
		}

		// Downloading a shared anchor does not save it: it arrives as a live trackable and
		// nothing more. The local save is what makes the map durable — without it the map can
		// only be re-entered while its host is present and online.
		private async void SaveAndRecordWhenActive(SerializableGuid guid, AnchorLease lease)
		{
			try
			{
				CancellationToken ctkn = lifetimeCtknSrc.Token;

				while (lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);

					if (!leases.TryGetValue(guid, out AnchorLease current) || current != lease)
						return;
				}

				if (!registry.IsSaved(guid))
				{
					bool saved = await registry.TrySaveAsync(guid, ctkn);
					if (!saved)
						Debug.LogWarning($"Downloaded anchor {guid} could not be saved; " +
							"this map will need its host again to re-enter this room.");
				}

				SaveCurrentMapQuietly();
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		private void ReleaseAllLeases()
		{
			foreach (AnchorLease lease in leases.Values)
				lease.Dispose();

			leases.Clear();
		}

		// ------- anchor minting ------------------------------------

		private async void MintLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.FixedUpdateAsync(ctkn);

					if (registry == null || CurrentMap == null || mintInFlight) continue;
					if (!WorldFrameTrusted) continue;

					// Roaming anchors accompany shared-anchor colocation. Tag maps get one
					// anchor per tag instead (minted on observation, not here).
					bool roamingAllowed = SyncBus.Active
						? ColocationManager.Instance.Method ==
						  ColocationManager.ColocationMethod.MetaSharedAnchor
						: !CurrentMap.HasTags;

					if (!roamingAllowed) continue;

					float spawnEverySqr = newAnchorDist * newAnchorDist;
					float3 headPos = MainXRRig.Camera.transform.position;
					float closestDistSq = float.MaxValue;

					foreach (MapAnchorEntry entry in CurrentMap.anchors)
					{
						float distSq = math.distancesq((float3)entry.canonPose.position, headPos);
						if (distSq < closestDistSq) closestDistSq = distSq;
					}

					if (closestDistSq > spawnEverySqr)
						await MintAnchorUnderPlayer(ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				MintLoop(ctkn);
			}
		}

		private async Awaitable MintAnchorUnderPlayer(CancellationToken ctkn)
		{
			Pose feetPose;
			feetPose.rotation = Quaternion.identity;

			Vector3 headPos = MainXRRig.Camera.transform.position;
			Ray ray = new(headPos, Vector3.down);

			if (Physics.Raycast(ray, out RaycastHit hit, 2f, anchorPlacementRaycastLayerMask,
				    QueryTriggerInteraction.Ignore))
				feetPose.position = hit.point;
			else
				feetPose.position = headPos - Vector3.up * 1.5f;

			await MintAnchor(feetPose, -1, ctkn);
		}

		/// <summary>
		/// Creates an anchor at a world pose, saves it to local storage, and records it in the
		/// current map with that pose as canon. In a shared-anchor session it is also uploaded
		/// and its canon pose published — publication does not wait on the upload.
		/// </summary>
		public async Awaitable MintAnchor(Pose pose, int tagId, CancellationToken ctkn)
		{
			if (registry == null || CurrentMap == null || mintInFlight)
				return;

			mintInFlight = true;

			AnchorLease lease = null;
			bool established = false;

			try
			{
				Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(pose);
				if (!result.status.IsSuccess() || result.value == null)
					throw new Exception("Failed to create new anchor!");

				lease = registry.Acquire(result.value, AnchorSource.Local);
				SerializableGuid guid = lease.Handle.guid;

				ctkn.ThrowIfCancellationRequested();

				// An anchor that can't be saved is useless to the map — it would be a record
				// no later session can load.
				bool saved = await registry.TrySaveAsync(result.value, ctkn);
				if (!saved)
					return;

				ctkn.ThrowIfCancellationRequested();

				leases[guid] = lease;

				CurrentMap.SetAnchorWithTag(GuidToString(guid), pose, tagId);
				SaveCurrentMapQuietly();

				bool sharedSession = SyncBus.Active &&
					ColocationManager.Instance.Method ==
					ColocationManager.ColocationMethod.MetaSharedAnchor;

				if (sharedSession)
				{
					canonAnchors.RequestSet(guid, pose);
					_ = ShareAnchor(result.value, ctkn);
				}

				established = true;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				mintInFlight = false;

				if (lease != null && !established)
				{
					SerializableGuid guid = lease.Handle.guid;

					if (leases.TryGetValue(guid, out AnchorLease registered) && registered == lease)
						leases.Remove(guid);

					lease.Dispose();
				}
			}
		}

		// ------- tags ----------------------------------------------

		/// <summary>
		/// Registers a tag into the current map at its observed world pose — a deliberate
		/// authoring act, only meaningful while the frame is trusted, and only outside
		/// sessions (a session's canon tags come from its host's map).
		/// </summary>
		public bool RegisterTag(int tagId, Pose worldPose)
		{
			if (SyncBus.Active)
				return false;

			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				CurrentMapChanged.Invoke(CurrentMap);
			}

			if (!WorldFrameTrusted)
				return false;

			CurrentMap.SetTag(tagId, worldPose);
			MapStore.MarkEdited(CurrentMap);
			SaveCurrentMap();
			MirrorTagsToCanon();
			return true;
		}

		public bool UnregisterTag(int tagId)
		{
			if (SyncBus.Active || CurrentMap == null)
				return false;

			for (int i = 0; i < CurrentMap.tags.Count; i++)
			{
				if (CurrentMap.tags[i].id != tagId)
					continue;

				CurrentMap.tags.RemoveAt(i);
				MapStore.MarkEdited(CurrentMap);
				SaveCurrentMap();
				MirrorTagsToCanon();
				return true;
			}

			return false;
		}

		/// <summary>
		/// A stable, close observation of a registered tag, in world coordinates. Mints this
		/// device's anchor for the tag if it has none, and otherwise corrects that anchor's
		/// canon pose — tags are environment and cannot drift; anchors can.
		/// </summary>
		public void OnTagObserved(int tagId, Pose observedTagPose)
		{
			if (registry == null || CurrentMap == null) return;
			if (!CurrentMap.TryGetTag(tagId, out MapTagEntry tag)) return;
			if (!WorldFrameTrusted) return;

			MapAnchorEntry? tagAnchor = null;
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				if (entry.tagId == tagId)
				{
					tagAnchor = entry;
					break;
				}

			if (tagAnchor == null)
			{
				MintTagAnchor(tagId, observedTagPose, tag.canonPose);
				return;
			}

			CorrectTagAnchor(tagId, tag.canonPose, observedTagPose, tagAnchor.Value);
		}

		private async void MintTagAnchor(int tagId, Pose observedTagPose, Pose canonTagPose)
		{
			if (mintInFlight || !tagAnchorMintsInFlight.Add(tagId))
				return;

			try
			{
				// The anchor is created AT the observed tag, so its canon pose is exactly the
				// tag's canon pose: the relative term of the correction formula is identity at
				// creation time. Note the anchor is minted at the observed pose but recorded
				// at canon — if the fit has residual error here, the first correction absorbs
				// it.
				await MintAnchorAt(observedTagPose, canonTagPose, tagId, lifetimeCtknSrc.Token);
			}
			finally
			{
				tagAnchorMintsInFlight.Remove(tagId);
			}
		}

		private async Awaitable MintAnchorAt(Pose observedPose, Pose canonPose, int tagId,
			CancellationToken ctkn)
		{
			await MintAnchor(observedPose, tagId, ctkn);

			// MintAnchor records canon = observed; for tag anchors canon comes from the tag.
			if (CurrentMap != null && CurrentMap.TryGetAnchorByTag(tagId, out MapAnchorEntry entry))
			{
				CurrentMap.SetAnchorWithTag(entry.guid, canonPose, tagId);
				SaveCurrentMapQuietly();
			}
		}

		/// <summary>
		/// canon_X := canon_T ∘ (observed_T⁻¹ ∘ observed_X), averaged over several
		/// detections. The relative term is alignment-invariant, so the current rig fit
		/// cancels out entirely — the correction cannot feed back through the alignment it
		/// informs.
		/// </summary>
		private void CorrectTagAnchor(int tagId, Pose canonTag, Pose observedTag,
			MapAnchorEntry anchorEntry)
		{
			if (!leases.TryGetValue(GuidFromString(anchorEntry.guid), out AnchorLease lease))
				return;

			AnchorHandle handle = lease.Handle;
			if (handle.state != AnchorHandle.State.Active) return;
			if (handle.anchor.trackingState != TrackingState.Tracking) return;

			Transform anchorTransform = handle.anchor.transform;

			// Both observations in the same frame; any rig alignment cancels in the relative
			// term.
			Matrix4x4 observedTagMat = Matrix4x4.TRS(
				observedTag.position, observedTag.rotation, Vector3.one);
			Matrix4x4 observedAnchorMat = Matrix4x4.TRS(
				anchorTransform.position, anchorTransform.rotation, Vector3.one);
			Matrix4x4 canonTagMat = Matrix4x4.TRS(
				canonTag.position, canonTag.rotation, Vector3.one);

			Matrix4x4 correctedMat = canonTagMat * (observedTagMat.inverse * observedAnchorMat);

			Vector3 correctedPos = correctedMat.GetPosition();
			Quaternion correctedRot = correctedMat.rotation;

			// Single-frame tag pose estimates are noisy; average before committing.
			if (!tagCorrections.TryGetValue(tagId, out TagCorrection correction))
			{
				correction = new TagCorrection();
				tagCorrections[tagId] = correction;
			}

			Vector4 rotVec = new(correctedRot.x, correctedRot.y, correctedRot.z, correctedRot.w);
			if (correction.samples > 0 && Vector4.Dot(correction.rotationSum, rotVec) < 0f)
				rotVec = -rotVec; // same hemisphere, or the average cancels itself

			correction.positionSum += correctedPos;
			correction.rotationSum += rotVec;
			correction.samples++;

			if (correction.samples < tagCorrectionSamples)
				return;

			Vector3 averagePos = correction.positionSum / correction.samples;
			Vector4 averageRotVec = correction.rotationSum.normalized;
			Quaternion averageRot = new(averageRotVec.x, averageRotVec.y, averageRotVec.z,
				averageRotVec.w);

			tagCorrections.Remove(tagId);

			CurrentMap.SetAnchorWithTag(anchorEntry.guid, new Pose(averagePos, averageRot), tagId);
			SaveCurrentMapQuietly();

			bool sharedSession = SyncBus.Active &&
				ColocationManager.Instance.Method ==
				ColocationManager.ColocationMethod.MetaSharedAnchor;

			if (sharedSession)
				canonAnchors.RequestSet(GuidFromString(anchorEntry.guid),
					new Pose(averagePos, averageRot));
		}

		// ------- probe ---------------------------------------------

		private async void StartupProbe(CancellationToken ctkn)
		{
			try
			{
				// Let OpenXR and tracking settle before asking where we are.
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

		/// <summary>
		/// Probes candidate maps' anchors against the current physical space (most recently
		/// used first, one map at a time — for the same-room-every-day case this resolves in
		/// a single probe) and optimistically loads the first hit if nothing is loaded yet.
		/// </summary>
		public async Awaitable ProbeAndAutoLoad(CancellationToken ctkn = default)
		{
			if (registry == null)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();

				if (map.anchors.Count == 0)
					continue;

				List<SerializableGuid> guids = new(map.anchors.Count);
				foreach (MapAnchorEntry entry in map.anchors)
					guids.Add(GuidFromString(entry.guid));

				HashSet<SerializableGuid> localized =
					await registry.ProbeLocalizableAsync(guids, probeTimeoutSeconds, ctkn);

				probeResults[map.id] = localized.Count;
				ProbeResultsChanged.Invoke();

				if (localized.Count == 0)
					continue;

				// Loading is optimistic: one localized anchor is enough to commit, and the
				// fit refines as more references come in. Minting into the map stays gated
				// on agreement, so a mis-load cannot pollute it; the fallback is the user
				// picking the right map manually.
				if (CurrentMap == null && !SyncBus.Active)
					LoadMap(map.id);

				return;
			}
		}

		/// <summary>Re-probes every map, for the picker's "found in this room" badges.</summary>
		public async Awaitable ProbeAllMaps(CancellationToken ctkn = default)
		{
			if (registry == null)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();

				if (map.anchors.Count == 0)
				{
					probeResults[map.id] = 0;
					continue;
				}

				List<SerializableGuid> guids = new(map.anchors.Count);
				foreach (MapAnchorEntry entry in map.anchors)
					guids.Add(GuidFromString(entry.guid));

				HashSet<SerializableGuid> localized =
					await registry.ProbeLocalizableAsync(guids, probeTimeoutSeconds, ctkn);

				probeResults[map.id] = localized.Count;
				ProbeResultsChanged.Invoke();
			}
		}

		// ------- identity ------------------------------------------

		private void PublishIdentity()
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || CurrentMap == null)
				return;

			FixedString64Bytes name = default;
			name.CopyFromTruncated(CurrentMap.name ?? "");

			mapIdentity.Value = new MapIdentity
			{
				id = Guid.ParseExact(CurrentMap.id, "N"),
				version = Guid.ParseExact(CurrentMap.version, "N"),
				name = name,
				hasTags = CurrentMap.HasTags,
			};
		}

		// ------- helpers -------------------------------------------

		private static string GuidToString(SerializableGuid guid)
		{
			return guid.guid.ToString("N");
		}

		private static SerializableGuid GuidFromString(string s)
		{
			return new SerializableGuid(Guid.ParseExact(s, "N"));
		}
	}
}
