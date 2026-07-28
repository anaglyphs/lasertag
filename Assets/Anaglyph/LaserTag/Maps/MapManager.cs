using Anaglyph.Debugging.Visuals;
using Anaglyph.Netcode;
using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Serialization;

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
		public bool hasTags;
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
		[SerializeField] private ReferenceColocator referenceColocator;

		[Tooltip("Every placeable map object; also how a saved map's prefab ids resolve")]
		[SerializeField] private MapObjectDatabase objectDatabase;

		[Tooltip("How long the room probe lets each anchor try to localize")]
		[SerializeField] private float probeTimeoutSeconds = 8f;

		[Tooltip("Mean reference error above which the fit does not count as agreeing")]
		[SerializeField] private float agreementMaxError = 0.3f;

		public GameMap CurrentMap { get; private set; }
		public event Action<GameMap> CurrentMapChanged = delegate { };

		public IReadOnlyDictionary<string, int> ProbeResults => probeResults;
		private readonly Dictionary<string, int> probeResults = new();
		public event Action ProbeResultsChanged = delegate { };

		private readonly SyncVariable<MapIdentity> mapIdentity = new("map.identity");
		private CancellationTokenSource lifetimeCtknSrc;
		private bool savePending;
		private bool frameContinuous;
		private bool isQuitting;

		private int agreeingReferenceCount;
		private float meanReferenceError;
		private readonly List<ColocationConstraint> referenceScratch = new();
		private readonly List<AnchorReferenceData> anchorImportScratch = new();
		private readonly List<TagReferenceData> tagImportScratch = new();
		private readonly List<TaggedAnchorReferenceData> taggedAnchorScratch = new();

		private bool authoritySessionStarted;
		private bool sessionMapAdopted;

		private void Awake()
		{
			Instance = this;
			lifetimeCtknSrc = new CancellationTokenSource();

			if (!spatialAnchorProvider)
				spatialAnchorProvider = FindFirstObjectByType<SpatialAnchorConstraintProvider>();
			if (!tagProvider)
				tagProvider = FindFirstObjectByType<TagConstraintProvider>();
			if (!referenceColocator)
				referenceColocator = FindFirstObjectByType<ReferenceColocator>();

			if (!objectDatabase)
				Debug.LogError("MapManager has no map object database.", this);

			mapIdentity.Register();
			mapIdentity.Changed += OnMapIdentityChanged;
			mapIdentity.Synced += OnMapIdentitySynced;

			if (spatialAnchorProvider)
			{
				spatialAnchorProvider.ReferencesChanged += OnSpatialAnchorReferencesChanged;
				spatialAnchorProvider.AnchorPersisted += OnSpatialAnchorPersisted;
			}

			if (tagProvider)
			{
				tagProvider.TagsChanged += OnTagReferencesChanged;
				tagProvider.AnchorsChanged += OnTaggedAnchorsChanged;
			}

			SyncBus.Deactivated += OnBusDeactivated;
			MapObject.LocalEditOccurred += OnLocalEdit;
			MainXRRig.Recentered += OnRecentered;
		}

		private void Start()
		{
			if (spatialAnchorProvider && spatialAnchorProvider.IsAvailable)
				StartupProbe(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			SaveCurrentMapQuietly();

			if (tagProvider)
			{
				tagProvider.AnchorsChanged -= OnTaggedAnchorsChanged;
				tagProvider.TagsChanged -= OnTagReferencesChanged;
			}

			if (spatialAnchorProvider)
			{
				spatialAnchorProvider.AnchorPersisted -= OnSpatialAnchorPersisted;
				spatialAnchorProvider.ReferencesChanged -= OnSpatialAnchorReferencesChanged;
			}

			MainXRRig.Recentered -= OnRecentered;
			MapObject.LocalEditOccurred -= OnLocalEdit;
			SyncBus.Deactivated -= OnBusDeactivated;
			mapIdentity.Synced -= OnMapIdentitySynced;
			mapIdentity.Changed -= OnMapIdentityChanged;
			mapIdentity.Unregister();

			lifetimeCtknSrc?.Cancel();
		}

		private void OnApplicationQuit()
		{
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

		// ------- world-frame trust -------------------------------

		public bool WorldFrameTrusted
		{
			get
			{
				if (CurrentMap == null)
					return false;
				if (frameContinuous)
					return true;
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
			referenceColocator?.GetCurrentReferences(referenceScratch);
			agreeingReferenceCount = referenceScratch.Count;

			if (referenceScratch.Count == 0)
			{
				meanReferenceError = 0f;
				return;
			}

			float errorSum = 0f;
			foreach (ColocationConstraint reference in referenceScratch)
				errorSum += Vector3.Distance(reference.observed.position, reference.canon.position);

			meanReferenceError = errorSum / referenceScratch.Count;
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
			MapStore.MarkUsed(map);
			InstantiateMapObjects(map);
			InjectMapIntoProviders(map);
			CurrentMapChanged.Invoke(map);
			return true;
		}

		public void UnloadCurrentMap()
		{
			if (CurrentMap != null)
				SaveCurrentMap();

			CurrentMap = null;
			frameContinuous = false;
			ClearProviderStateForNoMap();
			RemoveMapObjects();
			CurrentMapChanged.Invoke(null);
		}

		public void DeleteMap(string id)
		{
			if (CurrentMap != null && CurrentMap.id == id)
				UnloadCurrentMap();

			List<string> orphanedAnchors = new();
			MapStore.Delete(id, orphanedAnchors);

			if (spatialAnchorProvider)
				foreach (string orphan in orphanedAnchors)
					_ = spatialAnchorProvider.EraseAsync(GuidFromString(orphan));
		}

		private void OnLocalEdit()
		{
			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				InjectMapIntoProviders(CurrentMap);
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

		public void SaveCurrentMap()
		{
			if (CurrentMap == null)
				return;

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

		private void SaveCurrentMapQuietly()
		{
			if (CurrentMap != null)
				MapStore.Save(CurrentMap);
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

		private static void RemoveMapObjects()
		{
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj)
					obj.RemoveIfPermitted();
		}

		// ------- provider persistence adapter --------------------

		private void InjectMapIntoProviders(GameMap map)
		{
			anchorImportScratch.Clear();
			tagImportScratch.Clear();
			taggedAnchorScratch.Clear();

			foreach (MapAnchorEntry entry in map.anchors)
			{
				Guid guid = GuidFromString(entry.guid);
				anchorImportScratch.Add(new AnchorReferenceData(
					guid, entry.canonPose, entry.tagId));
				if (entry.tagId >= 0)
					taggedAnchorScratch.Add(new TaggedAnchorReferenceData(
						guid, entry.tagId, entry.canonPose));
			}

			foreach (MapTagEntry entry in map.tags)
				tagImportScratch.Add(new TagReferenceData(entry.id, entry.canonPose));

			spatialAnchorProvider?.SetReferences(anchorImportScratch);
			tagProvider?.SetReferences(tagImportScratch, taggedAnchorScratch);
		}

		private void InjectAnchorsIntoAnchorProvider()
		{
			if (CurrentMap == null || spatialAnchorProvider == null)
				return;

			anchorImportScratch.Clear();
			foreach (MapAnchorEntry entry in CurrentMap.anchors)
				anchorImportScratch.Add(new AnchorReferenceData(
					GuidFromString(entry.guid), entry.canonPose, entry.tagId));

			spatialAnchorProvider.SetReferences(anchorImportScratch);
		}

		private void ClearProviderStateForNoMap()
		{
			anchorImportScratch.Clear();
			tagImportScratch.Clear();
			taggedAnchorScratch.Clear();

			if (SyncBus.IsAuthority)
			{
				spatialAnchorProvider?.SetReferences(anchorImportScratch);
				tagProvider?.SetReferences(tagImportScratch, taggedAnchorScratch);
			}
			else
			{
				tagProvider?.SetLocalAnchors(taggedAnchorScratch);
			}
		}

		private bool AnchorProviderOwnsCurrentState => CurrentMap != null &&
			SessionReferencesBelongToCurrentMap &&
			(SyncBus.Active
				? ColocationManager.Instance != null &&
				  ColocationManager.Instance.Method == ColocationManager.ColocationMethod.MetaSharedAnchor
				: spatialAnchorProvider != null && spatialAnchorProvider.IsRunning);

		private bool TagProviderOwnsCurrentState => CurrentMap != null &&
			SessionReferencesBelongToCurrentMap &&
			(SyncBus.Active
				? ColocationManager.Instance != null &&
				  ColocationManager.Instance.Method == ColocationManager.ColocationMethod.AprilTag
				: tagProvider != null && tagProvider.IsRunning);

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

		private void OnSpatialAnchorReferencesChanged()
		{
			if (!AnchorProviderOwnsCurrentState)
				return;

			SnapshotAnchorProviderToMap();
			SaveCurrentMapQuietly();
		}

		private void SnapshotAnchorProviderToMap()
		{
			if (CurrentMap == null || spatialAnchorProvider == null)
				return;

			CurrentMap.anchors.Clear();
			foreach ((Guid guid, AnchorReferenceState state) in spatialAnchorProvider.References)
			{
				CurrentMap.SetAnchorWithTag(
					GuidToString(guid), state.canonPose, state.bindingId);
			}
		}

		private void OnTagReferencesChanged()
		{
			if (!TagProviderOwnsCurrentState)
				return;

			SnapshotTagProviderToMap();
			SaveCurrentMapQuietly();
		}

		private void SnapshotTagProviderToMap()
		{
			if (CurrentMap == null || tagProvider == null)
				return;

			CurrentMap.tags.Clear();
			foreach ((int tagId, Pose canon) in tagProvider.RegisteredTags)
				CurrentMap.SetTag(tagId, canon);
		}

		private void OnTaggedAnchorsChanged()
		{
			if (!TagProviderOwnsCurrentState)
				return;

			SnapshotTaggedAnchorsToMap();
			if (!SyncBus.Active)
				InjectAnchorsIntoAnchorProvider();
			SaveCurrentMapQuietly();
		}

		private void SnapshotTaggedAnchorsToMap()
		{
			if (CurrentMap == null || tagProvider == null)
				return;

			taggedAnchorScratch.Clear();
			tagProvider.GetLocalAnchorReferences(taggedAnchorScratch);
			CurrentMap.anchors.Clear();

			foreach (TaggedAnchorReferenceData entry in taggedAnchorScratch)
				CurrentMap.SetAnchorWithTag(
					GuidToString(entry.guid), entry.canonPose, entry.tagId);
		}

		private void OnSpatialAnchorPersisted(Guid _)
		{
			SaveCurrentMapQuietly();
		}

		// ------- session flows -----------------------------------

		private void AuthoritySessionStart()
		{
			if (authoritySessionStarted) return;
			authoritySessionStarted = true;

			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				InjectMapIntoProviders(CurrentMap);
				CurrentMapChanged.Invoke(CurrentMap);
			}

			CurrentMap.lastUsed = DateTime.UtcNow.Ticks;
			SaveCurrentMap();

			foreach (MapObject obj in new List<MapObject>(MapObject.All))
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

			GameMap previousMap = CurrentMap;
			if (previousMap != null)
			{
				SaveCurrentMap();
				CurrentMap = null;
				RemoveMapObjects();
			}
			else
			{
				RemoveMapObjects();
			}

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
				taggedAnchorScratch.Clear();
				foreach (MapAnchorEntry entry in CurrentMap.anchors)
					if (entry.tagId >= 0)
						taggedAnchorScratch.Add(new TaggedAnchorReferenceData(
							GuidFromString(entry.guid), entry.tagId, entry.canonPose));

				tagProvider?.SetLocalAnchors(taggedAnchorScratch);
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
		}

		// ------- tag authoring -----------------------------------

		public bool RegisterTag(int tagId, Pose worldPose)
		{
			if (SyncBus.Active)
				return false;

			if (CurrentMap == null)
			{
				CurrentMap = MapStore.CreateNew();
				frameContinuous = true;
				InjectMapIntoProviders(CurrentMap);
				CurrentMapChanged.Invoke(CurrentMap);
			}

			if (!WorldFrameTrusted)
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

			CurrentMap.anchors.RemoveAll(entry => entry.tagId == tagId);
			MapStore.MarkEdited(CurrentMap);
			InjectMapIntoProviders(CurrentMap);
			SaveCurrentMap();
			CurrentMapChanged.Invoke(CurrentMap);
			return true;
		}

		private void InjectTagsIntoProvider()
		{
			tagImportScratch.Clear();
			foreach (MapTagEntry entry in CurrentMap.tags)
				tagImportScratch.Add(new TagReferenceData(entry.id, entry.canonPose));

			tagProvider?.SetRegisteredTags(tagImportScratch);
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
				guids.Add(GuidFromString(entry.guid));
			return guids;
		}

		// ------- identity and helpers ----------------------------

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

		private static string GuidToString(Guid guid) => guid.ToString("N");
		private static Guid GuidFromString(string value) => Guid.ParseExact(value, "N");
	}
}
