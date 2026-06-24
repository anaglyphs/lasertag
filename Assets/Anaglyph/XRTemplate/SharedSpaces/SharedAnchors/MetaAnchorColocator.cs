using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.Netcode;
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
	// OVRSpatialAnchor.UpdateTransform). We replicate only DATA — a per-anchor Meta
	// share-group GUID and its canonical (host-frame) pose — as a NetworkList here.
	//
	// The host scatters anchors as it explores (one whenever it strays more than
	// spawnEveryMeters from every existing anchor): it creates an ARAnchor, shares it to
	// a fresh group, and appends an entry. Every client loads each entry's group, takes
	// the single anchor inside, and registers it locally. Each frame we pair every
	// entry's canon pose with its live tracked pose and fit: 1 anchor aligns directly
	// (full pose), 2+ run through IterativeClosestPoint for a yaw+translation best fit.
	[DefaultExecutionOrder(999)]
	public class MetaAnchorColocator : NetworkBehaviour, IColocator
	{
		public static MetaAnchorColocator Instance { get; private set; }

		[SerializeField] private float spawnEveryMeters = 2f;

		[Range(0f, 1f)] [SerializeField] private float alignLerp = 1f;

		[SerializeField] private bool requireTracking = true;

		[SerializeField] private int stableFramesRequired = 8;
		[SerializeField] private float stableMoveThreshold = 0.02f;
		[SerializeField] private float stableRotateThreshold = 1.5f;

		[Header("Relocalization experiment")] [SerializeField]
		private float unloadDistance = 0f;

		[SerializeField] private float reloadDistance;

		[Header("Debug")] [SerializeField] private GameObject canonMarkerPrefab;

		public event Action Colocated = delegate { };

		public struct AnchorEntry : INetworkSerializeByMemcpy, IEquatable<AnchorEntry>
		{
			public Guid Group;
			public Pose CanonPose;

			public AnchorEntry(Guid group, Pose canonPose)
			{
				Group = group;
				CanonPose = canonPose;
			}

			public bool Equals(AnchorEntry other)
			{
				return Group.Equals(other.Group);
			}
		}

		private readonly NetworkList<AnchorEntry> anchorEntriesSync = new();

		// GUID -> live AR Foundation anchor (host-created or client-loaded).
		private readonly Dictionary<Guid, ARAnchor> liveAnchors = new();
		private readonly Dictionary<Guid, SettleState> settleStates = new();
		private readonly Dictionary<Guid, GameObject> canonMarkers = new();

		private readonly HashSet<Guid> pendingLoads = new();
		private readonly List<Guid> staleGroups = new();
		private readonly List<Guid> toUnload = new();

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

		private void Awake()
		{
			Instance = this;
			anchorManager = FindAnyObjectByType<ARAnchorManager>();
		}

		public override void OnNetworkSpawn()
		{
			anchorEntriesSync.OnListChanged += OnAnchorEntriesSyncChanged;
		}

		public override void OnNetworkDespawn()
		{
			anchorEntriesSync.OnListChanged -= OnAnchorEntriesSyncChanged;
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
			epoch++; // invalidate any in-flight loads/saves/reloads

			// Owner drops the shared set; everyone removes its local anchors.
			if (IsSpawned && IsOwner)
				anchorEntriesSync.Clear();

			RemoveAllLiveAnchors();
			pendingLoads.Clear();
		}

		// Sent to every client by ColocationManager. Clearing the replicated set drops
		// every client's local anchors (via OnEntriesChanged); the host then re-seeds a
		// fresh anchor from LateUpdate. AR Foundation anchors can't be re-localized in
		// place, so we start over rather than reuse them.
		public void RealignEveryone()
		{
			if (!IsSpawned) return;

			if (!IsOwner)
				NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);

			hasColocated = false;
			epoch++; // invalidate any in-flight loads/saves/reloads from the old attempt
			anchorEntriesSync.Clear();
			RemoveAllLiveAnchors();
		}

		// ---- entry reconciliation --------------------------------------------

		private void OnAnchorEntriesSyncChanged(NetworkListEvent<AnchorEntry> _)
		{
			ReconcileEntries();
		}

		// Bring local anchors in line with the replicated entry list: load anything new,
		// remove anything whose entry is gone.
		private void ReconcileEntries()
		{
			if (!isActive || !SharedAnchorsAvailable()) return;

			// While suspended (post-resume, before we've relocalized) don't pull in new
			// anchors — rely on the ones that were live when we backgrounded.
			if (!suspended)
				foreach (AnchorEntry entry in anchorEntriesSync)
				{
					Guid group = entry.Group;
					if (liveAnchors.ContainsKey(group) || pendingLoads.Contains(group))
						continue;
					if (!WithinLoadRange(entry))
						continue;
					_ = LoadEntry(group);
				}

			staleGroups.Clear();
			foreach (Guid group in liveAnchors.Keys)
				if (!EntriesContain(group))
					staleGroups.Add(group);
			foreach (Guid group in staleGroups)
				RemoveLiveAnchor(group);
		}

		private bool EntriesContain(Guid group)
		{
			foreach (AnchorEntry e in anchorEntriesSync)
				if (e.Group == group)
					return true;
			return false;
		}

		private async Task LoadEntry(Guid group)
		{
			int myEpoch = epoch;

			pendingLoads.Add(group);
			ARAnchor anchor = await MetaSharedAnchors.LoadAsync(anchorManager, group);
			pendingLoads.Remove(group);

			// Aborted, the entry was removed, or we raced another load — discard.
			if (anchor == null) return;
			if (myEpoch != epoch || !isActive || !EntriesContain(group) || liveAnchors.ContainsKey(group))
			{
				DestroyAnchor(anchor);
				return;
			}

			liveAnchors[group] = anchor;
		}

		// ---- per-frame --------------------------------------------------------

		private void LateUpdate()
		{
			if (!isActive) return;

			if (IsOwner && !suspended)
			{
				if (SharedAnchorsAvailable()) TrySpawnAnchor();
				else if (anchorEntriesSync.Count == 0) SeedEditorStub();
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

			if (unloadDistance > 0f && !suspended && SharedAnchorsAvailable())
				UpdateAnchorStreaming();

			SyncCanonMarkers();
		}

		private void TrySpawnAnchor()
		{
			if (isSpawning) return; // one create/share in flight at a time

			Vector3 head = MainXRRig.Camera.transform.position;

			float nearestSqr = float.MaxValue;
			foreach (ARAnchor anchor in liveAnchors.Values)
			{
				if (anchor == null) continue;

				// Horizontal distance only — anchors sit ~1.5m below the head.
				Vector3 d = anchor.transform.position - head;
				d.y = 0;
				nearestSqr = Mathf.Min(nearestSqr, d.sqrMagnitude);
			}

			if (nearestSqr > spawnEveryMeters * spawnEveryMeters)
				_ = SpawnAnchor();
		}

		private async Task SpawnAnchor()
		{
			int myEpoch = epoch;
			isSpawning = true;
			try
			{
				Transform head = MainXRRig.Camera.transform;

				Vector3 spawnPos = head.position;
				spawnPos.y -= 1.5f;

				Vector3 flatForward = head.forward;
				flatForward.y = 0;
				flatForward.Normalize();
				Pose spawnPose = new(spawnPos, Quaternion.LookRotation(flatForward, Vector3.up));

				(bool ok, ARAnchor anchor, Guid group) =
					await MetaSharedAnchors.CreateAndShareAsync(anchorManager, spawnPose);

				if (!ok || anchor == null)
				{
					DestroyAnchor(anchor);
					return;
				}

				if (myEpoch != epoch || !isActive || !IsOwner)
				{
					DestroyAnchor(anchor);
					return;
				}

				// Host frame is the shared frame, so canon == tracked at creation.
				liveAnchors[group] = anchor;
				anchorEntriesSync.Add(new AnchorEntry(group, anchor.transform.GetWorldPose()));
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
			anchorEntriesSync.Add(new AnchorEntry(Guid.NewGuid(), Pose.identity));
		}

		private void RunFit()
		{
			lastFitCount = 0;

			if (!SharedAnchorsAvailable())
			{
				if (anchorEntriesSync.Count > 0 && !hasColocated)
				{
					hasColocated = true;
					Colocated.Invoke();
				}

				return;
			}

			trackedPoses.Clear();
			canonPoses.Clear();

			foreach (AnchorEntry entry in anchorEntriesSync)
			{
				if (!liveAnchors.TryGetValue(entry.Group, out ARAnchor anchor) || anchor == null)
					continue;

				// Only fold an anchor into the fit once the runtime is actively tracking
				// it AND its pose has settled. A freshly loaded anchor sits at a stale
				// (often near-origin) pose and streams corrections in for a while; letting
				// it into the Kabsch fit early yanks everyone's alignment until it lands.
				if (!IsAnchorReliable(entry.Group, anchor))
					continue;

				trackedPoses.Add(anchor.transform.GetWorldPose());
				canonPoses.Add(entry.CanonPose);
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
			return true;
		}

		// ---- distance-based streaming ----------------------------------------

		// Untrack + destroy anchors once you're beyond unloadDistance from their canon
		// pose, and reload them (from the cloud, via ReconcileEntries' LoadEntry path) as
		// you approach again. Distance is measured to the canon pose — a fixed world
		// target — so it still works once an anchor is unloaded and has no live transform.
		private void UpdateAnchorStreaming()
		{
			// Only stream once colocated: before that, world space isn't aligned to the
			// shared frame, so head-to-canon distance is meaningless and could unload an
			// anchor the fit still needs.
			if (!hasColocated) return;

			Vector3 head = MainXRRig.Camera.transform.position;

			toUnload.Clear();

			foreach (AnchorEntry entry in anchorEntriesSync)
			{
				Guid group = entry.Group;

				Vector3 d = entry.CanonPose.position - head;
				d.y = 0;
				float dist = d.magnitude;

				if (liveAnchors.ContainsKey(group))
				{
					if (dist > unloadDistance)
						toUnload.Add(group);
				}
				else if (!pendingLoads.Contains(group) && dist < reloadDistance)
				{
					// LoadEntry adds to pendingLoads synchronously, then awaits; safe to
					// kick off mid-iteration since it doesn't touch entries/liveAnchors yet.
					_ = LoadEntry(group);
				}
			}

			// Mutate liveAnchors only after the iteration above.
			foreach (Guid group in toUnload)
				RemoveLiveAnchor(group);
		}

		// Whether ReconcileEntries should load this entry right now. With streaming on,
		// hold off on distant entries (UpdateAnchorStreaming pulls them in as you approach);
		// until colocated, load everything so the fit has anchors to work with.
		private bool WithinLoadRange(AnchorEntry entry)
		{
			if (unloadDistance <= 0f || !hasColocated)
				return true;

			Vector3 head = MainXRRig.Camera.transform.position;
			Vector3 d = entry.CanonPose.position - head;
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
			foreach (AnchorEntry entry in anchorEntriesSync)
			{
				if (canonMarkers.ContainsKey(entry.Group))
					continue;

				canonMarkers[entry.Group] = Instantiate(
					canonMarkerPrefab, entry.CanonPose.position, entry.CanonPose.rotation);
			}

			// Drop markers whose entry is gone.
			staleGroups.Clear();
			foreach (Guid group in canonMarkers.Keys)
				if (!EntriesContain(group))
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

		private void RemoveLiveAnchor(Guid group)
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

			ClearCanonMarkers();
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

		private bool SharedAnchorsAvailable()
		{
			if (!XRSettings.enabled || anchorManager == null)
				return false;

			if (anchorManager.subsystem is not MetaOpenXRAnchorSubsystem subsystem)
				return false;

			return subsystem.isSharedAnchorsSupported == Supported.Supported;
		}

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