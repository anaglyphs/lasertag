using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Maps
{
	public enum SpaceRole { Offline, Host, Client }

	/// <summary>
	/// The scene component that owns the space and map currently shown, the space list,
	/// autosave and startup discovery. Its role follows the sync bus: offline and the host own
	/// the shown space and save it; a client renders the host's and saves nothing of it.
	/// <see cref="Space"/> and <see cref="Map"/> are the live documents, read-only to callers;
	/// every change goes through a command here or on <see cref="Alignment"/>.
	/// </summary>
	/// <remarks>Awake builds only what this component owns; wiring to other components waits for Start.</remarks>
	public sealed class SpaceManager : MonoBehaviour
	{
		public static SpaceManager Instance { get; private set; }
		[SerializeField] private ColocationManager colocationManager;
		[SerializeField] private MapObjectDatabase objectDatabase;
		[SerializeField] private float probeTimeoutSeconds = 8f;
		[SerializeField] private float saveDebounceSeconds = 2f;

		public enum AlignmentError { RequestRejected, SharingFailed, SharingUnsupported }

		public static event Action<AlignmentError> AlignmentErrorRaised = delegate { };
		/// <summary>The shown space, or its name, alignment settings or references.</summary>
		public static event Action SpaceChanged = delegate { };
		/// <summary>The shown map, or its name.</summary>
		public static event Action MapChanged = delegate { };
		/// <summary>The world frame was rebased: scans and visual maps measured in the old one are gone.</summary>
		public static event Action FrameChanged = delegate { };
		/// <summary>The space list or the presence results changed.</summary>
		public static event Action SpacesChanged = delegate { };

		private SpaceStore store;
		private readonly List<Space> spaces = new();
		private Space space;
		private Map map;
		private VisualMapPersistence visualMaps;
		private CancellationTokenSource lifetime;
		private CancellationTokenSource shown;
		private bool dirty, forceVisualSave, shuttingDown;
		private float saveDue;

		public SpaceAlignment Alignment { get; private set; }
		public MapObjects Objects { get; private set; }
		public SpaceDiscovery Discovery { get; private set; }
		public SpaceStore Store => store;

		/// <summary>The shown space; null until startup finds or makes one.</summary>
		public Space Space => space;
		/// <summary>The shown map; null without a space, or after its map was deleted.</summary>
		public Map Map => map;
		/// <summary>Every saved space, most recently used first.</summary>
		public IReadOnlyList<Space> Spaces => spaces;
		public SpaceRole Role => !SyncBus.Active ? SpaceRole.Offline : SyncBus.IsAuthority ? SpaceRole.Host : SpaceRole.Client;
		/// <summary>The host is replacing the session's map; edits and readiness wait.</summary>
		public bool IsChangingMap { get; private set; }
		public SpaceFrame Frame => space?.Frame ?? default;
		/// <summary>New each time a map is shown; edit requests from a previous showing are refused.</summary>
		public Guid MapRevision { get; private set; }

		private bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;
		private static string L(string key) => MenuCopy.Get("Map", key);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			Instance = null;
			AlignmentErrorRaised = delegate { };
			SpaceChanged = delegate { };
			MapChanged = delegate { };
			FrameChanged = delegate { };
			SpacesChanged = delegate { };
		}

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
			lifetime = new CancellationTokenSource();
			store = new SpaceStore(SpaceStore.DefaultDirectory);
			if (CatalogV2Migrator.NeedsMigration(CatalogV2Migrator.DefaultV2Root, store))
				CatalogV2Migrator.Migrate(CatalogV2Migrator.DefaultV2Root, store);
			spaces.AddRange(store.LoadAll());
			visualMaps = new VisualMapPersistence(store);
			Alignment = new SpaceAlignment(this, colocationManager, store);
			Objects = new MapObjects(this, objectDatabase);
			Discovery = new SpaceDiscovery(colocationManager, visualMaps, probeTimeoutSeconds);
		}

		private void Start()
		{
			Objects.Register();
			Alignment.Register();
			colocationManager.AnchorProvider.ErrorRaised += OnAnchorError;
			colocationManager.SourceChanged += RaiseSpaceChanged;
			colocationManager.MethodChanged += RaiseSpaceChanged;
			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
			MapObject.LocalEditOccurred += MarkDirty;
			MapObject.Added += OnObjectChanged;
			MapObject.Removed += OnObjectChanged;
			MainXRRig.Recentered += OnRecentered;
			SaveVisualMapsPeriodically(lifetime.Token);
			Startup(lifetime.Token);
		}

		private void Update()
		{
			if (dirty && Time.unscaledTime >= saveDue) Save();
		}

		private void OnDestroy()
		{
			if (Instance != this) return;
			shuttingDown = true;
			Save();
			shown?.Cancel();
			lifetime.Cancel();
			colocationManager.AnchorProvider.ErrorRaised -= OnAnchorError;
			colocationManager.SourceChanged -= RaiseSpaceChanged;
			colocationManager.MethodChanged -= RaiseSpaceChanged;
			SyncBus.Activated -= OnBusActivated;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			MapObject.LocalEditOccurred -= MarkDirty;
			MapObject.Added -= OnObjectChanged;
			MapObject.Removed -= OnObjectChanged;
			MainXRRig.Recentered -= OnRecentered;
			Alignment.Unregister();
			Objects.Unregister();
			shown?.Dispose();
			lifetime.Dispose();
			Instance = null;
		}

		private void OnApplicationQuit()
		{
			shuttingDown = true;
			Save();
		}

		private void OnApplicationPause(bool paused)
		{
			if (!paused) return;
			forceVisualSave = true;
			Save(synchronousVisual: true);
		}

		private static void RaiseSpaceChanged() => SpaceChanged.Invoke();
		private static void ReportAlignmentRejection() => AlignmentErrorRaised.Invoke(AlignmentError.RequestRejected);

		private static void OnAnchorError(SpatialAnchorColocationConstraintProvider.Error error) =>
			AlignmentErrorRaised.Invoke(error == SpatialAnchorColocationConstraintProvider.Error.SharingFailed
				? AlignmentError.SharingFailed : AlignmentError.SharingUnsupported);

		private void OnObjectChanged(MapObject _) => MarkDirty();

		private void OnRecentered()
		{
			Discovery.Invalidate();
			colocationManager.TrackingOriginChanged();
			SpacesChanged.Invoke();
		}

		// ------- showing ---------------------------------------------

		/// <summary>Shows a space and one of its maps: a new frame, so colocation starts over.</summary>
		private void Show(Space next, Map nextMap)
		{
			shown?.Cancel();
			shown?.Dispose();
			shown = new CancellationTokenSource();
			space = next;
			if (space != null) space.lastUsed = DateTime.UtcNow.Ticks;
			SetMap(nextMap);
			colocationManager.InvalidateAlignment();
			Alignment.Apply(space, preserve: false);
			FrameChanged.Invoke();
			if (space != null && Role != SpaceRole.Client) visualMaps.LoadAndRelocalize(space, shown.Token);
			SpaceChanged.Invoke();
			MapChanged.Invoke();
		}

		private void SetMap(Map next)
		{
			map = next;
			if (map != null) map.lastUsed = DateTime.UtcNow.Ticks;
			MapRevision = Guid.NewGuid();
			Objects.Replace(map?.objects ?? new List<MapObjectRecord>());
		}

		private void ShowMap(Map next)
		{
			SetMap(next);
			MapChanged.Invoke();
		}

		/// <summary>A command changed the shown space's alignment or references: saved, re-applied, and announced.</summary>
		internal void SpaceAlignmentChanged(bool rebase)
		{
			if (space == null) return;
			if (rebase) Rebase();
			else Alignment.Apply(space, preserve: true);
			Save();
			SpaceChanged.Invoke();
		}

		/// <summary>The providers changed the shown space's references (a minted anchor, a chosen pair).</summary>
		internal void SpaceReferencesChanged()
		{
			MarkDirty();
			SpaceChanged.Invoke();
		}

		// stored poses stop meaning the same place: a new frame id, and colocation starts over
		private void Rebase()
		{
			space.frameId = Space.NewId();
			visualMaps.Invalidate();
			store.DeleteVisualMap(space.id);
			colocationManager.InvalidateAlignment();
			Alignment.Apply(space, preserve: false);
			FrameChanged.Invoke();
		}

		// ------- saving ----------------------------------------------

		/// <summary>Writes the shown space soon, coalescing a burst of edits into one write.</summary>
		public void MarkDirty()
		{
			if (dirty) return;
			dirty = true;
			saveDue = Time.unscaledTime + saveDebounceSeconds;
		}

		/// <summary>Captures the scene into the shown map and writes the space. A client never saves the host's space.</summary>
		public bool Save() => Save(synchronousVisual: false);

		// the outgoing space's visual map is written whatever the save interval says
		private void SaveBeforeLeaving()
		{
			forceVisualSave = true;
			Save();
		}

		private bool Save(bool synchronousVisual)
		{
			dirty = false;
			if (space == null || Role == SpaceRole.Client) return true;
			if (map != null && Objects.TryCapture(out List<MapObjectRecord> records)) map.objects = records;
			if (!store.Save(space))
			{
				if (!shuttingDown) MarkDirty();
				return false;
			}
			visualMaps.Save(space, forceVisualSave || shuttingDown, synchronousVisual || shuttingDown);
			forceVisualSave = false;
			return true;
		}

		private async void SaveVisualMapsPeriodically(CancellationToken token)
		{
			try
			{
				while (!token.IsCancellationRequested)
				{
					await Awaitable.WaitForSecondsAsync(10f, token);
					if (space != null && Role != SpaceRole.Client && Alignment.IsAligned) visualMaps.Save(space, false, false);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}

		// ------- space commands --------------------------------------

		private Space FindSpace(string id) => spaces.Find(s => s.id == id);

		private string ManageBlocker() =>
			Role == SpaceRole.Client ? L("blocker.only-the-host-can-change-the-map") :
			RoundInProgress ? L("blocker.not-during-a-round") : null;

		public string LoadSpaceBlocker(string id)
		{
			string blocker = ManageBlocker();
			if (blocker != null) return blocker;
			if (FindSpace(id) == null) return L("blocker.map-is-missing");
			if (!HeadsetConfiguration.IsOperatorDevice && Discovery.GetPresence(id) == MapPresence.Elsewhere) return L("blocker.map-belongs-to-another-room");
			return null;
		}

		public bool LoadSpace(string id)
		{
			if (LoadSpaceBlocker(id) != null) return false;
			Space target = FindSpace(id);
			if (target == space) return true;
			SaveBeforeLeaving();
			Show(target, MostRecentMap(target) ?? AddMap(target));
			spaces.Sort(ByLastUsed);
			SpacesChanged.Invoke();
			return Save();
		}

		public string NewSpaceBlocker() => ManageBlocker();

		public bool NewSpace()
		{
			if (NewSpaceBlocker() != null) return false;
			SaveBeforeLeaving();
			Space fresh = Space.Create(GenerateSpaceName());
			spaces.Insert(0, fresh);
			Show(fresh, AddMap(fresh));
			SpacesChanged.Invoke();
			return Save();
		}

		public string DeleteSpaceBlocker(string id) =>
			ManageBlocker() ?? (FindSpace(id) == null ? L("blocker.map-is-missing") :
			SyncBus.Active && space?.id == id ? L("space.busy") : null);

		public bool DeleteSpace(string id)
		{
			if (DeleteSpaceBlocker(id) != null) return false;
			Space target = FindSpace(id);
			List<string> guids = new();
			foreach (AnchorRef anchor in target.anchors) guids.Add(anchor.guid);
			PrivateReferences references = store.LoadPrivate(id);
			if (references != null) foreach (TagAnchorRef anchor in references.tagAnchors) guids.Add(anchor.guid);
			spaces.Remove(target);
			if (target == space) Show(null, null);
			Alignment.EraseUnreferenced(guids);
			bool deleted = store.Delete(id);
			Discovery.Forget(id);
			SpacesChanged.Invoke();
			if (space == null) Startup(lifetime.Token);
			return deleted;
		}

		public string RenameSpaceBlocker() =>
			space == null ? L("space.load-first") : Role == SpaceRole.Client ? L("blocker.only-the-host-can-rename-the-map") : null;

		public bool RenameSpace(string name)
		{
			if (RenameSpaceBlocker() != null || string.IsNullOrWhiteSpace(name)) return false;
			space.name = TrimName(name);
			SpaceChanged.Invoke();
			SpacesChanged.Invoke();
			return Save();
		}

		public string ResetAlignmentBlocker() =>
			space == null ? L("space.load-first") : Role != SpaceRole.Offline ? L("space.busy") : null;

		/// <summary>Forgets every reference of the shown space and gives it a new frame; the maps keep their poses.</summary>
		public bool ResetAlignment()
		{
			if (ResetAlignmentBlocker() != null) return false;
			Alignment.ForgetReferences();
			Rebase();
			SpaceChanged.Invoke();
			return Save();
		}

		// ------- map commands ----------------------------------------

		private Space OwnerOf(string mapId) => spaces.Find(s => s.FindMap(mapId) != null);

		public string LoadMapBlocker(string id)
		{
			string blocker = ManageBlocker();
			if (blocker != null) return blocker;
			Space owner = OwnerOf(id);
			if (owner == null) return L("blocker.map-is-missing");
			if (owner != space) return LoadSpaceBlocker(owner.id);
			return map?.id == id ? L("blocker.already-loaded") : null;
		}

		/// <summary>Shows a map, loading its space first when it belongs to another one.</summary>
		public bool LoadMap(string id)
		{
			if (LoadMapBlocker(id) != null) return false;
			Space owner = OwnerOf(id);
			if (owner != space)
			{
				SaveBeforeLeaving();
				Show(owner, owner.FindMap(id));
				spaces.Sort(ByLastUsed);
				SpacesChanged.Invoke();
			}
			else
			{
				Save();
				ShowMap(owner.FindMap(id));
			}
			return Save();
		}

		public string NewMapBlocker() => ManageBlocker() ?? (space == null ? L("space.load-first") : null);

		public bool NewMap()
		{
			if (NewMapBlocker() != null) return false;
			Save();
			ShowMap(AddMap(space));
			SpacesChanged.Invoke();
			return Save();
		}

		public bool DuplicateMap(string id)
		{
			if (NewMapBlocker() != null) return false;
			Map source = space.FindMap(id);
			if (source == null) return false;
			Save();
			Map copy = Map.Create(TrimName(source.name + " (copy)"));
			copy.objects.AddRange(source.objects);
			space.maps.Add(copy);
			ShowMap(copy);
			SpacesChanged.Invoke();
			return Save();
		}

		public string DeleteMapBlocker(string id) =>
			id == null ? L("blocker.no-map-selected") : ManageBlocker() ?? (OwnerOf(id) == null ? L("blocker.map-is-missing") :
			SyncBus.Active && map?.id == id ? L("blocker.cannot-delete-the-active-session-map") : null);

		public bool DeleteMap(string id)
		{
			if (DeleteMapBlocker(id) != null) return false;
			Space owner = OwnerOf(id);
			Map target = owner.FindMap(id);
			owner.maps.Remove(target);
			if (target == map) ShowMap(null);
			bool saved = owner == space ? Save() : store.Save(owner);
			SpacesChanged.Invoke();
			return saved;
		}

		public string RenameMapBlocker() =>
			map == null ? L("blocker.no-map-selected") : Role == SpaceRole.Client ? L("blocker.only-the-host-can-rename-the-map") : null;

		public bool RenameMap(string name)
		{
			if (RenameMapBlocker() != null || string.IsNullOrWhiteSpace(name)) return false;
			map.name = TrimName(name);
			MapChanged.Invoke();
			SpacesChanged.Invoke();
			return Save();
		}

		private Map AddMap(Space owner)
		{
			Map created = Map.Create(GenerateMapName(owner));
			owner.maps.Add(created);
			return created;
		}

		private static Map MostRecentMap(Space owner)
		{
			Map best = null;
			foreach (Map candidate in owner.maps)
				if (best == null || candidate.lastUsed > best.lastUsed) best = candidate;
			return best;
		}

		private static int ByLastUsed(Space a, Space b) =>
			a.lastUsed == b.lastUsed ? string.CompareOrdinal(a.id, b.id) : b.lastUsed.CompareTo(a.lastUsed);

		private static string TrimName(string name)
		{
			string trimmed = name.Trim();
			return trimmed.Substring(0, Mathf.Min(Space.MaxNameLength, trimmed.Length));
		}

		private string GenerateSpaceName()
		{
			int highest = 0;
			foreach (Space candidate in spaces)
				if (candidate.name != null && candidate.name.StartsWith("Space ") && int.TryParse(candidate.name.Substring(6), out int n)) highest = Math.Max(highest, n);
			return $"Space {highest + 1}";
		}

		private string GenerateMapName(Space owner)
		{
			int highest = 0;
			foreach (Space candidate in spaces)
				foreach (Map existing in candidate.maps)
					if (existing.name != null && existing.name.StartsWith("Map ") && int.TryParse(existing.name.Substring(4), out int n)) highest = Math.Max(highest, n);
			return $"Map {highest + 1}";
		}

		// ------- sessions (the sync itself arrives with SpaceSync) ---

		private void OnBusActivated()
		{
			Discovery.Invalidate();
			if (SyncBus.IsAuthority)
			{
				Save();
				Objects.SpawnLocalObjects();
			}
			SpacesChanged.Invoke();
			SpaceChanged.Invoke();
		}

		private void OnAuthorityChanged(bool authority)
		{
			if (authority) Objects.SpawnLocalObjects();
			SpaceChanged.Invoke();
		}

		private void OnBusDeactivated()
		{
			Save();
			SpaceChanged.Invoke();
			SpacesChanged.Invoke();
		}

		// ------- startup ---------------------------------------------

		// A healthy completed probe may reuse one blank space. A failed probe never means an empty room.
		private async void Startup(CancellationToken token)
		{
			if (HeadsetConfiguration.IsOperatorDevice)
			{
				if (space == null && Role != SpaceRole.Client && spaces.Count > 0) LoadSpace(spaces[0].id);
				return;
			}
#if UNITY_EDITOR
			if (MainXRRig.Instance && !XRSettings.enabled)
			{
				RestoreLastSpaceForSimulation();
				return;
			}
#endif
			try
			{
				while (!token.IsCancellationRequested && Role != SpaceRole.Client && space == null)
				{
					await ProbeAndLoad(token);
					if (space == null) await Awaitable.WaitForSecondsAsync(2f, token);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}

#if UNITY_EDITOR
		/// <summary>Simulation has no room to recognize: the last space, or a new one.</summary>
		public bool RestoreLastSpaceForSimulation()
		{
			if (!MainXRRig.Instance || XRSettings.enabled || SyncBus.Active || space != null) return true;
			return spaces.Count > 0 ? LoadSpace(spaces[0].id) : NewSpace();
		}
#endif

		private async Awaitable ProbeAndLoad(CancellationToken token)
		{
			if (Role == SpaceRole.Client || space != null || !SpaceAlignment.TrackingReady) return;
			ProbeResult result = await Discovery.ProbeAsync(spaces, token);
			SpacesChanged.Invoke();
			if (token.IsCancellationRequested || Role == SpaceRole.Client || space != null) return;
			switch (result.outcome)
			{
				case ProbeOutcome.Matches:
					LoadSpace(result.best.id);
					return;
				case ProbeOutcome.NoMatches:
					Space draft = FindBlankSpace();
					if (draft != null) LoadSpace(draft.id);
					else NewSpace();
					return;
			}
		}

		/// <summary>The most recent space with nothing in it: no references, no visual map, only empty maps.</summary>
		private Space FindBlankSpace()
		{
			foreach (Space candidate in spaces)
			{
				if (candidate.HasReferences || store.HasVisualMap(candidate.id)) continue;
				bool empty = true;
				foreach (Map existing in candidate.maps) empty &= existing.objects.Count == 0;
				if (empty) return candidate;
			}
			return null;
		}

		/// <summary>Asks the room which saved spaces are here, for the picker. Offline only; a session's space is the host's.</summary>
		public async Awaitable ProbeSpaces(CancellationToken token = default)
		{
			if (Role != SpaceRole.Offline) return;
			await Discovery.ProbeAsync(spaces, token);
			SpacesChanged.Invoke();
		}

		public MapPresence GetSpacePresence(string id) => Discovery.GetPresence(id);
		public bool SupportsEnvironmentProbing => Discovery.SupportsEnvironmentProbing;
	}
}
