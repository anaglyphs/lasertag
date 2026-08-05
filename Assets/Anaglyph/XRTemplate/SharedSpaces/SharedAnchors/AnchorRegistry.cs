using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using Object = UnityEngine.Object;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Where an <see cref="AnchorHandle"/> is allowed to get its anchor from when it has to load one.
	/// Flags, so a single lease can permit both. Local storage is always tried first: it needs no
	/// network and materializes the anchor immediately.
	/// </summary>
	[Flags]
	public enum AnchorSource
	{
		None = 0,

		/// <summary>
		/// This device's persistent anchor storage. Only anchors previously handed to
		/// <see cref="AnchorRegistry.TrySaveAsync(AnchorLease, CancellationToken)"/> — in this session
		/// or an earlier one — can be loaded this way.
		/// </summary>
		Local = 1 << 0,

		/// <summary>
		/// The Meta shared anchor group whose id is the anchor guid. Requires whoever created the
		/// anchor to have shared it, and a round trip to Meta's servers.
		/// </summary>
		Shared = 1 << 1,

		Any = Local | Shared
	}

	/// <summary>
	/// Annoyingly, AR Foundation anchor operations are not cancellable.
	/// E.g. I can't stop an anchor download from completing and instantiating an ARAnchor
	/// if I don't need the anchor anymore.
	/// This system is here to make these limitations manageable. 
	/// 
	/// Owns the local AR Foundation anchor handles for a process and routes trackable
	/// materialization/removal events to them. Also fronts AR Foundation's persistent anchor
	/// storage, so an anchor can be saved to this device and loaded back in a later session
	/// without a network round trip.
	///
	/// Everything here is keyed by trackable id. Meta's runtime guarantees that the guid returned
	/// when saving or sharing an anchor *is* that anchor's trackable id, so one guid addresses an
	/// anchor locally, in the shared group, and in this registry.
	///
	/// Nothing here assumes a particular runtime. Whichever anchor subsystem is running is used
	/// for as much as it implements, and every capability past creating and tracking anchors is
	/// asked about at runtime: AR Foundation's XR Simulation tracks anchors but persists none of
	/// them, and only Meta's runtime — a headset, or the Meta XR Simulator in-editor — shares
	/// anchors or enumerates what this device has saved.
	/// </summary>
	[DefaultExecutionOrder(-300)]
	public sealed class AnchorRegistry : MonoBehaviour
	{
		public static AnchorRegistry Instance { get; private set; }

		private ARAnchorManager anchorManager;

		/// <summary>
		/// One handle per guid, kept for the registry's lifetime. Bounded by the anchor guids a
		/// session touches, and holding an idle handle costs nothing but the entry — cheap next to
		/// the identity guarantee it buys <see cref="Acquire(SerializableGuid, AnchorSource)"/>.
		/// </summary>
		private readonly Dictionary<SerializableGuid, AnchorHandle> handles = new();
		private readonly HashSet<SerializableGuid> savedGuidSet = new();

		/// <summary>
		/// Anchors this process saw in local storage first-hand, by saving or loading one. Device
		/// enumeration replaces the rest of <see cref="savedGuidSet"/> but cannot outvote these:
		/// discovery is filtered, and an anchor we just wrote is not gone because a query omitted it.
		/// </summary>
		private readonly HashSet<SerializableGuid> provenSavedGuids = new();

		private readonly List<AnchorHandle> reconciliationSnapshot = new();

		private bool disposed;
		private bool loggedNoSharedAnchors;
		private bool loggedNoSavedAnchors;

		/// <summary>
		/// Whichever anchor subsystem is running, resolved live rather than cached: the manager
		/// creates it when it is enabled, which can be after this component wakes.
		/// </summary>
		private XRAnchorSubsystem anchorSubsystem =>
			anchorManager != null ? anchorManager.subsystem : null;

		/// <summary>
		/// Meta's anchor subsystem, when Meta's runtime is the one providing anchors — on a
		/// headset, and in-editor under the Meta XR Simulator. Null under AR Foundation's XR
		/// Simulation, which tracks anchors but implements none of Meta's extensions
		/// (sharing, local persistence, discovery).
		/// </summary>
		private MetaOpenXRAnchorSubsystem metaAnchorSubsystem =>
			anchorSubsystem as MetaOpenXRAnchorSubsystem;

		/// <summary>
		/// False where this process has no anchor runtime at all, which is a normal state
		/// rather than a failure — a rig with no AR managers, or an editor session with no
		/// simulation running. Individual capabilities (saving, sharing, enumerating) vary
		/// between runtimes and are reported separately.
		/// </summary>
		public bool IsAvailable => anchorSubsystem != null && !disposed;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogError("Only one AnchorRegistry may own AR anchor handles.", this);
				enabled = false;
				return;
			}

			Instance = this;

			anchorManager = FindFirstObjectByType<ARAnchorManager>();
			if (anchorManager == null)
			{
				// Not an error: a rig without AR managers simply has no anchors to hand out,
				// and everything downstream is written to run without them.
				Debug.LogWarning("AnchorRegistry found no ARAnchorManager. " +
					"Anchors are unavailable for this session.", this);
				return;
			}

			// Unity cancels this token as the component is destroyed, so the loops started here end
			// with the registry and there is no source of our own to dispose of underneath them.
			RefreshSavedGuidsOnStartup(destroyCancellationToken);

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);
			ReconciliationLoop(destroyCancellationToken);
		}

		/// <summary>
		/// Seeds <see cref="savedGuids"/> as the registry comes up. Nothing waits on the result, so
		/// this owns the failure: an unobserved fault here would otherwise surface as a bare
		/// exception with no indication of what asked for it.
		/// </summary>
		private async void RefreshSavedGuidsOnStartup(CancellationToken ctkn)
		{
			try
			{
				await TryRefreshSavedGuidsAsync(ctkn);
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
		/// Tears down before clearing <see cref="Instance"/>, so anything the teardown notifies
		/// still finds the registry and sees it unavailable rather than absent.
		/// </summary>
		private void OnDestroy()
		{
			TearDown();

			if (Instance == this)
				Instance = null;
		}

		/// <summary>
		/// Takes a lease on the anchor with this guid.
		///
		/// A guid always resolves to the same <see cref="AnchorHandle"/>, for as long as this
		/// registry lives. Releasing every lease unloads the anchor but does not replace the handle,
		/// so a cached handle reference and anything subscribed to its
		/// <see cref="AnchorHandle.StateChanged"/> keep working across a release and a later
		/// re-acquisition.
		/// </summary>
		public AnchorLease Acquire(SerializableGuid guid, AnchorSource source)
		{
			ThrowIfUnavailable();
			ThrowIfNoSource(source);

			AnchorHandle handle = GetOrCreateHandle(guid);
			handle.Retain(anchorManager.GetAnchor(guid), source);
			return new AnchorLease(handle, source);
		}

		/// <inheritdoc cref="Acquire(SerializableGuid, AnchorSource)"/>
		public AnchorLease Acquire(ARAnchor anchor, AnchorSource source)
		{
			if (anchor == null)
				throw new ArgumentNullException(nameof(anchor));

			ThrowIfUnavailable();
			ThrowIfNoSource(source);

			AnchorHandle handle = GetOrCreateHandle(anchor.trackableId);
			handle.Retain(anchor, source);
			return new AnchorLease(handle, source);
		}

		private AnchorHandle GetOrCreateHandle(SerializableGuid guid)
		{
			if (!handles.TryGetValue(guid, out AnchorHandle handle))
			{
				handle = new AnchorHandle(this, guid);
				handles.Add(guid, handle);
			}

			return handle;
		}

		/// <summary>
		/// Creates a brand-new anchor at <paramref name="pose"/> and starts holding it. This does
		/// not load an anchor from local storage or a shared group, and it does not persist the new
		/// anchor; call <see cref="TrySaveAsync(AnchorLease, CancellationToken)"/> separately if it
		/// should survive this session.
		///
		/// The returned lease permits local recovery if the runtime later removes the anchor. Returns
		/// null when the runtime cannot create the anchor. Although creation itself is not cancellable,
		/// cancellation while it is in flight removes the resulting anchor instead of registering it.
		/// </summary>
		public async Awaitable<AnchorLease> TryMintAsync(Pose pose, CancellationToken ctkn = default)
		{
			ThrowIfUnavailable();
			ctkn.ThrowIfCancellationRequested();

			Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(pose);

			if (!result.status.IsSuccess() || result.value == null)
			{
				if (result.value != null)
					RemoveAnchor(result.value);

				Debug.LogWarning($"Failed to mint anchor at {pose}: {result.status}");
				return null;
			}

			ARAnchor mintedAnchor = result.value;

			if (disposed || ctkn.IsCancellationRequested)
			{
				RemoveAnchor(mintedAnchor);
				ctkn.ThrowIfCancellationRequested();
				return null;
			}

			return Acquire(mintedAnchor, AnchorSource.Local);
		}

		// ------- sharing ------------------------------------------

		/// <summary>
		/// What the running runtime says about sharing. Only Meta's runtime shares anchors;
		/// under anything else (XR Simulation, no runtime at all) the answer is unsupported.
		/// </summary>
		public Supported sharedAnchorsSupport =>
			metaAnchorSubsystem?.isSharedAnchorsSupported ?? Supported.Unsupported;

		/// <summary>Whether an anchor can be shared or downloaded at all right now.</summary>
		public bool canShareAnchors => sharedAnchorsSupport == Supported.Supported;

		/// <summary>
		/// Shares one loaded anchor into the Meta group addressed by its guid. The lease must
		/// outlive the call — see <see cref="ValidateLease"/>.
		/// </summary>
		public async Awaitable<XRResultStatus> TryShareAsync(AnchorLease lease,
			CancellationToken ctkn = default)
		{
			AnchorHandle handle = ValidateLease(lease);

			ThrowIfDisposed();
			ctkn.ThrowIfCancellationRequested();

			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
			if (meta == null || sharedAnchorsSupport == Supported.Unsupported)
			{
				LogNoSharedAnchorsOnce();
				return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);
			}

			SerializableGuid guid = handle.guid;
			ARAnchor anchor = handle.anchor;

			if (anchor == null)
			{
				Debug.LogWarning($"Cannot share anchor {guid}: it is not loaded.");
				return new XRResultStatus(XRResultStatus.StatusCode.UnknownError);
			}

			// MetaOpenXRAnchorSubsystem reads sharedAnchorsGroupId synchronously inside the share
			// call, before it returns its Awaitable, so group-scoped operations may overlap.
			meta.sharedAnchorsGroupId = guid;
			XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);
			ctkn.ThrowIfCancellationRequested();
			return result;
		}

		/// <summary>
		/// A lease is how a caller proves the anchor will still exist when the operation lands.
		/// AR Foundation cannot be told to abandon a save or a share, and an anchor whose last lease
		/// goes away is destroyed on the spot — so the lease has to be held for the whole call, and
		/// it has to be one nothing else can release underneath it.
		/// </summary>
		private AnchorHandle ValidateLease(AnchorLease lease)
		{
			if (lease == null)
				throw new ArgumentNullException(nameof(lease));

			if (lease.isDisposed)
				throw new ObjectDisposedException(nameof(AnchorLease),
					"Hold the lease until the operation completes; its anchor may already be gone.");

			if (lease.Handle.owner != this)
				throw new ArgumentException(
					"This lease belongs to another AnchorRegistry.", nameof(lease));

			return lease.Handle;
		}

		/// <summary>
		/// Callers retry sharing and downloading on a timer, so a runtime that will never
		/// support either is worth saying once rather than once per attempt.
		/// </summary>
		private void LogNoSharedAnchorsOnce()
		{
			if (loggedNoSharedAnchors)
				return;

			loggedNoSharedAnchors = true;
			Debug.LogWarning("This anchor runtime has no shared anchors. They need Meta's " +
				"runtime: a headset, or the Meta XR Simulator in-editor.");
		}

		/// <summary>
		/// A lease that only permits local loading on a runtime with no anchor storage can never be
		/// satisfied, and it will keep retrying for as long as it is held. Name the cause once.
		/// </summary>
		private void LogNoSavedAnchorsOnce()
		{
			if (loggedNoSavedAnchors)
				return;

			loggedNoSavedAnchors = true;
			Debug.LogWarning("This anchor runtime cannot load saved anchors, so anchors leased " +
				"from local storage alone will not load.");
		}

		// ------- local persistence ---------------------------------

		/// <summary>
		/// Anchors this device is known to hold in persistent storage: whatever the registry saved
		/// or loaded this process, plus whatever the last
		/// <see cref="TryRefreshSavedGuidsAsync"/> discovered. A refresh replaces what it enumerated
		/// but never drops an anchor this process saved or loaded itself.
		///
		/// Without a refresh this is a cache, not a manifest — a fresh process starts out knowing
		/// nothing.
		/// </summary>
		public IReadOnlyCollection<SerializableGuid> savedGuids => savedGuidSet;

		/// <summary>
		/// Anchor storage is a per-runtime capability, not a given: Meta's runtime has it,
		/// AR Foundation's XR Simulation tracks anchors but keeps none of them past the
		/// session. Callers that mint anchors should treat saving as best-effort.
		/// </summary>
		public bool canSaveAnchors => descriptor?.supportsSaveAnchor ?? false;
		public bool canLoadSavedAnchors => descriptor?.supportsLoadAnchor ?? false;
		public bool canEraseSavedAnchors => descriptor?.supportsEraseAnchor ?? false;

		public bool canEnumerateSavedAnchors =>
			metaDiscoveryAvailable || (descriptor?.supportsGetSavedAnchorIds ?? false);

		private XRAnchorSubsystemDescriptor descriptor =>
			anchorManager != null ? anchorManager.descriptor : null;

		/// <summary>
		/// Whether anchor discovery can go through Meta's SDK. AR Foundation's provider leaves
		/// <see cref="XRAnchorSubsystemDescriptor.supportsGetSavedAnchorIds"/> false on Quest, but
		/// the runtime underneath it does support discovery (XR_META_spatial_entity_discovery) and
		/// OVRPlugin reaches it — this project enables MetaXRFeature alongside Unity's OpenXR stack.
		///
		/// OVRPlugin p/invokes a native library that only exists where the Meta runtime does, so
		/// treat a missing entry point as "not a Quest" rather than letting it escape.
		/// </summary>
		private static bool metaDiscoveryAvailable
		{
			get
			{
				try
				{
					return OVRPlugin.initialized;
				}
				catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
				{
					return false;
				}
			}
		}

		/// <summary>
		/// Whether this device is known to have <paramref name="guid"/> in persistent storage.
		/// Only as complete as <see cref="savedGuids"/> — a false result on a fresh process means
		/// "not saved as far as we know", not "not saved".
		/// </summary>
		public bool IsSaved(SerializableGuid guid) => savedGuidSet.Contains(guid);

		/// <summary>
		/// Persists a leased anchor to local storage, so a later session can
		/// <see cref="Acquire(SerializableGuid, AnchorSource)"/> it with
		/// <see cref="AnchorSource.Local"/>. The anchor stays loaded; this only writes it to the
		/// device. Fails if the anchor isn't loaded right now — there is nothing for the runtime to
		/// save. The lease must outlive the call — see <see cref="ValidateLease"/>.
		/// </summary>
		public async Awaitable<bool> TrySaveAsync(AnchorLease lease, CancellationToken ctkn = default)
		{
			AnchorHandle handle = ValidateLease(lease);

			ThrowIfDisposed();

			if (!canSaveAnchors)
			{
				Debug.LogWarning("This runtime cannot save anchors to local storage.");
				return false;
			}

			SerializableGuid guid = handle.guid;
			ARAnchor anchor = handle.anchor;

			if (anchor == null)
			{
				Debug.LogWarning($"Cannot save anchor {guid} locally: it is not loaded.");
				return false;
			}

			Result<SerializableGuid> result = await anchorManager.TrySaveAnchorAsync(anchor, ctkn);

			if (!result.status.IsSuccess())
			{
				Debug.LogWarning($"Failed to save anchor {guid} locally: {result.status}");
				return false;
			}

			// The registry addresses saved anchors by trackable id, which Meta's runtime promises is
			// the same value it hands back here. An anchor that landed anywhere else is unreachable
			// from here, so give the storage back rather than leave it holding something nothing can
			// ever ask for again.
			if (result.value != guid)
			{
				Debug.LogError($"Anchor {guid} saved under a different guid ({result.value}); " +
					"erasing it, as it cannot be loaded back by trackable id.");

				if (canEraseSavedAnchors)
					await anchorManager.TryEraseAnchorAsync(result.value, CancellationToken.None);

				return false;
			}

			if (!disposed)
				MarkSaved(guid);

			return true;
		}

		/// <summary>
		/// Writes a just-downloaded anchor to this device, so the next session loads it from local
		/// storage instead of paying for the group again. Best-effort and unawaited: nothing
		/// downstream depends on it, and a runtime with no anchor storage simply skips it.
		/// </summary>
		internal async void SaveDownloadedAnchor(AnchorHandle handle)
		{
			if (disposed || !canSaveAnchors || IsSaved(handle.guid))
				return;

			AnchorLease pin = null;

			try
			{
				// The save has to outlive the leases that wanted the download, and there is no way
				// to call it off part way, so hold the anchor for the duration.
				AnchorSource pinSource = handle.source;
				if (pinSource == AnchorSource.None)
					return;

				pin = Acquire(handle.guid, pinSource);
				await TrySaveAsync(pin, destroyCancellationToken);
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
				pin?.Dispose();
			}
		}

		/// <summary>
		/// Deletes an anchor's local save. Does not unload the anchor if it is currently loaded —
		/// releasing its leases does that.
		/// </summary>
		public async Awaitable<bool> TryEraseSavedAsync(SerializableGuid guid, CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			if (!canEraseSavedAnchors)
			{
				Debug.LogWarning("This runtime cannot erase locally saved anchors.");
				return false;
			}

			XRResultStatus status = await anchorManager.TryEraseAnchorAsync(guid, ctkn);

			if (status.IsError())
			{
				Debug.LogWarning($"Failed to erase saved anchor {guid}: {status}");
				return false;
			}

			savedGuidSet.Remove(guid);
			provenSavedGuids.Remove(guid);
			return true;
		}

		/// <summary>Records first-hand evidence that local storage holds this anchor.</summary>
		private void MarkSaved(SerializableGuid guid)
		{
			savedGuidSet.Add(guid);
			provenSavedGuids.Add(guid);
		}

		/// <summary>
		/// Repopulates <see cref="savedGuids"/> by asking the device what it has persisted, so a
		/// fresh process can find anchors it saved in an earlier one without being told their guids.
		///
		/// Returns everything Lasertag ever saved on this headset, including anchors belonging to
		/// somewhere else entirely. Which of them actually localize is the test for where you are.
		/// </summary>
		public async Awaitable<bool> TryRefreshSavedGuidsAsync(CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			if (metaDiscoveryAvailable)
				return await TryDiscoverSavedGuidsAsync(ctkn);

			// Anywhere that isn't a Quest, take AR Foundation's own enumeration if the provider
			// happens to implement it.
			if (!(descriptor?.supportsGetSavedAnchorIds ?? false))
				return false;

			Result<NativeArray<SerializableGuid>> result =
				await anchorManager.TryGetSavedAnchorIdsAsync(Allocator.Persistent, ctkn);

			NativeArray<SerializableGuid> ids = result.value;

			try
			{
				if (!result.status.IsSuccess())
				{
					Debug.LogWarning($"Failed to read saved anchor ids: {result.status}");
					return false;
				}

				ReplaceSavedGuids(ids);
				return true;
			}
			finally
			{
				if (ids.IsCreated)
					ids.Dispose();
			}
		}

		/// <summary>
		/// Discovery through Meta's SDK. The anchors come back as OVR handles, but their UUIDs are
		/// the same guids AR Foundation addresses anchors by on this runtime, so the results feed
		/// straight into <see cref="Acquire(SerializableGuid, AnchorSource)"/> with
		/// <see cref="AnchorSource.Local"/> — nothing needs to touch OVR again.
		/// </summary>
		private async Awaitable<bool> TryDiscoverSavedGuidsAsync(CancellationToken ctkn)
		{
			List<OVRAnchor> discovered = new();

			// Storable is what makes an anchor persistable in the first place, so it filters out
			// the system's own scene entities (room mesh, planes) that discovery also surfaces.
			OVRAnchor.FetchOptions options = new()
			{
				SingleComponentType = typeof(OVRStorable)
			};

			(bool answered, OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> result) =
				await WaitOrGiveUp(OVRAnchor.FetchAnchorsAsync(discovered, options), ctkn);

			if (!answered)
			{
				Debug.LogWarning("Anchor discovery never answered; treating it as unavailable.");
				return false;
			}

			if (!result.Success)
			{
				Debug.LogWarning($"Failed to discover saved anchors: {result.Status}");
				return false;
			}

			// OVR ignores cancellation tokens outright, so this is the only place the request can
			// honour one — and by now the work is already done.
			if (ctkn.IsCancellationRequested || disposed)
				return false;

			savedGuidSet.Clear();
			foreach (OVRAnchor anchor in discovered)
				savedGuidSet.Add(new SerializableGuid(anchor.Uuid));

			savedGuidSet.UnionWith(provenSavedGuids);
			return true;
		}

		/// <summary>
		/// How long an OVR request may go unanswered before the registry stops waiting on it.
		/// </summary>
		private const float OvrRequestTimeoutSeconds = 5f;

		/// <summary>
		/// Awaits an OVR request, giving up if the runtime never answers. Measured under the
		/// Meta XR Simulator: discovery and probe fetches simply never complete there, and
		/// since OVR tasks ignore cancellation, awaiting one directly wedges the caller — which
		/// took map discovery with it. Returns whether the request answered in time; the task
		/// itself is left to finish whenever it likes.
		///
		/// A request that already completed still counts, however long the wait it was given.
		/// Cancellation reads as unanswered rather than throwing, so that every OVR-backed query
		/// here returns its empty result on cancellation instead of some throwing and some not.
		/// </summary>
		private async Awaitable<(bool answered, T result)> WaitOrGiveUp<T>(
			OVRTask<T> task, CancellationToken ctkn,
			float timeoutSeconds = OvrRequestTimeoutSeconds)
		{
			bool answered = false;
			T value = default;

			async void Await()
			{
				try
				{
					value = await task;
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}
				finally
				{
					answered = true;
				}
			}

			Await();

			float deadline = time + timeoutSeconds;

			try
			{
				while (!answered && time < deadline)
					await Awaitable.NextFrameAsync(ctkn);
			}
			catch (OperationCanceledException)
			{
				return (false, default);
			}

			return (answered, value);
		}

		private void ReplaceSavedGuids(NativeArray<SerializableGuid> ids)
		{
			savedGuidSet.Clear();
			foreach (SerializableGuid id in ids)
				savedGuidSet.Add(id);

			savedGuidSet.UnionWith(provenSavedGuids);
		}

		/// <summary>
		/// Which of these saved anchors localize in the physical space the headset is standing
		/// in right now. This is the cheap first phase of map discovery:
		/// probing through Meta's locatable API leaves the scene untouched,
		/// and only the chosen map's anchors ever get committed to real ARAnchors.
		///
		/// Anchors saved in one space occasionally localize in another; a non-empty result is
		/// a strong hint, not proof, of which room this is.
		///
		/// Cancellation returns an empty set rather than throwing.
		/// </summary>
		/// <param name="timeoutSeconds">Bounds how long the runtime is given to localize the whole
		/// set. The metadata fetch that precedes it carries its own short bound on top.</param>
		public async Awaitable<HashSet<SerializableGuid>> ProbeLocalizableAsync(
			IReadOnlyCollection<SerializableGuid> guids, float timeoutSeconds,
			CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			HashSet<SerializableGuid> localized = new();

			if (guids.Count == 0 || !metaDiscoveryAvailable)
				return localized;

			List<Guid> uuids = new(guids.Count);
			foreach (SerializableGuid guid in guids)
				uuids.Add(guid.guid);

			List<OVRAnchor> fetched = new();
			(bool answered, OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> fetchResult) =
				await WaitOrGiveUp(OVRAnchor.FetchAnchorsAsync(fetched, new OVRAnchor.FetchOptions
				{
					Uuids = uuids
				}), ctkn);

			if (ctkn.IsCancellationRequested || disposed)
				return localized;

			if (!answered)
			{
				Debug.LogWarning("Anchor probe fetch never answered; probing nothing.");
				return localized;
			}

			if (!fetchResult.Success)
			{
				Debug.LogWarning($"Anchor probe fetch failed: {fetchResult.Status}");
				return localized;
			}

			// Enable every locatable first so the runtime searches for them all concurrently,
			// then collect the results.
			List<(OVRAnchor anchor, OVRLocatable locatable, OVRTask<bool> enable)> probes = new();

			foreach (OVRAnchor anchor in fetched)
			{
				if (!anchor.TryGetComponent(out OVRLocatable locatable))
					continue;

				probes.Add((anchor, locatable, locatable.SetEnabledAsync(true, timeoutSeconds)));
			}

			// The probes run concurrently, so timeoutSeconds bounds the phase, not each one —
			// waiting that long per probe would multiply the caller's budget by the anchor count.
			float probeDeadline = time + timeoutSeconds;

			foreach ((OVRAnchor anchor, OVRLocatable locatable, OVRTask<bool> enable) in probes)
			{
				(bool answeredEnable, bool enabled) =
					await WaitOrGiveUp(enable, ctkn, probeDeadline - time);

				if (answeredEnable && enabled &&
				    locatable.TryGetSpatialAnchorPose(out OVRLocatable.TrackingSpacePose pose) &&
				    pose.IsPositionTracked)
					localized.Add(new SerializableGuid(anchor.Uuid));

				// Leave nothing running behind the probe: an enabled locatable keeps the
				// runtime tracking it, and the same UUID may be loaded through AR Foundation
				// afterward. (Whether the two stacks interfere at all is still unverified on
				// device; disabling here minimizes the surface either way.)
				_ = locatable.SetEnabledAsync(false);
			}

			if (ctkn.IsCancellationRequested || disposed)
				localized.Clear();

			return localized;
		}

		// ------- internals -----------------------------------------

		internal bool isDisposed => disposed;
		internal float time => Time.unscaledTime;

		/// <summary>
		/// Brings an anchor in from whichever of <paramref name="source"/>'s origins works, local
		/// storage first. Local loads hand back a materialized anchor; shared downloads only report
		/// that the request landed, and the anchor shows up later through trackablesChanged.
		/// </summary>
		internal async Awaitable<AnchorLoadResult> TryLoadAsync(SerializableGuid guid, AnchorSource source)
		{
			bool localRequested = source.HasFlag(AnchorSource.Local);
			string localFailure = null;

			if (localRequested && canLoadSavedAnchors)
			{
				AnchorLoadResult local = await TryLoadSavedAnchorAsync(guid);
				if (local.succeeded)
					return local;

				localFailure = local.reason;
			}

			// A download started now would materialize an anchor after teardown, with no listener
			// left to route it to a handle and nothing to remove it.
			if (disposed)
				return AnchorLoadResult.Failed;

			if (source.HasFlag(AnchorSource.Shared))
			{
				AnchorLoadResult shared = await TryLoadSharedAnchorAsync(guid);
				if (shared.succeeded || localFailure == null)
					return shared;

				// Both origins were tried, so report both: either one alone reads as the whole
				// story of why the anchor isn't here.
				return AnchorLoadResult.Failure($"{localFailure}; {shared.reason}");
			}

			if (localFailure != null)
				return AnchorLoadResult.Failure(localFailure);

			// Nothing was even attempted: the only permitted source is one this runtime lacks.
			if (localRequested)
				LogNoSavedAnchorsOnce();

			return AnchorLoadResult.Failure("this runtime cannot load saved anchors");
		}

		internal async Awaitable<AnchorLoadResult> TryLoadSavedAnchorAsync(SerializableGuid guid)
		{
			Result<ARAnchor> result = await anchorManager.TryLoadAnchorAsync(guid);

			if (!result.status.IsSuccess() || result.value == null)
				return AnchorLoadResult.Failure($"local storage: {result.status}");

			// Unlike a shared download, this materialized an ARAnchor synchronously. If nothing
			// wants it anymore there's no reconciliation left to sweep it up, so do it here.
			if (disposed)
			{
				RemoveAnchor(result.value);
				return AnchorLoadResult.Failed;
			}

			if (result.value.trackableId != (TrackableId)guid)
			{
				Debug.LogError($"Saved anchor {guid} loaded as a different trackable " +
					$"({result.value.trackableId}); discarding it.");
				RemoveAnchor(result.value);
				return AnchorLoadResult.Failed;
			}

			MarkSaved(guid);
			return AnchorLoadResult.Materialized(result.value, AnchorSource.Local);
		}

		internal async Awaitable<AnchorLoadResult> TryLoadSharedAnchorAsync(SerializableGuid guid)
		{
			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
			if (meta == null)
			{
				LogNoSharedAnchorsOnce();
				return AnchorLoadResult.Failure("this runtime has no shared anchors");
			}

			List<XRAnchor> downloaded = new(1);

			// MetaOpenXRAnchorSubsystem reads sharedAnchorsGroupId synchronously inside the load
			// call, before it returns its Awaitable, so group-scoped operations may overlap.
			meta.sharedAnchorsGroupId = guid;
			XRResultStatus result =
				await anchorManager.TryLoadAllSharedAnchorsAsync(downloaded, null);

			if (result.IsError())
				return AnchorLoadResult.Failure($"shared group: {result}");

			if (downloaded.Count == 0)
				return AnchorLoadResult.Failure("shared group was empty");

			return AnchorLoadResult.Downloading;
		}

		internal void RemoveAnchor(ARAnchor anchor)
		{
			if (anchor == null)
				return;

			// ARTrackableManager destroys a trackable's GameObject itself once the subsystem reports
			// the removal, so only clean up after it where it won't — destroying the object out from
			// under it hands the removal event a dead ARAnchor.
			bool managerWillDestroy = anchorManager != null &&
				anchorManager.TryRemoveAnchor(anchor) && anchor.destroyOnRemoval;

			if (!managerWillDestroy && anchor != null && anchor.gameObject != null)
				Object.Destroy(anchor.gameObject);
		}

		private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> eventData)
		{
			foreach (ARAnchor anchor in eventData.added)
				if (TryGetHandleFor(anchor.trackableId, out AnchorHandle handle))
					handle.OnAnchorAdded(anchor);

			foreach ((SerializableGuid guid, ARAnchor _) in eventData.removed)
				if (TryGetHandleFor(guid, out AnchorHandle handle))
					handle.OnAnchorRemoved();
		}

		private bool TryGetHandleFor(SerializableGuid id, out AnchorHandle handle) =>
			handles.TryGetValue(id, out handle);

		private async void ReconciliationLoop(CancellationToken ctkn)
		{
			while (!ctkn.IsCancellationRequested)
			{
				try
				{
					await Awaitable.NextFrameAsync(ctkn);
				}
				catch (OperationCanceledException)
				{
					return;
				}

				reconciliationSnapshot.Clear();
				reconciliationSnapshot.AddRange(handles.Values);

				// Contained per handle: reconciling calls out to consumers, and one of them
				// throwing must not stop every other anchor in the process from reconciling again.
				foreach (AnchorHandle handle in reconciliationSnapshot)
				{
					if (handle.isIdle)
						continue;

					try
					{
						handle.Reconcile();
					}
					catch (Exception e)
					{
						Debug.LogException(e);
					}
				}
			}
		}

		private void ThrowIfDisposed()
		{
			if (disposed)
				throw new ObjectDisposedException(nameof(AnchorRegistry));
		}

		/// <summary>
		/// For the operations that cannot degrade — there is no anchor to hand back without a
		/// runtime to create it. Callers ask <see cref="IsAvailable"/> first.
		/// </summary>
		private void ThrowIfUnavailable()
		{
			ThrowIfDisposed();

			if (anchorSubsystem == null)
				throw new InvalidOperationException(
					"No anchor runtime is available in this session.");
		}

		private static void ThrowIfNoSource(AnchorSource source)
		{
			if (source == AnchorSource.None)
				throw new ArgumentException(
					"A lease must permit at least one anchor source.", nameof(source));
		}

		/// <summary>
		/// Ends the registry, as its component is destroyed: it stops observing trackables, gives
		/// back every anchor it was holding, and leaves its handles terminal. Idempotent, and
		/// one-way — <see cref="Acquire(SerializableGuid, AnchorSource)"/> throws from here on, so
		/// no guid can gain a second handle and nothing is left to drive.
		/// </summary>
		private void TearDown()
		{
			if (disposed)
				return;

			disposed = true;

			if (anchorManager != null)
				anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

			// Nothing reconciles these handles again, so an anchor left loaded is one no lease can
			// ever release and only this registry knew how to find.
			foreach (AnchorHandle handle in handles.Values)
			{
				RemoveAnchor(handle.anchor);
				handle.Abandon();
			}

			handles.Clear();
		}
	}

	/// <summary>
	/// The outcome of one attempt to bring an anchor in. A local load produces the anchor outright;
	/// a shared download only succeeds at the request, and AR Foundation materializes the anchor
	/// some frames later.
	/// </summary>
	internal readonly struct AnchorLoadResult
	{
		public static AnchorLoadResult Failed => default;

		/// <summary>The download request landed; the anchor arrives through trackablesChanged.</summary>
		public static AnchorLoadResult Downloading =>
			new(true, null, null, AnchorSource.Shared);

		public static AnchorLoadResult Materialized(ARAnchor anchor, AnchorSource origin) =>
			new(true, anchor, null, origin);

		/// <summary>
		/// A failure that carries why. An anchor that is simply not here is an ordinary outcome and
		/// stays quiet per attempt; the reason surfaces once a handle has failed repeatedly.
		/// </summary>
		public static AnchorLoadResult Failure(string reason) =>
			new(false, null, reason, AnchorSource.None);

		private AnchorLoadResult(bool succeeded, ARAnchor anchor, string reason, AnchorSource origin)
		{
			this.succeeded = succeeded;
			this.anchor = anchor;
			this.reason = reason;
			this.origin = origin;
		}

		public bool succeeded { get; }
		public ARAnchor anchor { get; }
		public string reason { get; }

		/// <summary>Where the anchor came from, which decides whether it is worth saving locally.</summary>
		public AnchorSource origin { get; }
	}

	/// <summary>
	/// A reversible claim that an <see cref="AnchorHandle"/> should remain loaded.
	/// Releasing the final lease requests unloading; it does not dispose an in-flight handle.
	/// </summary>
	public sealed class AnchorLease : IDisposable
	{
		private bool disposed;

		internal AnchorLease(AnchorHandle handle, AnchorSource source)
		{
			Handle = handle ?? throw new ArgumentNullException(nameof(handle));
			Source = source;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// A dropped lease pins its anchor for the rest of the session with nothing left able to
		/// release it, and the symptom — an anchor that never unloads — points nowhere near the
		/// holder that lost it.
		/// </summary>
		~AnchorLease()
		{
			if (!disposed)
				Debug.LogError($"An anchor lease on {Handle.guid} was collected without being " +
					"disposed. Its anchor can no longer be released.");
		}
#endif

		public AnchorHandle Handle { get; }

		/// <summary>Where this lease permits its handle to load the anchor from.</summary>
		public AnchorSource Source { get; }

		internal bool isDisposed => disposed;

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			GC.SuppressFinalize(this);
			Handle.Release(Source);
		}
	}

	/// <summary>
	/// Reconciles the desired local presence of one anchor with AR Foundation's observed,
	/// asynchronously materialized anchor state.
	///
	/// The union of its live leases' <see cref="AnchorSource"/>s decides where a load may pull
	/// from, so one holder asking for a locally saved anchor and another asking for a shared
	/// download share a single handle without fighting over it.
	///
	/// A lease is a standing ask, so loading is endless rather than budgeted: an anchor that will
	/// not load right now is retried under a backoff for as long as any lease is held. A shared
	/// anchor whose owner has not uploaded it yet is a wait, not a verdict.
	///
	/// One handle serves a guid for the whole life of its registry, leased or not, so a handle
	/// reference and its <see cref="StateChanged"/> subscriptions are safe to hold onto.
	/// </summary>
	public sealed class AnchorHandle
	{
		public enum State
		{
			Unloaded,
			Loading,
			Materializing,
			Active,
			Removing,

			/// <summary>
			/// Terminal: the handle has outlived its registry, so it has no anchor and nothing left
			/// to drive it. Callers waiting on <see cref="Active"/> must treat this as the other way
			/// out; retrying is not one, since no registry remains to retry against.
			/// </summary>
			Failed
		}

		private const float RetryStepSeconds = 3f;
		private const float MaximumRetrySeconds = 30f;

		/// <summary>
		/// How many failed attempts before the handle says out loud that it is struggling. It keeps
		/// retrying either way, so this is said once per streak rather than per attempt.
		/// </summary>
		private const int AttemptsBeforeWarning = 3;

		/// <summary>
		/// How long a handle waits for a requested anchor to surface through trackablesChanged.
		/// Generous next to the handful of frames it should take, so that a download which never
		/// materializes spends its retry budget instead of waiting forever.
		/// </summary>
		private const float MaterializeTimeoutSeconds = 10f;

		private readonly AnchorRegistry registry;

		private int localLeaseCount;
		private int sharedLeaseCount;
		private bool loadInFlight;
		private bool materializedDuringLoad;
		private bool warnedAboutFailures;
		private bool saveWhenActive;
		private float retryAt;
		private float materializeDeadline;

		private bool reconcilingCurrently;
		private bool shouldReconcileAgain;

		internal AnchorHandle(AnchorRegistry registry, SerializableGuid guid)
		{
			this.registry = registry;
			this.guid = guid;
			state = State.Unloaded;
		}

		public event Action<AnchorHandle> StateChanged = delegate { };

		public SerializableGuid guid { get; }
		public State state { get; private set; }
		public ARAnchor anchor { get; private set; }
		public bool desiredLoaded => localLeaseCount > 0 || sharedLeaseCount > 0;

		/// <summary>
		/// How many attempts this handle has failed in a row. Nothing here acts on it — it is for
		/// holders that want to stop asking, since the handle itself never will.
		/// </summary>
		public int failedLoadCount { get; private set; }

		/// <summary>Why the last attempt failed. Null after progress.</summary>
		public string lastFailureReason { get; private set; }

		internal AnchorRegistry owner => registry;

		/// <summary>Whether an observed anchor is the one this handle is waiting for.</summary>
		private bool Matches(ARAnchor observedAnchor) =>
			observedAnchor.trackableId == (TrackableId)guid;

		/// <summary>Everywhere the current leases collectively allow this anchor to load from.</summary>
		public AnchorSource source =>
			(localLeaseCount > 0 ? AnchorSource.Local : AnchorSource.None) |
			(sharedLeaseCount > 0 ? AnchorSource.Shared : AnchorSource.None);

		/// <summary>
		/// Nothing left to reconcile: no lease wants the anchor, none is held, and nothing is in
		/// flight. Only a new lease or an observed anchor moves the handle out of this, and both
		/// reconcile it directly, so the registry's sweep can pass over it.
		/// </summary>
		internal bool isIdle =>
			!desiredLoaded &&
			!loadInFlight &&
			(state == State.Unloaded || state == State.Failed) &&
			anchor == null;

		internal void Retain(ARAnchor observedAnchor, AnchorSource leaseSource)
		{
			if (leaseSource.HasFlag(AnchorSource.Local))
				localLeaseCount++;

			if (leaseSource.HasFlag(AnchorSource.Shared))
				sharedLeaseCount++;

			// A new lease is a new ask, so whatever the last one was backing off from is worth
			// trying again now rather than at the end of its wait.
			ClearLoadFailures();
			if (state == State.Failed)
				SetState(State.Unloaded);

			if (observedAnchor != null)
				ObserveAnchor(observedAnchor);

			Reconcile();
		}

		internal void Release(AnchorSource leaseSource)
		{
			if (leaseSource.HasFlag(AnchorSource.Local))
			{
				if (localLeaseCount == 0)
					Debug.LogError($"Anchor handle {guid} was released more times than it was acquired.");
				else
					localLeaseCount--;
			}

			if (leaseSource.HasFlag(AnchorSource.Shared))
			{
				if (sharedLeaseCount == 0)
					Debug.LogError($"Anchor handle {guid} was released more times than it was acquired.");
				else
					sharedLeaseCount--;
			}

			Reconcile();
		}

		internal void OnAnchorAdded(ARAnchor addedAnchor)
		{
			if (!Matches(addedAnchor))
				return;

			ObserveAnchor(addedAnchor);
			Reconcile();
		}

		internal void OnAnchorRemoved()
		{
			// A removal for a guid this handle isn't currently holding is one of its own past
			// unloads catching up. Acting on it would knock a freshly re-leased handle out of
			// Loading or Materializing and start a second, redundant load.
			if (anchor == null)
				return;

			anchor = null;
			SetState(State.Unloaded);
			Reconcile();
		}

		/// <summary>
		/// The registry has given this handle's anchor back and will not reconcile it again, so the
		/// handle stops reporting one. Terminal, so callers waiting on an anchor stop waiting; leases
		/// stay counted, so releasing one afterwards is still balanced.
		/// </summary>
		internal void Abandon()
		{
			anchor = null;
			SetState(State.Failed);
		}

		internal void Reconcile()
		{
			if (registry.isDisposed)
				return;

			if (reconcilingCurrently)
			{
				shouldReconcileAgain = true;
				return;
			}

			do
			{
				shouldReconcileAgain = false;
				reconcilingCurrently = true;

				try
				{
					ReconcileOnce();
				}
				finally
				{
					reconcilingCurrently = false;
				}
			} while (shouldReconcileAgain);
		}

		private void ReconcileOnce()
		{
			// A requested anchor that never surfaces would otherwise hold the handle in
			// Materializing forever, unable to retry and never idle.
			if (state == State.Materializing && registry.time >= materializeDeadline)
			{
				SetState(State.Unloaded);
				ScheduleRetry();
			}

			if (desiredLoaded)
			{
				if (anchor != null)
				{
					SetState(State.Active);
					return;
				}

				if (loadInFlight || state == State.Materializing || state == State.Failed)
					return;

				if (registry.time >= retryAt)
					StartLoad();

				return;
			}

			if (anchor != null)
			{
				RemoveAnchor();
				return;
			}

			if (loadInFlight || state == State.Materializing)
				return;

			// Failed is terminal, so an abandoned handle does not quietly look loadable again.
			if (state != State.Failed)
				SetState(State.Unloaded);
		}

		private void StartLoad()
		{
			if (loadInFlight)
				return;

			loadInFlight = true;
			materializedDuringLoad = false;
			SetState(State.Loading);
			RunLoadAsync(source);
		}

		private async void RunLoadAsync(AnchorSource loadSource)
		{
			AnchorLoadResult result = AnchorLoadResult.Failed;

			try
			{
				result = await registry.TryLoadAsync(guid, loadSource);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			finally
			{
				loadInFlight = false;
				lastFailureReason = result.reason;

				// The registry tore down mid-load and already abandoned this handle. Anything that
				// arrived is orphaned, and the terminal state stands.
				if (registry.isDisposed)
				{
					registry.RemoveAnchor(result.anchor);
				}
				else
				{
					// A download only reports that the request landed, so record where the anchor
					// will have come from before it arrives — saving it is what spares the next
					// session the same round trip.
					if (result.origin.HasFlag(AnchorSource.Shared))
						saveWhenActive = true;

					// A local load hands the anchor straight back rather than routing it through
					// trackablesChanged, so adopt it here. A refused anchor leaves this a failed
					// attempt, since nothing else is going to arrive for it.
					if (result.anchor != null)
						ObserveAnchor(result.anchor);

					if (anchor != null)
					{
						ClearLoadFailures();
						SetState(State.Active);

						// The download can materialize its anchor before the request that asked
						// for it resolves, in which case this is the first point that knows the
						// anchor is worth saving.
						SaveIfDownloaded();
					}
					else if (result.succeeded && result.anchor == null && !materializedDuringLoad)
					{
						ClearLoadFailures();
						SetState(State.Materializing);
						materializeDeadline = registry.time + MaterializeTimeoutSeconds;
					}
					else if ((source & ~loadSource) != AnchorSource.None)
					{
						// A lease acquired mid-load widened where this handle may look. Backing off
						// now would sit out the wait on an origin nothing has tried yet.
						SetState(State.Unloaded);
						retryAt = 0;
					}
					else
					{
						SetState(State.Unloaded);
						ScheduleRetry();
					}

					Reconcile();
				}
			}
		}

		/// <summary>
		/// Adopts an observed anchor as this handle's. Returns false for an anchor addressed to
		/// some other handle: that breaks the registry's one-guid-one-anchor invariant, so it is
		/// reported rather than adopted — silently taking it would corrupt both handles.
		/// </summary>
		private bool ObserveAnchor(ARAnchor observedAnchor)
		{
			if (!Matches(observedAnchor))
			{
				Debug.LogError($"Anchor {observedAnchor.trackableId} was offered to handle {guid}; " +
					"ignoring it.");
				return false;
			}

			if (loadInFlight)
				materializedDuringLoad = true;

			anchor = observedAnchor;
			ClearLoadFailures();
			SetState(State.Active);
			SaveIfDownloaded();
			return true;
		}

		/// <summary>
		/// Hands a downloaded anchor to local storage now that it exists, so the next session loads
		/// it from this device instead of the group again. Anchors that came from local storage are
		/// saved by definition and skip this.
		/// </summary>
		private void SaveIfDownloaded()
		{
			if (!saveWhenActive || anchor == null)
				return;

			saveWhenActive = false;
			registry.SaveDownloadedAnchor(this);
		}

		private void RemoveAnchor()
		{
			ARAnchor anchorToRemove = anchor;

			SetState(State.Removing);

			// Removal destroys an anchor that may cost a network round trip to get back, so a
			// consumer that leased during the notification keeps the one already here.
			if (desiredLoaded)
			{
				SetState(State.Active);
				return;
			}

			anchor = null;
			registry.RemoveAnchor(anchorToRemove);
			SetState(State.Unloaded);

			Reconcile();
		}

		/// <summary>
		/// Backs off before the next attempt. A held lease is a standing ask, so there is no last
		/// attempt: an anchor whose owner has not shared it yet becomes loadable later, and giving
		/// up would strand a consumer that is still waiting.
		/// </summary>
		private void ScheduleRetry()
		{
			if (!desiredLoaded)
			{
				retryAt = 0;
				return;
			}

			failedLoadCount++;

			if (failedLoadCount >= AttemptsBeforeWarning && !warnedAboutFailures)
			{
				warnedAboutFailures = true;
				Debug.LogWarning($"Anchor {guid} has failed to load {failedLoadCount} times and " +
					"will keep retrying while it is leased. Last failure: " +
					$"{lastFailureReason ?? "never materialized"}.");
			}

			retryAt = registry.time +
				Mathf.Min(RetryStepSeconds * failedLoadCount, MaximumRetrySeconds);
		}

		/// <summary>Clears the failure streak; progress and fresh leases both count as one.</summary>
		private void ClearLoadFailures()
		{
			failedLoadCount = 0;
			retryAt = 0;
			lastFailureReason = null;
			warnedAboutFailures = false;
		}

		private void SetState(State next)
		{
			if (state == next)
				return;

			state = next;

			// Contained, so that a subscriber throwing cannot unwind the state machine that just
			// invoked it and leave the handle inconsistent with the anchor it holds.
			try
			{
				StateChanged.Invoke(this);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}
	}
}
