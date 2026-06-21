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

		[Tooltip("The host drops a new anchor whenever it gets this far (m) from every existing anchor.")]
		[SerializeField]
		private float spawnEveryMeters = 2f;

		[Tooltip("Per-frame lerp toward the fitted pose once colocated. 1 = snap. Lower smooths drift correction.")]
		[Range(0f, 1f)]
		[SerializeField]
		private float alignLerp = 1f;

		[Tooltip("Require the runtime to be actively tracking an anchor (TrackingState.Tracking) before it can drive the fit. Disable only if your provider never reports anything but Tracking.")]
		[SerializeField]
		private bool requireTracking = true;

		[Tooltip("An anchor must hold still for this many frames before it's first trusted in the fit (filters the load-time pose jump).")]
		[SerializeField]
		private int stableFramesRequired = 8;

		[Tooltip("Max per-frame world movement (m) for an anchor to count as 'settled' for admission.")]
		[SerializeField]
		private float stableMoveThreshold = 0.02f;

		[Tooltip("Max per-frame world rotation (deg) for an anchor to count as 'settled' for admission.")]
		[SerializeField]
		private float stableRotateThreshold = 1.5f;

		[Header("Relocalization experiment")]
		[Tooltip("Experimental: save each anchor locally once it localizes, then after you leave it (departMeters) and return (within spawnEveryMeters), destroy and reload it to force a fresh load-time localization. Tests whether initial localization is more robust than continuous tracking.")]
		[SerializeField]
		private bool reloadOnRevisit = false;

		[Tooltip("How far (m, horizontal) you must get from an anchor before a revisit will trigger a reload.")]
		[SerializeField]
		private float departMeters = 5f;

		public event Action Colocated = delegate { };

		// Replicated: one entry per shared anchor (group GUID + canon pose).
		// Host-authoritative writes; clients read.
		private readonly NetworkList<AnchorEntry> entries = new();

		// Local: group GUID -> live AR Foundation anchor (host-created or client-loaded).
		private readonly Dictionary<Guid, ARAnchor> liveAnchors = new();

		// Local: per-anchor settle/admission state for the fit's reliability gate.
		private readonly Dictionary<Guid, SettleState> settleStates = new();

		// Local: groups a client is currently loading, to dedupe concurrent reconciles.
		private readonly HashSet<Guid> pendingLoads = new();
		private readonly List<Guid> staleGroups = new();

		// Local: relocalization-experiment bookkeeping (local save GUID + depart state).
		private readonly Dictionary<Guid, RevisitState> revisitStates = new();
		private readonly List<Guid> toSave = new();
		private readonly List<Guid> toReload = new();

		// Reused per-frame scratch for the fit.
		private readonly List<Pose> trackedPoses = new();
		private readonly List<Pose> canonPoses = new();
		private float3[] trackedBuf = Array.Empty<float3>();
		private float3[] canonBuf = Array.Empty<float3>();

		private ARAnchorManager anchorManager;

		private bool isActive;
		private bool isSpawning;
		private bool hasColocated;
		public bool HasColocated => hasColocated;

		private void Awake()
		{
			Instance = this;
			anchorManager = FindAnyObjectByType<ARAnchorManager>();
		}

		public override void OnNetworkSpawn()
		{
			entries.OnListChanged += OnEntriesChanged;
		}

		public override void OnNetworkDespawn()
		{
			entries.OnListChanged -= OnEntriesChanged;
		}

		// ---- IColocator -------------------------------------------------------

		public void StartColocation()
		{
			if (isActive) return;
			isActive = true;
			hasColocated = false;

			// Late joiner: entries may already be populated — load them now.
			ReconcileEntries();
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;
			hasColocated = false;

			// Owner drops the shared set; everyone removes its local anchors.
			if (IsSpawned && IsOwner)
				entries.Clear();

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
			entries.Clear();
			RemoveAllLiveAnchors();
		}

		// ---- entry reconciliation --------------------------------------------

		private void OnEntriesChanged(NetworkListEvent<AnchorEntry> _)
		{
			ReconcileEntries();
		}

		// Bring local anchors in line with the replicated entry list: load anything new,
		// remove anything whose entry is gone.
		private void ReconcileEntries()
		{
			if (!isActive || !SharedAnchorsAvailable()) return;

			foreach (AnchorEntry entry in entries)
			{
				Guid group = entry.Group;
				if (liveAnchors.ContainsKey(group) || pendingLoads.Contains(group))
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
			foreach (AnchorEntry e in entries)
				if (e.Group == group)
					return true;
			return false;
		}

		private async Task LoadEntry(Guid group)
		{
			pendingLoads.Add(group);
			ARAnchor anchor = await MetaSharedAnchors.LoadAsync(anchorManager, group);
			pendingLoads.Remove(group);

			// Aborted, the entry was removed, or we raced another load — discard.
			if (anchor == null) return;
			if (!isActive || !EntriesContain(group) || liveAnchors.ContainsKey(group))
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

			if (IsOwner)
			{
				if (SharedAnchorsAvailable()) TrySpawnAnchor();
				else if (entries.Count == 0) SeedEditorStub();
			}

			RunFit();

			if (reloadOnRevisit && SharedAnchorsAvailable())
				UpdateRevisitReloads();
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

				if (!isActive || !IsOwner)
				{
					DestroyAnchor(anchor);
					return;
				}

				// Host frame is the shared frame, so canon == tracked at creation.
				liveAnchors[group] = anchor;
				entries.Add(new AnchorEntry(group, anchor.transform.GetWorldPose()));
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
			entries.Add(new AnchorEntry(Guid.NewGuid(), Pose.identity));
		}

		private void RunFit()
		{
			if (!SharedAnchorsAvailable())
			{
				if (entries.Count > 0 && !hasColocated)
				{
					hasColocated = true;
					Colocated.Invoke();
				}

				return;
			}

			trackedPoses.Clear();
			canonPoses.Clear();

			foreach (AnchorEntry entry in entries)
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

		// ---- relocalization experiment ---------------------------------------

		// Hypothesis: an anchor's load-time localization is more accurate than the
		// corrections it streams while continuously tracked, so a drifted anchor that
		// fails to snap back will land correctly if we destroy it and load it fresh.
		//
		// We save each anchor locally once it first localizes (admitted to the fit). When
		// the head leaves it by departMeters and then returns within spawnEveryMeters, we
		// drop the live anchor and reload it from the local save, re-running the load-time
		// localization path. Compare the post-reload alignment against the pre-reload drift.
		private void UpdateRevisitReloads()
		{
			Vector3 head = MainXRRig.Camera.transform.position;

			toSave.Clear();
			toReload.Clear();

			foreach (KeyValuePair<Guid, ARAnchor> kv in liveAnchors)
			{
				Guid group = kv.Key;
				ARAnchor anchor = kv.Value;
				if (anchor == null) continue;

				if (!revisitStates.TryGetValue(group, out RevisitState rs))
					revisitStates[group] = rs = new RevisitState();

				if (rs.Busy) continue;

				// Save once the anchor has been admitted to the fit (i.e. localized + settled).
				if (!rs.Saved)
				{
					bool admitted = settleStates.TryGetValue(group, out SettleState ss) && ss.Admitted;
					if (admitted)
						toSave.Add(group);
					continue;
				}

				// Horizontal distance only, matching the spawn spacing metric.
				Vector3 d = anchor.transform.position - head;
				d.y = 0;
				float dist = d.magnitude;

				if (dist > departMeters)
					rs.Departed = true;
				else if (rs.Departed && dist < spawnEveryMeters)
					toReload.Add(group);
			}

			// Mutate liveAnchors only after the iteration above. Mark Busy up front so the
			// next frame doesn't re-queue the same group while the op is in flight.
			foreach (Guid group in toSave)
			{
				revisitStates[group].Busy = true;
				_ = SaveAnchorLocally(group);
			}

			foreach (Guid group in toReload)
			{
				revisitStates[group].Busy = true;
				_ = ReloadAnchorLocally(group);
			}
		}

		private async Task SaveAnchorLocally(Guid group)
		{
			if (!revisitStates.TryGetValue(group, out RevisitState rs))
				return;

			try
			{
				if (!liveAnchors.TryGetValue(group, out ARAnchor anchor) || anchor == null)
					return;

				Result<SerializableGuid> result = await anchorManager.TrySaveAnchorAsync(anchor);

				if (result.status.IsSuccess())
				{
					rs.SavedGuid = result.value;
					rs.Saved = true;
				}
				else
				{
					Debug.LogWarning($"[MetaAnchorColocator] Local save failed for {group}: {result.status}");
				}
			}
			finally
			{
				rs.Busy = false;
			}
		}

		private async Task ReloadAnchorLocally(Guid group)
		{
			if (!revisitStates.TryGetValue(group, out RevisitState rs))
				return;

			// Block ReconcileEntries from cloud-loading this group while we swap it.
			pendingLoads.Add(group);
			try
			{
				// Drop the drifted anchor and its admission so the reloaded one must
				// re-settle from its fresh load-time pose.
				RemoveLiveAnchor(group);

				Result<ARAnchor> result = await anchorManager.TryLoadAnchorAsync(rs.SavedGuid);

				if (!isActive || !EntriesContain(group))
				{
					if (result.status.IsSuccess()) DestroyAnchor(result.value);
					return;
				}

				if (result.status.IsSuccess() && result.value != null)
					liveAnchors[group] = result.value;
				else
					Debug.LogWarning($"[MetaAnchorColocator] Reload failed for {group}: {result.status}");

				// Must leave and return again before the next reload.
				rs.Departed = false;
			}
			finally
			{
				pendingLoads.Remove(group);
				rs.Busy = false;
			}
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

			// Locally-saved anchors persist in Meta's store across this; the bookkeeping is
			// per-session, so a re-seed starts the save/depart cycle over.
			revisitStates.Clear();
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

		// Replicated anchor entry: a Meta share-group GUID and its canonical pose.
		// Blittable (unmanaged) so it rides NetworkList's memcpy path; identity is the
		// group GUID.
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

		// Per-anchor reliability bookkeeping for IsAnchorReliable. Reference type so the
		// dictionary entry is mutated in place.
		private sealed class SettleState
		{
			public Pose LastPose;
			public int StableFrames;
			public bool Admitted;
		}

		// Per-anchor state for the reloadOnRevisit experiment. Saved holds whether we've
		// captured a local persistent GUID; Departed latches once the head has left by
		// departMeters; Busy guards an in-flight save/reload for this group.
		private sealed class RevisitState
		{
			public bool Saved;
			public SerializableGuid SavedGuid;
			public bool Departed;
			public bool Busy;
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