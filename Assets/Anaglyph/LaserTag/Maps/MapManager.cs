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
	/// Owns maps at runtime: which map is loaded, what belongs to it, and every flow that
	/// reads or writes that. Anchor plumbing lives in <see cref="AnchorReferenceProvider"/>;
	/// this class decides *which* anchors a map holds and *where* they belong, and the
	/// provider handles creating, saving, sharing, and presenting them to colocators.
	///
	/// Shared anchors are transport, not storage: every anchor this device ends up with —
	/// minted locally or downloaded from a peer — is saved to local storage and recorded in
	/// this device's copy of the map. The cloud is only how an anchor gets between headsets
	/// the first time.
	/// </summary>
	[DefaultExecutionOrder(-100)]
	public class MapManager : MonoBehaviour
	{
		public static MapManager Instance { get; private set; }

		[SerializeField] private AnchorReferenceProvider anchorProvider;

		[Tooltip("Every placeable map object; also how a saved map's prefab ids resolve")]
		[SerializeField] private MapObjectDatabase objectDatabase;

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

		private readonly SyncVariable<MapIdentity> mapIdentity = new("map.identity");
		private readonly SyncDictionary<Guid, Pose> canonAnchors = new("map.anchors.canon");
		private readonly SyncDictionary<int, Pose> canonTags = new("map.tags.canon");

		public SyncDictionary<int, Pose> CanonTags => canonTags;

		private CancellationTokenSource lifetimeCtknSrc;

		private bool savePending;

		// The frame a map was authored in stays trustworthy as long as tracking has been
		// physically continuous since its creation — no sleep, no recenter. This is what
		// lets a brand-new map register its first references at all: with nothing to align
		// to yet, continuity is the only ground truth there is.
		private bool frameContinuous;

		private int agreeingReferenceCount;
		private float meanReferenceError;
		private readonly List<ColocationReference> referenceScratch = new();

		// Snapshots for loops whose bodies can write back into the map's own lists.
		private readonly List<MapAnchorEntry> publishScratch = new();
		private readonly List<MapTagEntry> tagPublishScratch = new();

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

			if (!anchorProvider)
				anchorProvider = FindFirstObjectByType<AnchorReferenceProvider>();

			if (!objectDatabase)
				Debug.LogError("MapManager has no map object database — saved maps cannot " +
					"restore their objects.", this);

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

			if (anchorProvider)
				anchorProvider.AnchorPersisted += OnAnchorPersisted;
		}

		private void Start()
		{
			MintLoop(lifetimeCtknSrc.Token);

			if (anchorProvider && anchorProvider.IsAvailable)
				StartupProbe(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			// Never snapshot here: teardown destroys objects in arbitrary order, and the
			// pause/quit callbacks already saved while the world was intact. This only
			// flushes non-object state (anchor records) accrued since.
			SaveCurrentMapQuietly();

			if (anchorProvider)
				anchorProvider.AnchorPersisted -= OnAnchorPersisted;

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
		}

		// Once the app starts quitting, scene objects die in arbitrary order — MapObject.All
		// may already be empty by the time any later save runs. Snapshotting that emptiness
		// over the real object list is how a map gets silently wiped, so no snapshot may be
		// taken past this point.
		private bool isQuitting;

		private void OnApplicationQuit()
		{
			// The last moment every object is reliably still alive: take the final snapshot
			// here, then freeze it.
			SaveCurrentMap();
			isQuitting = true;
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

			if (anchorProvider)
				anchorProvider.GetColocationReferences(referenceScratch);

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

		// ------- current map lifecycle -----------------------------

		/// <summary>
		/// Loads a map, adopting its frame: existing map objects are torn down and the map's
		/// own are instantiated at their canon poses, and its anchors are committed to real
		/// anchors for the per-frame fit. Only callable while disconnected — in a session
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

			if (anchorProvider)
				foreach (MapAnchorEntry entry in map.anchors)
					anchorProvider.Adopt(GuidFromString(entry.guid), entry.canonPose,
						AnchorSource.Local);

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

			if (anchorProvider)
				anchorProvider.ReleaseAll();

			RemoveMapObjects();
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
			if (anchorProvider)
				foreach (string orphan in orphanedAnchors)
					_ = anchorProvider.EraseAsync(GuidFromString(orphan));
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

			// During quit teardown the world is half-destroyed; keep the last good snapshot
			// (taken in OnApplicationQuit) instead of overwriting it with what's left.
			if (!isQuitting)
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
				if (!obj)
					continue; // destroyed this frame (session or scene teardown in progress)

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
			return objectDatabase != null ? objectDatabase.FindPrefab(prefabId) : null;
		}

		/// <summary>
		/// Clears the world of map objects, as far as this device is permitted to: everything
		/// this device controls goes — local-only objects and ones it spawned alike, the latter
		/// by despawn so they leave every peer. Objects another peer spawned are that peer's to
		/// remove; see <see cref="MapObject.RemoveIfPermitted"/>.
		/// </summary>
		private static void RemoveMapObjects()
		{
			// Copy: removing mutates MapObject.All.
			List<MapObject> objects = new(MapObject.All);

			foreach (MapObject obj in objects)
			{
				if (!obj)
					continue;

				obj.RemoveIfPermitted();
			}
		}

		// ------- session flows -------------------------------------

		// Set once the authority-side session start ran; adoption uses its own idempotence.
		private bool authoritySessionStarted;

		// Whether this peer has taken the session's map over its own yet. The first adopt of a
		// session is the one that clears the local world; later identity updates are just the
		// host saving content and must leave live session objects alone.
		private bool sessionMapAdopted;

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
		/// tag reference source read one dictionary in both worlds. Non-authority peers never
		/// write — their canon tags arrive through the sync.
		/// </summary>
		private void MirrorTagsToCanon()
		{
			if (SyncBus.Active && !SyncBus.IsAuthority)
				return;

			canonTags.Clear();

			if (CurrentMap == null)
				return;

			// Snapshot for the same reason as PublishAndShareAnchors: writing to a synced
			// endpoint can come back around into the list being read.
			tagPublishScratch.Clear();
			tagPublishScratch.AddRange(CurrentMap.tags);

			foreach (MapTagEntry tag in tagPublishScratch)
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
			if (!anchorProvider)
				return;

			anchorProvider.WarnIfSharingUnsupported();

			// Publishing feeds straight back into the map: the authority applies its own
			// write immediately, and AdoptCanonAnchor records it. Iterate a snapshot so
			// that write-back can't invalidate this enumeration.
			publishScratch.Clear();
			publishScratch.AddRange(CurrentMap.anchors);

			foreach (MapAnchorEntry entry in publishScratch)
			{
				Guid guid = GuidFromString(entry.guid);

				canonAnchors.RequestSet(guid, entry.canonPose);
				anchorProvider.Adopt(guid, entry.canonPose, AnchorSource.Local);
				_ = anchorProvider.ShareAsync(guid, lifetimeCtknSrc.Token);
			}
		}

		private void OnBusDeactivated()
		{
			authoritySessionStarted = false;
			sessionMapAdopted = false;

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

			RemoveMapObjects();
			InstantiateMapObjects(CurrentMap);

			// Session teardown reset the sync endpoints; restore the offline mirror so the
			// tag reference source keeps its canon poses.
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

			bool firstAdopt = !sessionMapAdopted;
			sessionMapAdopted = true;

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

				// Matching ids do not make our objects the session's objects: ours were
				// instantiated locally, the host's arrive spawned, and keeping both is how the
				// map comes up doubled. The session's copies are the real ones — drop ours on
				// the way in. Only on the way in: past the first adopt, live objects are
				// session content (including anything this peer placed) and must survive.
				if (firstAdopt)
					RemoveMapObjects();

				return;
			}

			// Different map: unload ours first — there is only one world space.
			if (CurrentMap != null)
				UnloadCurrentMap();
			else
				RemoveMapObjects(); // never inject stale local objects into a session

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

		private void OnCanonAnchorsChanged(SyncDictionary<Guid, Pose>.EventData data)
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
			foreach ((Guid guid, Pose pose) in canonAnchors)
				AdoptCanonAnchor(guid, pose);
		}

		private void AdoptCanonAnchor(Guid guid, Pose canonPose)
		{
			if (CurrentMap == null)
				return;

			CurrentMap.SetAnchor(GuidToString(guid), canonPose);

			// Local storage is tried first; the cloud download only happens for anchors this
			// device has never saved.
			if (anchorProvider)
				anchorProvider.Adopt(guid, canonPose, AnchorSource.Any);
		}

		// An anchor became durable on this device — make sure the map file records it.
		private void OnAnchorPersisted(Guid guid)
		{
			SaveCurrentMapQuietly();
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

		// ------- anchor minting ------------------------------------

		private async void MintLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.FixedUpdateAsync(ctkn);

					if (!anchorProvider || !anchorProvider.IsAvailable) continue;
					if (CurrentMap == null || anchorProvider.IsMinting) continue;
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

			// A roaming anchor defines its own canon pose: it is created where it is, and
			// where it is IS where it belongs.
			await MintAnchor(feetPose, feetPose, -1, ctkn);
		}

		/// <summary>
		/// Mints an anchor and records it in the current map. In a shared-anchor session it is
		/// also uploaded and its canon pose published — neither waits on the upload.
		/// </summary>
		private async Awaitable MintAnchor(Pose createAt, Pose canon, int tagId,
			CancellationToken ctkn)
		{
			if (!anchorProvider || CurrentMap == null)
				return;

			Guid guid = await anchorProvider.MintAsync(createAt, canon, ctkn);

			if (guid == Guid.Empty || CurrentMap == null)
				return;

			CurrentMap.SetAnchorWithTag(GuidToString(guid), canon, tagId);
			SaveCurrentMapQuietly();

			bool sharedSession = SyncBus.Active &&
				ColocationManager.Instance.Method ==
				ColocationManager.ColocationMethod.MetaSharedAnchor;

			if (sharedSession)
			{
				canonAnchors.RequestSet(guid, canon);
				_ = anchorProvider.ShareAsync(guid, ctkn);
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
			if (!anchorProvider || CurrentMap == null) return;
			if (!CurrentMap.TryGetTag(tagId, out MapTagEntry tag)) return;
			if (!WorldFrameTrusted) return;

			if (!CurrentMap.TryGetAnchorByTag(tagId, out MapAnchorEntry tagAnchor))
			{
				MintTagAnchor(tagId, observedTagPose, tag.canonPose);
				return;
			}

			CorrectTagAnchor(tagId, tag.canonPose, observedTagPose, tagAnchor);
		}

		private async void MintTagAnchor(int tagId, Pose observedTagPose, Pose canonTagPose)
		{
			if (anchorProvider.IsMinting || !tagAnchorMintsInFlight.Add(tagId))
				return;

			try
			{
				// Created AT the observed tag, but its canon pose is the tag's canon pose —
				// so the relative term of the correction formula is identity at creation
				// time. Any residual fit error gets absorbed by the first correction.
				await MintAnchor(observedTagPose, canonTagPose, tagId, lifetimeCtknSrc.Token);
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
				tagAnchorMintsInFlight.Remove(tagId);
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
			Guid anchorGuid = GuidFromString(anchorEntry.guid);

			if (!anchorProvider.TryGetObserved(anchorGuid, out Pose observedAnchor))
				return;

			// Both observations in the same frame; any rig alignment cancels in the relative
			// term.
			Matrix4x4 observedTagMat = Matrix4x4.TRS(
				observedTag.position, observedTag.rotation, Vector3.one);
			Matrix4x4 observedAnchorMat = Matrix4x4.TRS(
				observedAnchor.position, observedAnchor.rotation, Vector3.one);
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

			Pose correctedCanon = new(averagePos, averageRot);

			CurrentMap.SetAnchorWithTag(anchorEntry.guid, correctedCanon, tagId);
			anchorProvider.SetCanon(anchorGuid, correctedCanon);
			SaveCurrentMapQuietly();

			bool sharedSession = SyncBus.Active &&
				ColocationManager.Instance.Method ==
				ColocationManager.ColocationMethod.MetaSharedAnchor;

			if (sharedSession)
				canonAnchors.RequestSet(anchorGuid, correctedCanon);
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
			if (!anchorProvider || !anchorProvider.IsAvailable)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();

				if (map.anchors.Count == 0)
					continue;

				HashSet<Guid> localized =
					await anchorProvider.ProbeAsync(AnchorGuidsOf(map), probeTimeoutSeconds, ctkn);

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
			if (!anchorProvider || !anchorProvider.IsAvailable)
				return;

			foreach (GameMap map in MapStore.GetByLastUsed())
			{
				ctkn.ThrowIfCancellationRequested();

				if (map.anchors.Count == 0)
				{
					probeResults[map.id] = 0;
					continue;
				}

				HashSet<Guid> localized =
					await anchorProvider.ProbeAsync(AnchorGuidsOf(map), probeTimeoutSeconds, ctkn);

				probeResults[map.id] = localized.Count;
				ProbeResultsChanged.Invoke();
			}
		}

		private static List<Guid> AnchorGuidsOf(GameMap map)
		{
			List<Guid> guids = new(map.anchors.Count);

			foreach (MapAnchorEntry entry in map.anchors)
				guids.Add(GuidFromString(entry.guid));

			return guids;
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

		private static string GuidToString(Guid guid)
		{
			return guid.ToString("N");
		}

		private static Guid GuidFromString(string s)
		{
			return Guid.ParseExact(s, "N");
		}
	}
}
