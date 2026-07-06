using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.Netcode;
using Unity.Mathematics;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	// Colocation by fitting the tracking space across many shared anchors.
	//
	// Anchors are NOT NetworkObjects: AR Foundation owns the anchor GameObjects locally
	// (and updates their poses itself, which avoids the frame-lag of
	// OVRSpatialAnchor.UpdateTransform). We replicate only DATA — a SyncDictionary
	// mapping each anchor's Meta share-group GUID to its canonical (host-frame) pose.
	//
	// The spawner (the SyncBus authority) scatters anchors as it explores (one whenever
	// it strays more than spawnEveryMeters from every existing anchor): it creates an
	// ARAnchor, shares it to a fresh group, and adds an entry. Every client loads
	// each entry's group, takes the single anchor inside, and registers it locally.
	// Each frame we pair every entry's canon pose with its live tracked pose and fit:
	// 1 anchor aligns directly (full pose), 2+ run through IterativeClosestPoint for a
	// yaw+translation best fit.
	[DefaultExecutionOrder(999)]
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		public static MetaAnchorColocator Instance { get; private set; }

		[SerializeField] private bool anchorBreadcrumb = false;
		[SerializeField] private float spawnEveryMeters = 2f;

		[Range(0f, 1f)] [SerializeField] private float alignLerp = 1f;

		[SerializeField] private bool requireTracking = true;

		[SerializeField] private int stableFramesRequired = 8;
		[SerializeField] private float stableMoveThreshold = 0.02f;
		[SerializeField] private float stableRotateThreshold = 1.5f;

		[SerializeField] private bool anchorStreaming = false;
		[SerializeField] private float unloadDistance = 10f;
		[SerializeField] private float reloadDistance = 5f;

		[Header("Debug")] [SerializeField] private GameObject canonMarkerPrefab;

		public event Action Colocated = delegate { };

		// Share-group GUID -> canon (shared-frame) pose.
		private readonly SyncDictionary<Guid, Pose> canonPosesSync = new("colo.anchors");

		// GUID -> live AR Foundation anchor (host-created or client-loaded).
		private readonly Dictionary<Guid, ARAnchor> liveAnchors = new();
		private readonly Dictionary<Guid, SettleState> settleStates = new();
		private readonly Dictionary<Guid, GameObject> canonMarkers = new();

		private readonly HashSet<Guid> pendingLoads = new();
		private readonly List<Guid> staleGroups = new();
		private readonly List<Guid> toUnload = new();

		// Anchors saved to local disk (group -> persistent save GUID) when streaming unloads
		// them, so re-activation loads from disk instead of re-downloading from the cloud.
		private readonly Dictionary<Guid, SerializableGuid> diskSaves = new();

		// Groups whose anchor is mid save-then-unload, to dedupe the unload across frames.
		private readonly HashSet<Guid> pendingUnloads = new();

		// Groups whose last load failed, mapped to when the periodic reconcile may retry
		// them. Without this, a failed load was silent and permanent for the session.
		private readonly Dictionary<Guid, float> loadRetryAt = new();
		private const float loadRetrySeconds = 5f;

		// Reconciliation re-runs on a timer (not just on events) so a load window missed
		// during subsystem warm-up, or a failed load, is retried instead of lost.
		private const float reconcileEverySeconds = 1f;
		private float nextReconcileAt;

		private readonly List<Pose> trackedPoses = new();
		private readonly List<Pose> canonPoses = new();
		private float3[] trackedBuf = Array.Empty<float3>();
		private float3[] canonBuf = Array.Empty<float3>();

		private ARAnchorManager anchorManager;

		private bool isActive;
		private bool isSpawning;
		private bool hasColocated;
		public bool HasColocated => hasColocated;

		// Bumped whenever the live anchor set is invalidated (StopColocation, RealignEveryone).
		// Async ops capture it before their first await and bail if it changed underneath them,
		// so a load/save/reload that finishes after the session ended can't resurrect anchors.
		private int epoch;

		// True from the moment the app backgrounds until the headset has relocalized to an
		// anchor that was live at that point. While suspended we suppress spawning, loading,
		// and unloading: right after resume, tracking is briefly lost and the head can read
		// as far from every anchor, which would otherwise spawn spurious anchors or unload
		// good ones. We lean on the anchors that survived the background instead.
		private bool suspended;

		// Anchors the last RunFit admitted. >0 means at least one anchor is tracked + settled
		// this frame — our signal that the headset has relocalized.
		private int lastFitCount;

		// Set when a non-authority peer realigns: the clear runs once bus authority
		// actually lands on this peer (see OnAuthorityChanged).
		private bool pendingRealign;

		private void Awake()
		{
			Instance = this;
			anchorManager = FindAnyObjectByType<ARAnchorManager>();

			canonPosesSync.Register();
			canonPosesSync.Changed += OnCanonPosesSyncChanged;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		private void OnDestroy()
		{
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			canonPosesSync.Changed -= OnCanonPosesSyncChanged;
			canonPosesSync.Unregister();
		}

		// Headset sleep / app background. Tracking is lost here and stays unreliable for a
		// moment after resume, so flag ourselves suspended; LateUpdate then holds off
		// spawning and (un)loading anchors until the fit relocalizes to one that was live
		// when we paused.
		private void OnApplicationPause(bool paused)
		{
			if (!paused || !isActive) return;

			suspended = true;
			lastFitCount = 0; // don't let a pre-sleep successful fit read as "relocalized"
		}

		// ---- IColocator -------------------------------------------------------

		public void StartColocation()
		{
			if (isActive) return;
			isActive = true;
			hasColocated = false;
			suspended = false;

			// Late joiner: entries may already be populated — load them now.
			ReconcileEntries();
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;
			hasColocated = false;
			pendingRealign = false;
			epoch++; // invalidate any in-flight loads/saves/reloads

			// Authority drops the shared set; everyone removes its local anchors.
			if (SyncBus.Active && SyncBus.IsAuthority)
				canonPosesSync.Clear();

			RemoveAllLiveAnchors();
			pendingLoads.Clear();
			pendingUnloads.Clear();
			loadRetryAt.Clear();
		}

		// Clearing the replicated set drops every client's local anchors (via
		// ReconcileEntries); the realigner then re-seeds a fresh anchor from
		// LateUpdate. AR Foundation anchors can't be re-localized in place, so we
		// start over rather than reuse them. The realigner must be the bus authority
		// first — its frame becomes the new canon frame — so on a non-authority peer
		// the clear waits for the ownership change to land instead of racing it.
		public void RealignEveryone()
		{
			if (!SyncBus.Active) return;

			if (SyncBus.IsAuthority)
			{
				DoRealign();
			}
			else
			{
				pendingRealign = true;
				SyncBus.RequestAuthority();
			}
		}

		private void DoRealign()
		{
			hasColocated = false;
			epoch++; // invalidate any in-flight loads/saves/reloads from the old attempt
			canonPosesSync.Clear();
			RemoveAllLiveAnchors();
			loadRetryAt.Clear();
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			if (!isAuthority)
			{
				pendingRealign = false; // someone else took over mid-request
				return;
			}

			if (pendingRealign)
			{
				pendingRealign = false;
				DoRealign();
			}
		}

		// ---- entry reconciliation --------------------------------------------

		private void OnCanonPosesSyncChanged()
		{
			ReconcileEntries();
		}

		// Bring local anchors in line with the replicated entry set: load anything new,
		// remove anything whose entry is gone.
		private void ReconcileEntries()
		{
			if (!isActive || !SharedAnchorsAvailable()) return;

			// While suspended (post-resume, before we've relocalized) don't pull in new
			// anchors — rely on the ones that were live when we backgrounded.
			if (!suspended)
				foreach ((Guid group, Pose canonPose) in canonPosesSync)
				{
					if (liveAnchors.ContainsKey(group) || pendingLoads.Contains(group))
						continue;
					if (loadRetryAt.TryGetValue(group, out float retryTime) && Time.time < retryTime)
						continue;
					if (!WithinLoadRange(canonPose))
						continue;
					LoadGroup(group);
				}

			staleGroups.Clear();
			foreach (Guid group in liveAnchors.Keys)
				if (!canonPosesSync.ContainsKey(group))
					staleGroups.Add(group);
			foreach (Guid group in staleGroups)
				RemoveLiveAnchor(group);
		}

		private async Task LoadEntry(Guid group)
		{
			int myEpoch = epoch;

			pendingLoads.Add(group);
			ARAnchor anchor = await MetaSharedAnchors.LoadAsync(anchorManager, group);
			pendingLoads.Remove(group);

			if (anchor == null)
			{
				// Error status, empty group, or materialization timeout — all likelier on a
				// cold first run. Register for retry; the periodic reconcile picks it up.
				if (myEpoch == epoch && isActive && canonPosesSync.ContainsKey(group))
				{
					Debug.LogWarning($"[MetaAnchorColocator] Load failed for {group}, retrying in {loadRetrySeconds}s");
					loadRetryAt[group] = Time.time + loadRetrySeconds;
				}

				return;
			}

			// Aborted, the entry was removed, or we raced another load — discard.
			if (myEpoch != epoch || !isActive || !canonPosesSync.ContainsKey(group) || liveAnchors.ContainsKey(group))
			{
				DestroyAnchor(anchor);
				return;
			}

			Debug.Log($"[MetaAnchorColocator] Loaded anchor {group}");
			loadRetryAt.Remove(group);
			liveAnchors[group] = anchor;
		}

		// Bring a group's anchor in locally: from local disk if we saved it on a previous
		// unload, otherwise download it from the cloud (the first time we ever see it).
		private void LoadGroup(Guid group)
		{
			if (diskSaves.ContainsKey(group))
				_ = LoadFromDisk(group);
			else
				_ = LoadEntry(group);
		}

		private async Task LoadFromDisk(Guid group)
		{
			if (!diskSaves.TryGetValue(group, out SerializableGuid savedGuid))
				return;

			int myEpoch = epoch;

			pendingLoads.Add(group);
			try
			{
				Result<ARAnchor> result = await anchorManager.TryLoadAnchorAsync(savedGuid);

				if (!result.status.IsSuccess() || result.value == null)
				{
					// Disk copy unusable — drop it so the next approach re-downloads from cloud.
					Debug.LogWarning(
						$"[MetaAnchorColocator] Disk load failed for {group}: {result.status}; will fall back to cloud");
					diskSaves.Remove(group);
					return;
				}

				if (myEpoch != epoch || !isActive || !canonPosesSync.ContainsKey(group) || liveAnchors.ContainsKey(group))
				{
					DestroyAnchor(result.value);
					return;
				}

				liveAnchors[group] = result.value;
			}
			finally
			{
				pendingLoads.Remove(group);
			}
		}

		// Deactivation: save the anchor to local disk (so it can be re-activated without a
		// cloud download), then untrack and destroy it.
		private async Task UnloadToDisk(Guid group)
		{
			int myEpoch = epoch;

			pendingUnloads.Add(group);
			try
			{
				if (liveAnchors.TryGetValue(group, out ARAnchor anchor) && anchor != null)
				{
					Result<SerializableGuid> save = await anchorManager.TrySaveAnchorAsync(anchor);

					// Session ended mid-save: RemoveAllLiveAnchors already tore the anchor down
					// and erased known disk saves, so erase this late one rather than leak it.
					if (myEpoch != epoch)
					{
						if (save.status.IsSuccess()) EraseDiskSave(save.value);
						return;
					}

					if (save.status.IsSuccess())
						diskSaves[group] = save.value;
					else
						Debug.LogWarning(
							$"[MetaAnchorColocator] Disk save failed for {group}: {save.status} (will cloud-reload)");
				}

				RemoveLiveAnchor(group);
			}
			finally
			{
				pendingUnloads.Remove(group);
			}
		}

		// ---- per-frame --------------------------------------------------------

		private void LateUpdate()
		{
			if (!isActive) return;

			AnchorAvailability availability = GetAvailability();

			if (SyncBus.IsAuthority && !suspended)
			{
				// While WarmingUp do neither: spawning is impossible, and seeding the
				// editor stub on a device that's merely still starting up would poison
				// the session with a fake entry.
				if (availability == AnchorAvailability.Available)
					TrySpawnAnchor();
				else if (availability == AnchorAvailability.Unavailable && canonPosesSync.Count == 0)
					SeedEditorStub();
			}

			// Event-driven reconciliation alone is lossy: a load window missed while the
			// subsystem was warming up, or a failed load, would otherwise never retry.
			if (Time.time >= nextReconcileAt)
			{
				nextReconcileAt = Time.time + reconcileEverySeconds;
				ReconcileEntries();
			}

			RunFit();

			// Lift suspension once we've relocalized to an anchor that survived the
			// background (the fit admitted one). If there are no live anchors, there's
			// nothing to relocalize against, so don't get stuck waiting forever.
			if (suspended && (lastFitCount > 0 || liveAnchors.Count == 0))
			{
				suspended = false;
				ReconcileEntries(); // catch up on anything that arrived while suspended
			}

			if (anchorStreaming && !suspended && SharedAnchorsAvailable())
				UpdateAnchorStreaming();

			SyncCanonMarkers();
		}

		private void TrySpawnAnchor()
		{
			if (isSpawning) return; // one create/share in flight at a time

			Transform head = MainXRRig.Camera.transform;

			// Measure against canon poses, not live anchors: an entry exists from the moment
			// it's shared, so a nearby anchor that's still downloading still counts as
			// coverage. That way we wait for it to finish loading rather than spawn a
			// duplicate in a spot an anchor already claims.
			float nearestSqr = float.MaxValue;
			foreach (Pose canonPose in canonPosesSync.Values)
			{
				Vector3 d = canonPose.position - head.position;
				d.y = 0; // horizontal only — anchors sit ~1.5m below the head
				nearestSqr = Mathf.Min(nearestSqr, d.sqrMagnitude);
			}

			if (anchorBreadcrumb)
				if (nearestSqr > spawnEveryMeters * spawnEveryMeters)
				{
					Vector3 spawnPos = head.position;
					spawnPos.y -= 1.5f;

					Vector3 flatForward = head.forward;
					flatForward.y = 0;
					flatForward.Normalize();

					_ = SpawnAnchor(new Pose(spawnPos, Quaternion.LookRotation(flatForward, Vector3.up)));
				}
		}

		public async Task<Guid> SpawnAnchor(Pose pose)
		{
			int myEpoch = epoch;
			isSpawning = true;
			try
			{
				Pose spawnPose = new(pose.position, pose.rotation);

				(bool ok, ARAnchor anchor, Guid group) =
					await MetaSharedAnchors.CreateAndShareAsync(anchorManager, spawnPose);

				if (!ok || anchor == null)
				{
					DestroyAnchor(anchor);
					return Guid.Empty;
				}

				if (myEpoch != epoch || !isActive || !SyncBus.IsAuthority)
				{
					DestroyAnchor(anchor);
					return Guid.Empty;
				}

				// The spawner's frame is the shared frame, so canon == tracked at creation.
				liveAnchors[group] = anchor;
				canonPosesSync.Set(group, anchor.transform.GetWorldPose());
				return group;
			}
			finally
			{
				isSpawning = false;
			}
		}

		// No AR Foundation (e.g. in-editor play): publish one stub entry so the flow
		// reaches "colocated" without hardware. RunFit treats this as an identity fit.
		private void SeedEditorStub()
		{
			canonPosesSync.Set(Guid.NewGuid(), Pose.identity);
		}

		private void RunFit()
		{
			lastFitCount = 0;

			AnchorAvailability availability = GetAvailability();

			// Only a device that can NEVER fit (editor, no Meta anchors) may fake
			// colocation off the stub entry. During warm-up just wait — latching
			// hasColocated here fired Colocated without the rig ever aligning, and
			// flipped the streaming distance checks into the unaligned frame.
			if (availability != AnchorAvailability.Available)
			{
				if (availability == AnchorAvailability.Unavailable &&
				    canonPosesSync.Count > 0 && !hasColocated)
				{
					hasColocated = true;
					Colocated.Invoke();
				}

				return;
			}

			trackedPoses.Clear();
			canonPoses.Clear();

			foreach ((Guid group, Pose canonPose) in canonPosesSync)
			{
				if (!liveAnchors.TryGetValue(group, out ARAnchor anchor) || anchor == null)
					continue;

				// Only fold an anchor into the fit once the runtime is actively tracking
				// it AND its pose has settled. A freshly loaded anchor sits at a stale
				// (often near-origin) pose and streams corrections in for a while; letting
				// it into the Kabsch fit early yanks everyone's alignment until it lands.
				if (!IsAnchorReliable(group, anchor))
					continue;

				trackedPoses.Add(anchor.transform.GetWorldPose());
				canonPoses.Add(canonPose);
			}

			int n = trackedPoses.Count;
			lastFitCount = n;
			if (n == 0) return;

			float lerp = hasColocated ? alignLerp : 1f;

			if (n == 1)
			{
				// Single anchor: align its full tracked pose onto its canon pose.
				Pose tracked = trackedPoses[0];
				Pose canon = canonPoses[0];

				Matrix4x4 trackedMat = Matrix4x4.TRS(tracked.position, tracked.rotation, Vector3.one);
				Matrix4x4 canonMat = Matrix4x4.TRS(canon.position, canon.rotation, Vector3.one);

				MainXRRig.Instance.AlignSpace(trackedMat, canonMat, lerp);
			}
			else
			{
				// 2+ anchors: best yaw+translation fit of tracked positions onto canon.
				if (trackedBuf.Length < n)
				{
					trackedBuf = new float3[n];
					canonBuf = new float3[n];
				}

				for (int i = 0; i < n; i++)
				{
					trackedBuf[i] = trackedPoses[i].position;
					canonBuf[i] = canonPoses[i].position;
				}

				Matrix4x4 spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;
				Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
					trackedBuf.AsSpan(0, n), canonBuf.AsSpan(0, n));
				Matrix4x4 aligned = delta * spaceMat;

				Vector3 p = aligned.GetPosition();
				if (p.magnitude > 10000f ||
				    float.IsNaN(p.x) || float.IsInfinity(p.x) ||
				    float.IsNaN(p.y) || float.IsInfinity(p.y) ||
				    float.IsNaN(p.z) || float.IsInfinity(p.z))
					return;

				MainXRRig.Instance.AlignSpace(spaceMat, aligned, lerp);
			}

			if (!hasColocated)
			{
				hasColocated = true;
				Colocated.Invoke();
			}
		}

		// Gate for whether an anchor is trustworthy enough to drive the fit this frame.
		//
		// Admission (first trust) requires the anchor to be actively tracked and to hold
		// still for stableFramesRequired frames, which filters out the load-time jump from
		// a stale pose to the real one. Once admitted, the anchor keeps participating while
		// it stays tracked — so we still follow the small ongoing relocalization corrections
		// that actually fight drift (alignLerp smooths them). Losing tracking revokes
		// admission, so a re-localization (which can jump) must re-settle before it counts.
		private bool IsAnchorReliable(Guid group, ARAnchor anchor)
		{
			if (requireTracking && anchor.trackingState != TrackingState.Tracking)
			{
				settleStates.Remove(group);
				return false;
			}

			Pose pose = anchor.transform.GetWorldPose();

			if (!settleStates.TryGetValue(group, out SettleState state))
			{
				settleStates[group] = new SettleState { LastPose = pose };
				return false;
			}

			// Already trusted: follow the live pose, including ongoing drift corrections.
			if (state.Admitted)
			{
				state.LastPose = pose;
				return true;
			}

			float moved = Vector3.Distance(pose.position, state.LastPose.position);
			float rotated = Quaternion.Angle(pose.rotation, state.LastPose.rotation);
			state.LastPose = pose;

			if (moved > stableMoveThreshold || rotated > stableRotateThreshold)
			{
				state.StableFrames = 0; // still settling
				return false;
			}

			if (++state.StableFrames < stableFramesRequired)
				return false;

			state.Admitted = true;
			Debug.Log($"[MetaAnchorColocator] Anchor {group} admitted to fit");
			return true;
		}

		// ---- distance-based streaming ----------------------------------------

		// Once you're beyond unloadDistance from an anchor's canon pose, save it to local
		// disk and untrack it; as you approach again (within reloadDistance) load it back
		// from that disk save, falling back to a cloud download only if it was never saved.
		// Distance is measured to the canon pose — a fixed world target — so it still works
		// once an anchor is unloaded and has no live transform.
		private void UpdateAnchorStreaming()
		{
			// Only stream once colocated: before that, world space isn't aligned to the
			// shared frame, so head-to-canon distance is meaningless and could unload an
			// anchor the fit still needs.
			if (!hasColocated) return;

			Vector3 head = MainXRRig.Camera.transform.position;

			toUnload.Clear();

			foreach ((Guid group, Pose canonPose) in canonPosesSync)
			{
				Vector3 d = canonPose.position - head;
				d.y = 0;
				float dist = d.magnitude;

				if (liveAnchors.ContainsKey(group))
				{
					if (dist > unloadDistance && !pendingUnloads.Contains(group))
						toUnload.Add(group);
				}
				else if (!pendingLoads.Contains(group) && !pendingUnloads.Contains(group) && dist < reloadDistance)
				{
					// Respect the failure cooldown — this runs every frame.
					if (loadRetryAt.TryGetValue(group, out float retryTime) && Time.time < retryTime)
						continue;

					// Loaders add to pendingLoads synchronously, then await; safe to kick
					// off mid-iteration since they don't touch entries/liveAnchors yet.
					LoadGroup(group);
				}
			}

			// Launch the (async) save-then-unload only after the iteration above.
			foreach (Guid group in toUnload)
				_ = UnloadToDisk(group);
		}

		// Whether ReconcileEntries should load this entry right now. With streaming on,
		// hold off on distant entries (UpdateAnchorStreaming pulls them in as you approach);
		// until colocated, load everything so the fit has anchors to work with.
		private bool WithinLoadRange(Pose canonPose)
		{
			if (!anchorStreaming || !hasColocated)
				return true;

			Vector3 head = MainXRRig.Camera.transform.position;
			Vector3 d = canonPose.position - head;
			d.y = 0;
			return d.magnitude < unloadDistance;
		}

		// ---- debug markers ----------------------------------------------------

		// One marker per entry, pinned to that entry's canon pose. Canon poses are fixed
		// world-space targets in the shared frame, so a marker never moves — the live
		// anchor visual drifting away from its marker shows that anchor's displacement.
		// (With a single anchor + alignLerp 1 the anchor is snapped onto its marker every
		// frame; the gap is most visible with multiple anchors or a lower alignLerp.)
		// Markers are always spawned; put a RenderIfDebug on the prefab to gate visibility.
		private void SyncCanonMarkers()
		{
			if (canonMarkerPrefab == null)
			{
				if (canonMarkers.Count > 0)
					ClearCanonMarkers();
				return;
			}

			// Spawn a marker for any entry that lacks one. Canon pose is immutable per
			// group, so we set the transform once at instantiation.
			foreach ((Guid group, Pose canonPose) in canonPosesSync)
			{
				if (canonMarkers.ContainsKey(group))
					continue;

				canonMarkers[group] = Instantiate(
					canonMarkerPrefab, canonPose.position, canonPose.rotation);
			}

			// Drop markers whose entry is gone.
			staleGroups.Clear();
			foreach (Guid group in canonMarkers.Keys)
				if (!canonPosesSync.ContainsKey(group))
					staleGroups.Add(group);
			foreach (Guid group in staleGroups)
				DestroyCanonMarker(group);
		}

		private void DestroyCanonMarker(Guid group)
		{
			if (canonMarkers.TryGetValue(group, out GameObject marker) && marker != null)
				Destroy(marker);
			canonMarkers.Remove(group);
		}

		private void ClearCanonMarkers()
		{
			foreach (GameObject marker in canonMarkers.Values)
				if (marker != null)
					Destroy(marker);
			canonMarkers.Clear();
		}

		// ---- cleanup ----------------------------------------------------------

		public void RemoveLiveAnchor(Guid group)
		{
			if (liveAnchors.TryGetValue(group, out ARAnchor anchor))
				DestroyAnchor(anchor);
			liveAnchors.Remove(group);
			settleStates.Remove(group);
		}

		private void RemoveAllLiveAnchors()
		{
			foreach (ARAnchor anchor in liveAnchors.Values)
				DestroyAnchor(anchor);

			liveAnchors.Clear();
			settleStates.Clear();

			// Erase everything we saved to disk this session so saves don't pile up locally.
			EraseAllDiskSaves();

			ClearCanonMarkers();
		}

		private void EraseAllDiskSaves()
		{
			foreach (SerializableGuid saved in diskSaves.Values)
				EraseDiskSave(saved);
			diskSaves.Clear();
		}

		// Drop a persistent disk save from local storage. Deliberately NOT epoch-gated — it
		// runs because the session is ending and must complete regardless. async void
		// because it's fire-and-forget cleanup.
		private async void EraseDiskSave(SerializableGuid savedGuid)
		{
			if (anchorManager == null) return;

			try
			{
				XRResultStatus status = await anchorManager.TryEraseAnchorAsync(savedGuid);
				if (status.IsError())
					Debug.LogWarning($"[MetaAnchorColocator] Failed to erase disk save {savedGuid}: {status}");
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[MetaAnchorColocator] Erase of disk save threw: {e.Message}");
			}
		}

		// Get rid of an anchor we no longer want, making sure its GameObject is destroyed.
		//
		// AR Foundation only destroys an anchor's GameObject when the *subsystem* reports
		// the anchor in its `removed` changes — but Meta does NOT report shared/loaded
		// anchors as removed, so `TryRemoveAnchor` returns true yet the prefab instance is
		// orphaned and litters the scene. So we ask the subsystem to drop it (guarded:
		// TryRemoveAnchor throws when the manager is disabled during teardown) and then
		// destroy the GameObject ourselves. This sticks: the manager only re-spawns a
		// trackable whose id is missing from its table, which an orphaned anchor never is,
		// and its `removed` handling null-checks the trackable.
		private void DestroyAnchor(ARAnchor anchor)
		{
			if (anchor == null) return;

			GameObject go = anchor.gameObject;

			if (anchorManager != null && anchorManager.isActiveAndEnabled)
				anchorManager.TryRemoveAnchor(anchor);

			if (go != null)
				Destroy(go);
		}

		// Distinguishes "the subsystem is still starting up" from "this device will never
		// have shared anchors". isSharedAnchorsSupported reports Unknown until the OpenXR
		// instance is created, which takes a moment after app launch — the FIRST colocation
		// run of a process can land inside that window. Treating Unknown as Unavailable
		// there made the fit fake "colocated" (rig never moves), seeded the editor stub on
		// device, and consumed one-shot load triggers. WarmingUp instead means: wait.
		private enum AnchorAvailability
		{
			WarmingUp,
			Available,
			Unavailable
		}

		private AnchorAvailability GetAvailability()
		{
			if (!XRSettings.enabled || anchorManager == null)
				return AnchorAvailability.Unavailable;

			if (anchorManager.subsystem == null)
				return AnchorAvailability.WarmingUp; // manager not started yet

			if (anchorManager.subsystem is not MetaOpenXRAnchorSubsystem subsystem)
				return AnchorAvailability.Unavailable; // e.g. XR Simulation

			return subsystem.isSharedAnchorsSupported switch
			{
				Supported.Supported => AnchorAvailability.Available,
				Supported.Unknown => AnchorAvailability.WarmingUp, // OpenXR instance not created yet
				_ => AnchorAvailability.Unavailable,
			};
		}

		private bool SharedAnchorsAvailable() => GetAvailability() == AnchorAvailability.Available;

		// Per-anchor reliability bookkeeping for IsAnchorReliable. Reference type so the
		// dictionary entry is mutated in place.
		private sealed class SettleState
		{
			public Pose LastPose;
			public int StableFrames;
			public bool Admitted;
		}
	}

	// Wraps AR Foundation's shared-anchor create/share/load. Share and load both read
	// the single global MetaOpenXRAnchorSubsystem.sharedAnchorsGroupId, so a static gate
	// serializes "set group, then operate" across all callers on this device.
	internal static class MetaSharedAnchors
	{
		private static readonly SemaphoreSlim gate = new(1, 1);

		public static async Task<(bool ok, ARAnchor anchor, Guid group)> CreateAndShareAsync(
			ARAnchorManager manager, Pose pose)
		{
			Result<ARAnchor> add = await manager.TryAddAnchorAsync(pose);
			if (!add.status.IsSuccess() || add.value == null)
				return (false, null, Guid.Empty);

			ARAnchor anchor = add.value;
			Guid group = Guid.NewGuid();

			await gate.WaitAsync();
			try
			{
				SetGroup(manager, group);

				List<XRShareAnchorResult> results = new();
				await manager.TryShareAnchorsAsync(new[] { anchor }, results);

				bool ok = results.Count > 0 && results[0].resultStatus.IsSuccess();
				return (ok, anchor, group);
			}
			finally
			{
				gate.Release();
			}
		}

		public static async Task<ARAnchor> LoadAsync(ARAnchorManager manager, Guid group)
		{
			List<XRAnchor> loaded = new();
			TrackableId id;

			await gate.WaitAsync();
			try
			{
				SetGroup(manager, group);

				XRResultStatus status = await manager.TryLoadAllSharedAnchorsAsync(loaded, null);
				if (status.IsError() || loaded.Count == 0)
					return null;

				id = loaded[0].trackableId;
			}
			finally
			{
				gate.Release();
			}

			// The ARAnchor MonoBehaviour materializes a frame or more after the load
			// completes; poll the trackable collection for it (outside the gate).
			for (int i = 0; i < 180; i++)
			{
				if (manager.trackables.TryGetTrackable(id, out ARAnchor anchor) && anchor != null)
					return anchor;

				await Awaitable.NextFrameAsync();
			}

			return null;
		}

		private static void SetGroup(ARAnchorManager manager, Guid group)
		{
			MetaOpenXRAnchorSubsystem subsystem = (MetaOpenXRAnchorSubsystem)manager.subsystem;
			subsystem.sharedAnchorsGroupId = new SerializableGuid(group);
		}
	}
}