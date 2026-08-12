using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using Object = UnityEngine.Object;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	/// <summary>
	/// Where an <see cref="AnchorHandle"/> may get its anchor from. Flags, so one lease can permit
	/// both. Local storage is tried first: it needs no network and materializes immediately.
	/// </summary>
	[Flags]
	public enum AnchorSource
	{
		None = 0,

		/// <summary>
		/// This device's persistent anchor storage. Only anchors previously handed to
		/// <see cref="AnchorRegistry.TrySaveAsync(AnchorLease, CancellationToken)"/> — in this
		/// session or an earlier one — can be loaded this way.
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
	/// Owns the local AR Foundation anchor handles for a process, routes trackable events to them,
	/// and fronts AR Foundation's persistent anchor storage.
	///
	/// AR Foundation anchor operations are not cancellable, so this holds anchors nothing wants
	/// anymore until the runtime is done with them.
	///
	/// Everything is keyed by trackable id. Meta's runtime guarantees the guid returned when saving
	/// or sharing an anchor *is* that anchor's trackable id, so one guid addresses an anchor
	/// locally, in the shared group, and here.
	///
	/// Every capability past creating and tracking anchors is asked about at runtime: AR
	/// Foundation's XR Simulation persists nothing, and only Meta's runtime — a headset, or the
	/// Meta XR Simulator in-editor — shares anchors or enumerates what this device has saved.
	/// </summary>
	[DefaultExecutionOrder(-300)]
	public sealed class AnchorRegistry : MonoBehaviour
	{
		public static AnchorRegistry Instance { get; private set; }

		private ARAnchorManager anchorManager;

		/// <summary>One handle per guid, kept for the registry's lifetime.</summary>
		private readonly Dictionary<SerializableGuid, AnchorHandle> handles = new();
		private readonly HashSet<SerializableGuid> savedGuidSet = new();

		/// <summary>
		/// Anchors this process saw in local storage first-hand. Device enumeration replaces the
		/// rest of <see cref="savedGuidSet"/> but cannot outvote these: discovery only surfaces what
		/// is around the headset.
		/// </summary>
		private readonly HashSet<SerializableGuid> provenSavedGuids = new();

		/// <summary>
		/// When a guid whose local load failed may be asked of local storage again. An anchor this
		/// device holds fails to load until the headset localizes the space it was saved in, which
		/// is a wall-clock wait rather than a number of attempts.
		/// </summary>
		private readonly Dictionary<SerializableGuid, float> localRetryAt = new();

		/// <summary>How long a failed local load defers the next one.</summary>
		private const float LocalRecheckSeconds = 20f;

		/// <summary>
		/// Anchors erased while an enumeration was in flight. Subtracted from that listing, which
		/// was gathered before they went and still names other anchors correctly.
		/// </summary>
		private readonly HashSet<SerializableGuid> unsavedDuringRefresh = new();

		private bool refreshInFlight;

		/// <summary>What the last pass found localizable.</summary>
		private readonly HashSet<SerializableGuid> lastLocalized = new();

		/// <summary>
		/// Anchors handed back to the runtime whose removal it has not reported yet, against the
		/// time each stops being waited on. Until then the anchor is still in the manager's
		/// trackables and its guid is still taken, so no handle may be given it or load it again.
		/// </summary>
		private readonly Dictionary<TrackableId, float> removalsInFlight = new();

		/// <summary>
		/// How long a removal may go unreported before the registry stops holding the guid for it.
		/// Only covers a runtime that stops answering; the manager normally reports next update.
		/// </summary>
		private const float RemovalTimeoutSeconds = 5f;

		private readonly List<AnchorHandle> reconciliationSnapshot = new();

		private bool disposed;
		private bool loggedNoSharedAnchors;
		private bool loggedNoSavedAnchors;

		/// <summary>
		/// Resolved live rather than cached: the manager creates its subsystem when it is enabled,
		/// which can be after this component wakes.
		/// </summary>
		private XRAnchorSubsystem anchorSubsystem =>
			anchorManager != null ? anchorManager.subsystem : null;

		/// <summary>
		/// Null under AR Foundation's XR Simulation, which tracks anchors but implements none of
		/// Meta's extensions (sharing, local persistence, discovery).
		/// </summary>
		private MetaOpenXRAnchorSubsystem metaAnchorSubsystem =>
			anchorSubsystem as MetaOpenXRAnchorSubsystem;

		/// <summary>
		/// False where this process has no anchor runtime at all — a rig with no AR managers, or an
		/// editor session with no simulation running. A normal state, not a failure.
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
				// Not an error: everything downstream runs without anchors.
				Debug.LogWarning("AnchorRegistry found no ARAnchorManager. " +
					"Anchors are unavailable for this session.", this);
				return;
			}

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);

			// Unity cancels this token as the component is destroyed, so the loop ends with the
			// registry and needs no source of our own underneath it.
			ReconciliationLoop(destroyCancellationToken);
		}

		/// <summary>
		/// Tears down before clearing <see cref="Instance"/>, so anything the teardown notifies
		/// finds the registry unavailable rather than absent.
		/// </summary>
		private void OnDestroy()
		{
			TearDown();

			if (Instance == this)
				Instance = null;
		}

		/// <summary>
		/// Takes a lease on the anchor with this guid. A guid always resolves to the same
		/// <see cref="AnchorHandle"/> for as long as this registry lives, so a cached handle
		/// reference keeps working across a release and a later re-acquisition.
		/// </summary>
		public AnchorLease Acquire(SerializableGuid guid, AnchorSource source)
		{
			ThrowIfUnavailable();

			if (source == AnchorSource.None)
				throw new ArgumentException(
					"A lease must permit at least one anchor source.", nameof(source));

			AnchorHandle handle = GetOrCreateHandle(guid);
			handle.Retain(FindAdoptableAnchor(guid), source);
			return new AnchorLease(handle, source);
		}

		/// <summary>
		/// The manager's anchor for this guid, unless it is one this registry has handed back and is
		/// still waiting to see gone — see <see cref="removalsInFlight"/>.
		/// </summary>
		private ARAnchor FindAdoptableAnchor(SerializableGuid guid) =>
			IsRemovalInFlight(guid) ? null : anchorManager.GetAnchor(guid);

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
		/// Creates a brand-new anchor at <paramref name="pose"/> and starts holding it. Does not
		/// persist it; call <see cref="TrySaveAsync(AnchorLease, CancellationToken)"/> for that.
		/// Null when the runtime cannot create it. Creation is not cancellable, so cancellation
		/// while it is in flight removes the resulting anchor instead of registering it.
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

			// Nothing else knows this anchor exists yet, so whatever stops it being registered has
			// to give it back here.
			if (!IsAvailable || ctkn.IsCancellationRequested)
			{
				RemoveAnchor(mintedAnchor);
				ctkn.ThrowIfCancellationRequested();
				return null;
			}

			// Releasing the last lease gives it back to the runtime and destroys its GameObject,
			// exactly as it would one the registry loaded itself.
			AnchorHandle handle = GetOrCreateHandle(mintedAnchor.trackableId);
			handle.Retain(mintedAnchor, AnchorSource.Local);
			return new AnchorLease(handle, AnchorSource.Local);
		}

		// ------- sharing ------------------------------------------

		/// <summary>Only Meta's runtime shares anchors; anything else answers unsupported.</summary>
		public Supported sharedAnchorsSupport =>
			metaAnchorSubsystem?.isSharedAnchorsSupported ?? Supported.Unsupported;

		/// <summary>Whether an anchor can be shared or downloaded at all right now.</summary>
		public bool canShareAnchors => sharedAnchorsSupport == Supported.Supported;

		/// <summary>
		/// Meta's subsystem when this runtime shares anchors, null otherwise. Callers point
		/// <see cref="MetaOpenXRAnchorSubsystem.sharedAnchorsGroupId"/> at their group immediately
		/// before the call that uses it; the subsystem reads it synchronously, before returning its
		/// Awaitable, so group-scoped operations may overlap.
		/// </summary>
		private MetaOpenXRAnchorSubsystem TryGetSharingSubsystem()
		{
			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;

			if (meta != null && sharedAnchorsSupport != Supported.Unsupported)
				return meta;

			LogNoSharedAnchorsOnce();
			return null;
		}

		/// <summary>Shares one loaded anchor into the Meta group addressed by its guid.</summary>
		public async Awaitable<XRResultStatus> TryShareAsync(AnchorLease lease,
			CancellationToken ctkn = default)
		{
			AnchorHandle handle = ValidateLease(lease);

			ThrowIfDisposed();
			ctkn.ThrowIfCancellationRequested();

			MetaOpenXRAnchorSubsystem meta = TryGetSharingSubsystem();
			if (meta == null)
				return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);

			SerializableGuid guid = handle.guid;
			ARAnchor anchor = handle.anchor;

			if (anchor == null)
			{
				Debug.LogWarning($"Cannot share anchor {guid}: it is not loaded.");
				return new XRResultStatus(XRResultStatus.StatusCode.UnknownError);
			}

			// The upload cannot be called off part way, so the anchor has to outlive it however
			// briefly nothing else wants it.
			handle.BeginOperation();

			try
			{
				meta.sharedAnchorsGroupId = guid;
				XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);
				ctkn.ThrowIfCancellationRequested();
				return result;
			}
			finally
			{
				handle.EndOperation();
			}
		}

		/// <summary>
		/// A lease authorizes operating on an anchor; it is not what keeps the anchor alive for the
		/// operation. The registry pins the handle itself, since AR Foundation cannot be told to
		/// abandon a save or a share once it has started.
		/// </summary>
		private AnchorHandle ValidateLease(AnchorLease lease)
		{
			if (lease == null)
				throw new ArgumentNullException(nameof(lease));

			if (lease.isDisposed)
				throw new ObjectDisposedException(nameof(AnchorLease),
					"A disposed lease no longer authorizes anything.");

			if (lease.Handle.owner != this)
				throw new ArgumentException(
					"This lease belongs to another AnchorRegistry.", nameof(lease));

			return lease.Handle;
		}

		/// <summary>Callers retry on a timer, so an unsupporting runtime is named once.</summary>
		private void LogNoSharedAnchorsOnce()
		{
			if (loggedNoSharedAnchors)
				return;

			loggedNoSharedAnchors = true;
			Debug.LogWarning("This anchor runtime has no shared anchors. They need Meta's " +
				"runtime: a headset, or the Meta XR Simulator in-editor.");
		}

		/// <summary>
		/// A local-only lease on a runtime with no anchor storage can never be satisfied, and it
		/// retries for as long as it is held. Name the cause once.
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
		/// Anchor storage is a per-runtime capability: Meta's runtime has it, XR Simulation keeps
		/// nothing past the session. Callers that mint anchors treat saving as best-effort.
		/// </summary>
		public bool canSaveAnchors => descriptor?.supportsSaveAnchor ?? false;

		private bool canLoadSavedAnchors => descriptor?.supportsLoadAnchor ?? false;
		private bool canEraseSavedAnchors => descriptor?.supportsEraseAnchor ?? false;

		private XRAnchorSubsystemDescriptor descriptor =>
			anchorManager != null ? anchorManager.descriptor : null;

		/// <summary>
		/// AR Foundation's provider leaves
		/// <see cref="XRAnchorSubsystemDescriptor.supportsGetSavedAnchorIds"/> false on Quest, but
		/// the runtime underneath does support discovery (XR_META_spatial_entity_discovery) and
		/// <see cref="MetaSpatialEntities"/> reaches it — this project enables MetaXRFeature
		/// alongside Unity's OpenXR stack.
		/// </summary>
		private static bool metaDiscoveryAvailable => MetaSpatialEntities.IsAvailable;

		/// <summary>
		/// Whether this device is known to hold <paramref name="guid"/>. Only as complete as the
		/// last <see cref="TryRefreshLocalizableAsync"/>: without one, false means "not as far as we
		/// know", not "not saved".
		/// </summary>
		public bool IsSaved(SerializableGuid guid) => savedGuidSet.Contains(guid);

		/// <summary>
		/// Persists a leased anchor to this device, so a later session can
		/// <see cref="Acquire(SerializableGuid, AnchorSource)"/> it with
		/// <see cref="AnchorSource.Local"/>. The anchor stays loaded. Fails if it is not loaded
		/// right now — there is nothing for the runtime to save.
		/// </summary>
		public Awaitable<bool> TrySaveAsync(AnchorLease lease, CancellationToken ctkn = default) =>
			TrySaveHandleAsync(ValidateLease(lease), ctkn);

		private async Awaitable<bool> TrySaveHandleAsync(AnchorHandle handle, CancellationToken ctkn)
		{
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

			// The write cannot be called off part way, so the anchor has to outlive it however
			// briefly nothing else wants it.
			handle.BeginOperation();

			try
			{
				Result<SerializableGuid> result = await anchorManager.TrySaveAnchorAsync(anchor, ctkn);

				if (!result.status.IsSuccess())
				{
					Debug.LogWarning($"Failed to save anchor {guid} locally: {result.status}");
					return false;
				}

				// Saved anchors are addressed by trackable id, which Meta's runtime promises is the
				// value it hands back here. An anchor that landed under anything else can never be
				// asked for again, so give the storage back.
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
			finally
			{
				handle.EndOperation();
			}
		}

		/// <summary>
		/// Writes a just-downloaded anchor to this device, so the next session skips the group.
		/// Best-effort and unawaited. Runs whether anything still wants the anchor: the round
		/// trip is already paid for, and the write pins the anchor for its own duration.
		/// </summary>
		internal async void SaveDownloadedAnchor(AnchorHandle handle)
		{
			if (disposed || !canSaveAnchors || IsSaved(handle.guid))
				return;

			try
			{
				await TrySaveHandleAsync(handle, destroyCancellationToken);
			}
			catch (OperationCanceledException)
			{
			}
			catch (ObjectDisposedException)
			{
				// The registry tore down while the write was in flight.
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		/// <summary>
		/// Deletes an anchor's local save. Does not unload a loaded anchor — releasing its leases
		/// does that.
		/// </summary>
		public async Awaitable<bool> TryEraseSavedAsync(SerializableGuid guid,
			CancellationToken ctkn = default)
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

			MarkNotSaved(guid);
			return true;
		}

		/// <summary>Records first-hand evidence that local storage holds this anchor.</summary>
		private void MarkSaved(SerializableGuid guid)
		{
			savedGuidSet.Add(guid);
			provenSavedGuids.Add(guid);
			localRetryAt.Remove(guid);
		}

		/// <summary>
		/// Only for anchors this registry erased. A load failing is not evidence of absence, since
		/// an anchor saved in a space the headset is not standing in fails every time and is still
		/// there; <see cref="TryRefreshLocalizableAsync"/> is what answers that.
		/// </summary>
		private void MarkNotSaved(SerializableGuid guid)
		{
			savedGuidSet.Remove(guid);
			provenSavedGuids.Remove(guid);

			if (refreshInFlight)
				unsavedDuringRefresh.Add(guid);
		}

		/// <summary>
		/// Repopulates what <see cref="IsSaved"/> reports by asking the device what it has
		/// persisted, and reports which of those anchors localize in the space the headset is
		/// standing in right now. One pass answers both, since the runtime only surfaces what it can
		/// find around the headset. Probing commits no ARAnchors.
		///
		/// Anchors saved in one space occasionally localize in another, so a non-empty result is a
		/// strong hint rather than proof of which room this is.
		///
		/// Null where the runtime could not answer, where another pass is already running, or on
		/// cancellation — none of which is the same as nothing being here.
		/// </summary>
		/// <param name="probeTimeoutSeconds">Bounds how long the runtime is given to localize the
		/// whole set. The discovery that precedes it carries
		/// <see cref="DiscoveryTimeoutSeconds"/> on top.</param>
		public async Awaitable<HashSet<SerializableGuid>> TryRefreshLocalizableAsync(
			float probeTimeoutSeconds, CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			// Two concurrent passes would each clear the other's record of what changed underneath
			// it, and the second round trip fetches what the first is already fetching.
			if (refreshInFlight)
				return null;

			refreshInFlight = true;
			lastLocalized.Clear();
			unsavedDuringRefresh.Clear();

			try
			{
				return await EnumerateSavedGuidsAsync(probeTimeoutSeconds, ctkn)
					? new HashSet<SerializableGuid>(lastLocalized)
					: null;
			}
			finally
			{
				refreshInFlight = false;
			}
		}

		private async Awaitable<bool> EnumerateSavedGuidsAsync(float probeTimeoutSeconds,
			CancellationToken ctkn)
		{
			if (metaDiscoveryAvailable)
				return await DiscoverAndProbeAsync(probeTimeoutSeconds, ctkn);

			// Off Quest, take AR Foundation's own enumeration where the provider implements it.
			// Nothing localizes: only Meta's runtime can be asked whether an anchor is findable
			// from here without materializing it.
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

				savedGuidSet.Clear();
				foreach (SerializableGuid id in ids)
					savedGuidSet.Add(id);

				ApplyRefreshCorrections();
				return true;
			}
			finally
			{
				if (ids.IsCreated)
					ids.Dispose();
			}
		}

		/// <summary>How long the runtime is given to finish enumerating what it has stored.</summary>
		private const float DiscoveryTimeoutSeconds = 10f;

		/// <summary>
		/// Discovery through Meta's runtime. Entities come back as runtime handles, but their uuids
		/// are the same guids AR Foundation addresses anchors by, so results feed straight into
		/// <see cref="Acquire(SerializableGuid, AnchorSource)"/>.
		/// </summary>
		private async Awaitable<bool> DiscoverAndProbeAsync(float probeTimeoutSeconds,
			CancellationToken ctkn)
		{
			List<SpatialEntity> discovered = new();

			if (!await MetaSpatialEntities.TryDiscoverAsync(
				    discovered, null, DiscoveryTimeoutSeconds, ctkn))
			{
				Debug.LogWarning("Failed to discover saved anchors.");
				ReleaseAll(discovered);
				return false;
			}

			if (ctkn.IsCancellationRequested || disposed)
			{
				ReleaseAll(discovered);
				return false;
			}

			// An unfiltered discovery surfaces the system's own scene entities (room mesh, planes)
			// alongside saved anchors. Storable is what makes an anchor persistable, so it is what
			// tells the two apart. Handles are kept only for what gets probed.
			List<SpatialEntity> anchors = new(discovered.Count);
			savedGuidSet.Clear();

			foreach (SpatialEntity entity in discovered)
			{
				if (!MetaSpatialEntities.IsStorable(entity.handle))
				{
					MetaSpatialEntities.Release(entity.handle);
					continue;
				}

				anchors.Add(entity);
				savedGuidSet.Add(new SerializableGuid(entity.uuid));
			}

			ApplyRefreshCorrections();
			await ProbeAsync(anchors, probeTimeoutSeconds, ctkn);
			return true;
		}

		/// <summary>
		/// Records which of these anchors the runtime can locate from where the headset is standing,
		/// into <see cref="lastLocalized"/>. Takes ownership of every handle: each is given back as
		/// its probe finishes, however long after this returns that is.
		/// </summary>
		private async Awaitable ProbeAsync(List<SpatialEntity> anchors, float timeoutSeconds,
			CancellationToken ctkn)
		{
			// Enable every locatable first so the runtime searches for them all concurrently, which
			// is why timeoutSeconds bounds the phase rather than each probe.
			int outstanding = 0;
			bool counting = true;

			foreach (SpatialEntity entity in anchors)
			{
				outstanding++;
				Probe(entity);
			}

			float probeDeadline = time + timeoutSeconds;

			try
			{
				while (outstanding > 0 && time < probeDeadline)
					await Awaitable.NextFrameAsync(ctkn);
			}
			catch (OperationCanceledException)
			{
			}

			// Anything still outstanding answers on its own time and still cleans up after itself;
			// it just no longer counts towards this result, and must not land in the next pass's.
			counting = false;

			if (ctkn.IsCancellationRequested || disposed)
				lastLocalized.Clear();

			async void Probe(SpatialEntity entity)
			{
				try
				{
					bool enabled = await MetaSpatialEntities.TrySetLocatableAsync(
						entity.handle, true, timeoutSeconds, ctkn);

					if (counting && enabled && MetaSpatialEntities.IsPositionTracked(entity.handle))
					{
						SerializableGuid guid = new(entity.uuid);
						lastLocalized.Add(guid);

						// The runtime just found this anchor, so whatever made its last local load
						// fail has passed. Waiting out the rest of the backoff would hold up the
						// load of a map this probe is about to recommend.
						localRetryAt.Remove(guid);
					}
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}
				finally
				{
					outstanding--;

					// A discovered entity holds a runtime handle and, while locatable, keeps the
					// runtime tracking it — and the same uuid may be loaded through AR Foundation
					// afterward. Giving the handle back ends both. Never runs across a pending
					// enable, which the runtime does not define an outcome for.
					MetaSpatialEntities.Release(entity.handle);
				}
			}
		}

		private static void ReleaseAll(List<SpatialEntity> entities)
		{
			foreach (SpatialEntity entity in entities)
				MetaSpatialEntities.Release(entity.handle);
		}

		/// <summary>
		/// Corrects a listing with what this process learned first-hand while it was being fetched.
		/// Subtracting comes first so that an anchor which missed a load and was then saved during
		/// the same fetch ends up saved.
		/// </summary>
		private void ApplyRefreshCorrections()
		{
			savedGuidSet.ExceptWith(unsavedDuringRefresh);
			savedGuidSet.UnionWith(provenSavedGuids);
		}

		// ------- internals -----------------------------------------

		internal bool isDisposed => disposed;
		internal float time => Time.unscaledTime;

		/// <summary>
		/// Brings an anchor in from whichever of <paramref name="source"/>'s origins works, local
		/// storage first — see <see cref="ShouldTryLocal"/>. Local loads hand back a materialized
		/// anchor; shared downloads only report that the request landed, and the anchor shows up
		/// later through trackablesChanged.
		/// </summary>
		internal async Awaitable<AnchorLoadResult> TryLoadAsync(SerializableGuid guid,
			AnchorSource source)
		{
			string localFailure = null;

			if (ShouldTryLocal(guid, source))
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

				// Both origins were tried, so report both: either alone reads as the whole story.
				return AnchorLoadResult.Failure($"{localFailure}; {shared.reason}");
			}

			if (localFailure != null)
				return AnchorLoadResult.Failure(localFailure);

			// Nothing was attempted: the lease permits local only, and this runtime has no storage.
			LogNoSavedAnchorsOnce();
			return AnchorLoadResult.Failure("this runtime cannot load saved anchors");
		}

		/// <summary>Whether this attempt should ask local storage before the shared group.</summary>
		private bool ShouldTryLocal(SerializableGuid guid, AnchorSource source)
		{
			if (!canLoadSavedAnchors)
				return false;

			// A copy this device already holds beats the group round trip whatever the lease
			// permits: it materializes immediately and needs no network.
			if (!IsSaved(guid) && !source.HasFlag(AnchorSource.Local))
				return false;

			// Nothing else to fall back on, so keep asking however often it has failed.
			if (!source.HasFlag(AnchorSource.Shared))
				return true;

			// A saved anchor belonging to another space fails forever here, so a download gets its
			// turn — and local is asked again once the wait lapses, in case all that was missing
			// was the headset localizing the space it was saved in.
			return !localRetryAt.TryGetValue(guid, out float retryAt) || time >= retryAt;
		}

		private async Awaitable<AnchorLoadResult> TryLoadSavedAnchorAsync(SerializableGuid guid)
		{
			Result<ARAnchor> result = await anchorManager.TryLoadAnchorAsync(guid);

			if (!result.status.IsSuccess() || result.value == null)
			{
				// Defers the next local load without touching what the registry believes is saved:
				// an anchor saved elsewhere fails here every time and is still in storage.
				localRetryAt[guid] = time + LocalRecheckSeconds;
				return AnchorLoadResult.Failure($"local storage: {result.status}");
			}

			// This materialized an ARAnchor synchronously. If nothing wants it anymore there is no
			// reconciliation left to sweep it up, so do it here.
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
			return AnchorLoadResult.Materialized(result.value);
		}

		private async Awaitable<AnchorLoadResult> TryLoadSharedAnchorAsync(SerializableGuid guid)
		{
			MetaOpenXRAnchorSubsystem meta = TryGetSharingSubsystem();
			if (meta == null)
				return AnchorLoadResult.Failure("this runtime has no shared anchors");

			List<XRAnchor> downloaded = new(1);

			meta.sharedAnchorsGroupId = guid;
			XRResultStatus result =
				await anchorManager.TryLoadAllSharedAnchorsAsync(downloaded, null);

			if (result.IsError())
				return AnchorLoadResult.Failure($"shared group: {result}");

			if (downloaded.Count == 0)
				return AnchorLoadResult.Failure("shared group was empty");

			// A group holds the one anchor its id names. Waiting on an anchor that is not coming
			// would spend the whole materialize timeout learning nothing.
			foreach (XRAnchor candidate in downloaded)
				if (candidate.trackableId == (TrackableId)guid)
					return AnchorLoadResult.Downloading;

			return AnchorLoadResult.Failure("shared group held a different anchor");
		}

		/// <summary>Whether the runtime still owes this registry a removal for this guid.</summary>
		internal bool IsRemovalInFlight(SerializableGuid guid)
		{
			if (!removalsInFlight.TryGetValue(guid, out float deadline))
				return false;

			if (time < deadline)
				return true;

			removalsInFlight.Remove(guid);
			return false;
		}

		internal void RemoveAnchor(ARAnchor anchor)
		{
			if (anchor == null)
				return;

			// Read before removing: a pending anchor is destroyed inside the call below.
			TrackableId id = anchor.trackableId;
			bool wasPending = anchor.pending;
			bool destroyOnRemoval = anchor.destroyOnRemoval;

			// A disabled manager throws rather than degrading, and this runs during teardown.
			bool removalRequested = anchorManager != null && anchorManager.enabled &&
				anchorManager.TryRemoveAnchor(anchor);

			// An anchor the manager had already reported is announced removed some frames later; one
			// it had not is dropped synchronously above and never reported at all. Past teardown
			// there is no handle left to hold the guid for, and nothing to clear the entry.
			if (removalRequested && !wasPending && !disposed)
				removalsInFlight[id] = time + RemovalTimeoutSeconds;

			// ARTrackableManager destroys a trackable's GameObject itself, so only clean up after it
			// where it won't — destroying the object out from under it hands the removal event a
			// dead ARAnchor.
			if (!(removalRequested && destroyOnRemoval) && anchor != null && anchor.gameObject != null)
				Object.Destroy(anchor.gameObject);
		}

		private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> eventData)
		{
			// Either report settles what the runtime owed for a guid: a removal is the confirmation
			// asked for, and an add means the runtime re-established the anchor regardless.
			foreach (ARAnchor anchor in eventData.added)
			{
				removalsInFlight.Remove(anchor.trackableId);

				if (handles.TryGetValue(anchor.trackableId, out AnchorHandle handle))
					handle.OnAnchorAdded(anchor);
			}

			foreach ((SerializableGuid guid, ARAnchor _) in eventData.removed)
			{
				removalsInFlight.Remove(guid);

				if (handles.TryGetValue(guid, out AnchorHandle handle))
					handle.OnAnchorRemoved();
			}
		}

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
				// throwing must not stop every other anchor from reconciling again.
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

		/// <summary>
		/// Stops observing trackables, gives back every anchor held, and leaves the handles
		/// terminal. Idempotent and one-way: <see cref="Acquire(SerializableGuid, AnchorSource)"/>
		/// throws from here on, so no guid can gain a second handle.
		/// </summary>
		private void TearDown()
		{
			if (disposed)
				return;

			disposed = true;

			if (anchorManager != null)
				anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);

			// Nothing reconciles these handles again, so an anchor left loaded is one no lease can
			// ever release and only this registry knew how to find. Contained per handle: giving one
			// back can fail, and the rest still have to be given back and left terminal.
			foreach (AnchorHandle handle in handles.Values)
			{
				try
				{
					// Abandon keeps an anchor under an uncancellable operation aside and gives it
					// back when that operation ends.
					if (!handle.hasOperationInFlight)
						RemoveAnchor(handle.anchor);
				}
				catch (Exception e)
				{
					Debug.LogException(e);
				}

				handle.Abandon();
			}

			handles.Clear();
			removalsInFlight.Clear();
		}
	}

	/// <summary>An anchor a mint produced, offered to whoever asked for it.</summary>
	internal readonly struct MintedAnchor
	{
		public MintedAnchor(AnchorLease lease, Guid guid, bool saved)
		{
			this.lease = lease;
			this.guid = guid;
			this.saved = saved;
		}

		public readonly AnchorLease lease;
		public readonly Guid guid;

		/// <summary>
		/// Whether it reached this device's persistent storage. False where the runtime has none,
		/// which leaves the anchor a valid reference for this session and no later one.
		/// </summary>
		public readonly bool saved;
	}

	/// <summary>
	/// The one way a colocation provider turns a pose into a reference it owns.
	/// </summary>
	internal static class AnchorMinting
	{
		/// <summary>
		/// Mints an anchor at <paramref name="pose"/>, saves it where the runtime can, and offers
		/// it to <paramref name="commit"/>, which reports whether it took it. The provider's own
		/// checks belong in <paramref name="commit"/>: it runs after the awaits, by which point
		/// the state the mint was started for may be gone.
		///
		/// A save <paramref name="commit"/> refuses is erased again, so a mint nothing kept leaves
		/// nothing behind on the device.
		///
		/// Exceptions propagate, cancellation included: reentrancy guards and logging policy stay
		/// with the caller.
		/// </summary>
		/// <param name="commitTakesLease">Whether a successful <paramref name="commit"/> keeps the
		/// lease. Providers that store it pass true; those that register a constraint and let their
		/// own reconciliation acquire a lease from it pass false.</param>
		public static async Awaitable<bool> TryMintAsync(AnchorRegistry registry, Pose pose,
			Func<MintedAnchor, bool> commit, bool commitTakesLease, CancellationToken ctkn)
		{
			AnchorLease minted = null;
			Guid guid = Guid.Empty;
			bool saved = false;
			bool committed = false;

			try
			{
				minted = await registry.TryMintAsync(pose, ctkn);
				if (minted == null)
					return false;

				guid = minted.Handle.guid.guid;

				// Where the runtime cannot save, the anchor still aligns this session, which is
				// what makes colocation testable against a simulator.
				if (registry.canSaveAnchors)
				{
					saved = await registry.TrySaveAsync(minted, ctkn);
					if (!saved)
						return false;
				}

				ctkn.ThrowIfCancellationRequested();

				committed = commit(new MintedAnchor(minted, guid, saved));
				return committed;
			}
			finally
			{
				if (!committed || !commitTakesLease)
					minted?.Dispose();

				if (saved && !committed && registry != null && registry.IsAvailable)
				{
					try
					{
						await registry.TryEraseSavedAsync(
							new SerializableGuid(guid), CancellationToken.None);
					}
					catch (ObjectDisposedException)
					{
					}
				}
			}
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
		public static AnchorLoadResult Downloading => new(true, null, null);

		public static AnchorLoadResult Materialized(ARAnchor anchor) => new(true, anchor, null);

		/// <summary>
		/// A failure that carries why. An anchor that is simply not here is an ordinary outcome and
		/// stays quiet per attempt; the reason surfaces once a handle has failed repeatedly.
		/// </summary>
		public static AnchorLoadResult Failure(string reason) => new(false, null, reason);

		private AnchorLoadResult(bool succeeded, ARAnchor anchor, string reason)
		{
			this.succeeded = succeeded;
			this.anchor = anchor;
			this.reason = reason;
		}

		public bool succeeded { get; }
		public ARAnchor anchor { get; }
		public string reason { get; }

		/// <summary>
		/// The request landed but produced no anchor, which only a shared download does. Whether the
		/// anchor is worth saving locally turns on this.
		/// </summary>
		public bool downloading => succeeded && anchor == null;
	}

	/// <summary>
	/// A reversible claim that an <see cref="AnchorHandle"/> should remain loaded. Releasing the
	/// final lease requests unloading; it does not dispose an in-flight handle.
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
		/// holder that lost it. Quiet once the registry is gone, which has already given every
		/// anchor back. Reads nothing but plain fields: this runs off the main thread.
		/// </summary>
		~AnchorLease()
		{
			if (!disposed && !Handle.owner.isDisposed)
				Debug.LogError($"An anchor lease on {Handle.guid} was collected without being " +
					"disposed. Its anchor can no longer be released.");
		}
#endif

		public AnchorHandle Handle { get; }

		/// <summary>Where this lease permits its handle to load the anchor from.</summary>
		internal AnchorSource Source { get; }

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
	/// The union of its live leases' <see cref="AnchorSource"/>s decides where a load may pull from,
	/// so one holder asking for a locally saved anchor and another asking for a shared download
	/// share a single handle without fighting over it.
	///
	/// A lease is a standing ask, so loading is endless rather than budgeted: an anchor that will
	/// not load right now is retried under a backoff for as long as any lease is held.
	///
	/// One handle serves a guid for the whole life of its registry, leased or not. Holders read
	/// <see cref="state"/> rather than being notified — the handle never calls out, which is what
	/// keeps reconciliation free of re-entrancy.
	/// </summary>
	public sealed class AnchorHandle
	{
		public enum State
		{
			Unloaded,
			Loading,
			Materializing,
			Active,

			/// <summary>
			/// Handed back, and the runtime has not confirmed it gone. The guid is still taken until
			/// it does, so the handle cannot load one either.
			/// </summary>
			Removing,

			/// <summary>
			/// Terminal: the handle has outlived its registry. Callers waiting on
			/// <see cref="Active"/> must treat this as the other way out, and retrying is not one.
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
		/// Generous next to the handful of frames it should take, so a download that never
		/// materializes spends its retry budget instead of waiting forever.
		/// </summary>
		private const float MaterializeTimeoutSeconds = 10f;

		/// <summary>
		/// How long a handle waits for a load request to answer at all. AR Foundation's anchor calls
		/// carry no timeout and the Meta provider ignores cancellation, so a request that never
		/// answers would hold the handle in <see cref="State.Loading"/> for the session. A duplicate
		/// request is worth more than a guid that can never load again.
		/// </summary>
		private const float LoadTimeoutSeconds = 30f;

		private readonly AnchorRegistry registry;

		private int localLeaseCount;
		private int sharedLeaseCount;
		private int operationCount;
		private bool loadInFlight;
		private bool materializedDuringLoad;

		/// <summary>
		/// Which load attempt the handle is waiting on. Bumping it disowns whatever is still
		/// outstanding, so a request that answers after its wait expired settles nothing.
		/// </summary>
		private int loadGeneration;

		private int failedLoadCount;
		private string lastFailureReason;
		private bool warnedAboutFailures;
		private bool saveWhenActive;
		private float retryAt;
		private float materializeDeadline;
		private float loadDeadline;

		/// <summary>
		/// An anchor <see cref="Abandon"/> could not give back because an operation was still
		/// running against it. Held only between teardown and that operation ending.
		/// </summary>
		private ARAnchor pinnedThroughTeardown;

		internal AnchorHandle(AnchorRegistry registry, SerializableGuid guid)
		{
			this.registry = registry;
			this.guid = guid;
			state = State.Unloaded;
		}

		public SerializableGuid guid { get; }
		public State state { get; private set; }
		public ARAnchor anchor { get; private set; }

		internal AnchorRegistry owner => registry;

		private bool desiredLoaded => localLeaseCount > 0 || sharedLeaseCount > 0;

		/// <summary>Whether an observed anchor is the one this handle is waiting for.</summary>
		private bool Matches(ARAnchor observedAnchor) =>
			observedAnchor.trackableId == (TrackableId)guid;

		/// <summary>Everywhere the current leases collectively allow this anchor to load from.</summary>
		private AnchorSource source =>
			(localLeaseCount > 0 ? AnchorSource.Local : AnchorSource.None) |
			(sharedLeaseCount > 0 ? AnchorSource.Shared : AnchorSource.None);

		/// <summary>
		/// Nothing left to reconcile. Anything that gives the handle work to do — a new lease, an
		/// observed anchor — makes this false in the same call, so the sweep picks the handle up on
		/// the next frame and can pass over it until then.
		/// </summary>
		internal bool isIdle =>
			!desiredLoaded &&
			!loadInFlight &&
			(state == State.Unloaded || state == State.Failed) &&
			anchor == null;

		/// <summary>
		/// Whether an operation the runtime cannot be told to abandon is running against this
		/// handle's anchor. The anchor has to outlive it, so unloading waits.
		/// </summary>
		internal bool hasOperationInFlight => operationCount > 0;

		internal void BeginOperation() => operationCount++;

		internal void EndOperation()
		{
			if (operationCount == 0)
			{
				Debug.LogError($"Anchor handle {guid} ended more operations than it began.");
				return;
			}

			operationCount--;

			// Nothing reconciles an abandoned handle, so an anchor the registry left pinned for this
			// operation has to be given back from here.
			if (operationCount == 0 && pinnedThroughTeardown != null)
			{
				ARAnchor pinned = pinnedThroughTeardown;
				pinnedThroughTeardown = null;
				registry.RemoveAnchor(pinned);
			}
		}

		/// <summary>
		/// Waits until this handle holds an anchor. A lease is a standing ask and the handle retries
		/// for as long as one is held, so the ways out are the anchor arriving, cancellation, and
		/// <see cref="State.Failed"/> — terminal, and reported as false.
		/// </summary>
		public async Awaitable<bool> WaitForAnchorAsync(CancellationToken ctkn = default)
		{
			while (anchor == null)
			{
				if (state == State.Failed)
					return false;

				await Awaitable.NextFrameAsync(ctkn);
			}

			return true;
		}

		internal void Retain(ARAnchor observedAnchor, AnchorSource leaseSource)
		{
			AnchorSource before = source;

			if (leaseSource.HasFlag(AnchorSource.Local))
				localLeaseCount++;

			if (leaseSource.HasFlag(AnchorSource.Shared))
				sharedLeaseCount++;

			// Only a lease that opens an origin nothing has tried yet clears the streak. Letting one
			// that adds nothing clear it lets two holders polling the same guid erase the backoff
			// between them.
			if ((source & ~before) != AnchorSource.None)
				ClearLoadFailures();

			// A refused anchor belongs to the caller or the manager, so it is reported rather than
			// given back. The handle then has to load its own, without sitting out a backoff first.
			if (observedAnchor != null && !ObserveAnchor(observedAnchor))
				ClearLoadFailures();
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
		}

		internal void OnAnchorAdded(ARAnchor addedAnchor)
		{
			if (!Matches(addedAnchor))
				return;

			ObserveAnchor(addedAnchor);
		}

		internal void OnAnchorRemoved()
		{
			// A removal for a guid this handle isn't holding is the confirmation of one of its own
			// past unloads: it frees the guid, but must not knock a freshly re-leased handle out of
			// Loading or Materializing.
			if (anchor != null)
			{
				anchor = null;
				state = State.Unloaded;
			}
		}

		/// <summary>
		/// The registry has given this handle's anchor back and will not reconcile it again.
		/// Terminal, so callers waiting on an anchor stop waiting; leases stay counted, so releasing
		/// one afterwards is still balanced. An anchor still under an uncancellable operation is
		/// kept aside for <see cref="EndOperation"/> to release.
		/// </summary>
		internal void Abandon()
		{
			if (hasOperationInFlight)
				pinnedThroughTeardown = anchor;

			anchor = null;
			state = State.Failed;
		}

		/// <summary>
		/// Brings the anchor this handle holds in line with what its leases want. The registry's
		/// per-frame sweep is the only caller: everything else just leaves the handle in a state
		/// <see cref="isIdle"/> rejects.
		/// </summary>
		internal void Reconcile()
		{
			if (registry.isDisposed)
				return;

			// A request that never answers cannot be called off, so it is disowned instead and left
			// to arrive whenever it likes.
			if (loadInFlight && registry.time >= loadDeadline)
			{
				loadInFlight = false;
				loadGeneration++;

				// Unless the anchor turned up on its own while the request hung, in which case the
				// request had nothing left to answer and this is no failure.
				if (state == State.Loading)
				{
					state = State.Unloaded;
					ScheduleRetry();
				}
			}

			// A requested anchor that never surfaces would otherwise hold the handle in
			// Materializing forever, unable to retry and never idle.
			if (state == State.Materializing && registry.time >= materializeDeadline)
			{
				state = State.Unloaded;
				ScheduleRetry();
			}

			// A removal the runtime has not confirmed still owns the guid: a load would be handed
			// that same anchor only for the pending removal to destroy it. Nothing to do but wait.
			if (registry.IsRemovalInFlight(guid))
				return;

			if (state == State.Removing)
				state = State.Unloaded;

			if (desiredLoaded)
			{
				if (anchor != null)
				{
					state = State.Active;
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
				// A save or a share cannot be called off, so the anchor it runs against outlives it
				// however briefly nothing wants it.
				if (!hasOperationInFlight)
					RemoveAnchor();

				return;
			}

			if (loadInFlight || state == State.Materializing)
				return;

			// Failed is terminal, so an abandoned handle does not quietly look loadable again.
			if (state != State.Failed)
				state = State.Unloaded;
		}

		private void StartLoad()
		{
			if (loadInFlight)
				return;

			loadInFlight = true;
			materializedDuringLoad = false;
			loadDeadline = registry.time + LoadTimeoutSeconds;
			state = State.Loading;
			RunLoadAsync(source, ++loadGeneration);
		}

		private async void RunLoadAsync(AnchorSource loadSource, int generation)
		{
			AnchorLoadResult result = AnchorLoadResult.Failed;

			try
			{
				result = await registry.TryLoadAsync(guid, loadSource);
			}
			catch (OperationCanceledException)
			{
				// The provider cancelled everything it had in flight as it shut down.
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			try
			{
				// The wait for this one expired and the handle moved on. Its state belongs to
				// whatever replaced this attempt, so all that is left is not to strand an anchor it
				// produced on the way.
				if (generation != loadGeneration)
				{
					DiscardOrAdopt(result.anchor);
					return;
				}

				loadInFlight = false;
				lastFailureReason = result.reason;
				SettleLoad(result, loadSource);
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
		}

		/// <summary>
		/// Takes an anchor produced by a disowned load if the handle still has none, and gives it
		/// back otherwise. Nothing else knows the anchor exists, so it cannot simply be dropped.
		/// </summary>
		private void DiscardOrAdopt(ARAnchor loaded)
		{
			// The anchor a disowned request eventually returned may be the one already adopted
			// through trackablesChanged while it hung.
			if (loaded == null || loaded == anchor)
				return;

			if (registry.isDisposed || anchor != null || !ObserveAnchor(loaded))
				registry.RemoveAnchor(loaded);
		}

		/// <summary>
		/// Takes the handle from <see cref="State.Loading"/> to whatever the attempt produced. Runs
		/// however the attempt ended, including after the wait for it timed out.
		/// </summary>
		private void SettleLoad(AnchorLoadResult result, AnchorSource loadSource)
		{
			// The registry tore down mid-load and already abandoned this handle. Anything that
			// arrived is orphaned, and the terminal state stands.
			if (registry.isDisposed)
			{
				registry.RemoveAnchor(result.anchor);
				return;
			}

			// Recorded before the anchor arrives: saving it is what spares the next session the
			// same round trip.
			if (result.downloading)
				saveWhenActive = true;

			// A local load hands the anchor straight back rather than routing it through
			// trackablesChanged, so adopt it here. A refused anchor came from a request this handle
			// made, so nothing but this will give it back.
			if (result.anchor != null && !ObserveAnchor(result.anchor))
				registry.RemoveAnchor(result.anchor);

			if (anchor != null)
			{
				ClearLoadFailures();
				state = State.Active;

				// A download can materialize its anchor before the request that asked for it
				// resolves, in which case this is the first point that knows it is worth saving.
				SaveIfDownloaded();
			}
			else if (result.downloading && !materializedDuringLoad)
			{
				ClearLoadFailures();
				state = State.Materializing;
				materializeDeadline = registry.time + MaterializeTimeoutSeconds;
			}
			else if ((source & ~loadSource) != AnchorSource.None)
			{
				// A lease acquired mid-load widened where this handle may look, so backing off now
				// would sit out the wait on an origin nothing has tried yet.
				state = State.Unloaded;
				retryAt = 0;
			}
			else
			{
				state = State.Unloaded;
				ScheduleRetry();
			}
		}

		/// <summary>
		/// Adopts an observed anchor as this handle's. False for an anchor addressed to some other
		/// handle: silently taking it would corrupt both.
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
			state = State.Active;
			SaveIfDownloaded();
			return true;
		}

		/// <summary>
		/// Hands a downloaded anchor to local storage now that it exists. Anchors that came from
		/// local storage are saved by definition and skip this.
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
			anchor = null;
			registry.RemoveAnchor(anchorToRemove);

			// Stays Removing until the runtime confirms: until then the guid is still taken, and
			// anything that loaded it would be handed the anchor on its way out.
			state = registry.IsRemovalInFlight(guid) ? State.Removing : State.Unloaded;
		}

		/// <summary>
		/// Backs off before the next attempt. A held lease is a standing ask, so there is no last
		/// attempt: an anchor whose owner has not shared it yet becomes loadable later.
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
	}
}
