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
		/// <see cref="AnchorRegistry.TrySaveAsync(ARAnchor, CancellationToken)"/> — in this session
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
	///
	/// Where the running runtime has no shared anchors, the editor stands in for that transport
	/// here (see <see cref="simulatingSharedAnchors"/>) so a second peer can align without a
	/// headset. Everything above this class then behaves identically in the editor and on a
	/// device, and the fiction stays in one place — this one.
	/// </summary>
	[DefaultExecutionOrder(-300)]
	public sealed class AnchorRegistry : MonoBehaviour, IDisposable
	{
		public static AnchorRegistry Instance { get; private set; }

		[Tooltip("In the editor only, stand in for Meta's shared-anchor transport so a second " +
		         "peer can align without a headset. Never has any effect in a build.")]
		[SerializeField] private bool simulateSharedAnchorsInEditor = true;

		private ARAnchorManager anchorManager;
		private readonly Dictionary<SerializableGuid, AnchorHandle> handles = new();

		/// <summary>
		/// Handles indexed by the id of the local anchor standing in for them while shared
		/// anchors are simulated, so trackable events for a stand-in reach the handle that
		/// is addressed by the anchor's real guid.
		/// </summary>
		private readonly Dictionary<SerializableGuid, AnchorHandle> simulatedAnchorHandles = new();
		private readonly HashSet<SerializableGuid> savedGuidSet = new();
		private readonly CancellationTokenSource lifetimeCtknSrc = new();

		private readonly List<AnchorHandle> reconciliationSnapshot = new();

		private bool disposed;
		private bool loggedNoSharedAnchors;
		private bool loggedSimulatedSharing;

		/// <summary>
		/// Where a simulated download finds the pose to put an anchor at. The registry knows
		/// nothing about where anchors belong — that is the colocation layer's synchronized
		/// data — so it asks. Null, or a null answer, fails the download like a real one.
		/// Set by whichever provider owns the session's canon poses.
		/// </summary>
		public Func<SerializableGuid, Pose?> SimulatedSharedAnchorPose { get; set; }

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

			TryRefreshSavedGuidsAsync(lifetimeCtknSrc.Token);

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);
			ReconciliationLoop(lifetimeCtknSrc.Token);
		}

		private void OnDestroy()
		{
			Dispose();

			if (Instance == this)
				Instance = null;
		}

		public AnchorLease Acquire(SerializableGuid guid, AnchorSource source)
		{
			ThrowIfUnavailable();
			ThrowIfNoSource(source);

			if (!handles.TryGetValue(guid, out AnchorHandle handle))
			{
				handle = new AnchorHandle(this, guid);
				handles.Add(guid, handle);
			}

			handle.Retain(anchorManager.GetAnchor(guid), source);
			return new AnchorLease(handle, source);
		}

		public AnchorLease Acquire(ARAnchor anchor, AnchorSource source)
		{
			if (anchor == null)
				throw new ArgumentNullException(nameof(anchor));

			ThrowIfUnavailable();
			ThrowIfNoSource(source);

			SerializableGuid guid = anchor.trackableId;
			if (!handles.TryGetValue(guid, out AnchorHandle handle))
			{
				handle = new AnchorHandle(this, guid);
				handles.Add(guid, handle);
			}

			handle.Retain(anchor, source);
			return new AnchorLease(handle, source);
		}

		/// <summary>
		/// Creates a brand-new anchor at <paramref name="pose"/> and starts holding it. This does
		/// not load an anchor from local storage or a shared group, and it does not persist the new
		/// anchor; call <see cref="TrySaveAsync(ARAnchor, CancellationToken)"/> separately if it
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
		/// Whether Meta's runtime really implements shared anchors here. This is the only
		/// question a build ever asks; everything below it is editor scaffolding.
		/// </summary>
		private bool CheckMetaSharingSupport()
		{
			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
			return meta != null && meta.isSharedAnchorsSupported == Supported.Supported;
		}

		/// <summary>
		/// Whether this registry is standing in for Meta's shared-anchor transport rather than
		/// using it.
		///
		/// Editor only, and deliberately a runtime check rather than <c>#if UNITY_EDITOR</c>:
		/// the simulation then compiles and type-checks in every build and is simply never
		/// true in one. That distinction matters here more than anywhere else in this class —
		/// a simulated download hands back an anchor placed where a peer *said* it was, with
		/// nothing measured, so on a headset it would report two players as colocated while
		/// they stand metres apart. A runtime that can really share is always preferred.
		/// </summary>
		private bool simulatingSharedAnchors =>
			Application.isEditor && simulateSharedAnchorsInEditor &&
			IsAvailable && !CheckMetaSharingSupport();

		/// <summary>
		/// What the running runtime says about sharing. Meta's runtime answers for itself;
		/// anything else (XR Simulation, no runtime at all) cannot share — unless the editor
		/// is simulating the transport, in which case the answer is yes and this class is the
		/// one implementing it.
		/// </summary>
		public Supported sharedAnchorsSupport
		{
			get
			{
				if (simulatingSharedAnchors)
					return Supported.Supported;

				MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
				return meta != null ? meta.isSharedAnchorsSupported : Supported.Unsupported;
			}
		}

		/// <summary>Whether an anchor can be shared or downloaded at all right now.</summary>
		public bool canShareAnchors => sharedAnchorsSupport == Supported.Supported;

		/// <summary>Shares one loaded anchor into the Meta group addressed by its guid.</summary>
		public async Awaitable<XRResultStatus> TryShareAsync(SerializableGuid guid,
			CancellationToken ctkn = default)
		{
			ThrowIfDisposed();
			ctkn.ThrowIfCancellationRequested();

			if (simulatingSharedAnchors)
			{
				// Nothing to upload: a simulated download reads the anchor's pose from the
				// colocation layer's synchronized set, which every peer already has.
				LogSimulatingSharedAnchorsOnce();
				return new XRResultStatus(XRResultStatus.StatusCode.UnqualifiedSuccess);
			}

			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
			if (meta == null)
			{
				LogNoSharedAnchorsOnce();
				return new XRResultStatus(XRResultStatus.StatusCode.Unsupported);
			}

			ARAnchor anchor =
				handles.TryGetValue(guid, out AnchorHandle handle) && handle.anchor != null
					? handle.anchor
					: anchorManager.GetAnchor(guid);

			if (anchor == null)
			{
				Debug.LogWarning($"Cannot share anchor {guid}: it is not loaded.");
				return new XRResultStatus(XRResultStatus.StatusCode.UnknownError);
			}

			meta.sharedAnchorsGroupId = guid;
			XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);
			ctkn.ThrowIfCancellationRequested();
			return result;
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

		// ------- local persistence ---------------------------------

		/// <summary>
		/// Anchors this device is known to hold in persistent storage: whatever the registry saved
		/// or loaded this process, plus whatever the last
		/// <see cref="TryRefreshSavedGuidsAsync"/> discovered.
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
		/// Persists the currently loaded anchor with this guid to local storage, so a later session
		/// can <see cref="Acquire(SerializableGuid, AnchorSource)"/> it with
		/// <see cref="AnchorSource.Local"/>. Fails if the anchor isn't loaded right now — there is
		/// nothing for the runtime to save.
		/// </summary>
		public async Awaitable<bool> TrySaveAsync(SerializableGuid guid, CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			if (!IsAvailable)
				return false;

			handles.TryGetValue(guid, out AnchorHandle handle);

			// A simulated stand-in is a different anchor wearing this guid's name; saving it
			// would write a fiction into real storage under the wrong id. The simulation says
			// this device has the anchor, so report the save as done and keep callers on their
			// normal path — nothing outlives the session here anyway.
			if (handle != null && handle.isSimulated)
				return true;

			ARAnchor anchorToSave = handle != null && handle.anchor != null
				? handle.anchor
				: anchorManager.GetAnchor(guid);

			if (anchorToSave == null)
			{
				Debug.LogWarning($"Cannot save anchor {guid} locally: it is not loaded.");
				return false;
			}

			return await TrySaveAsync(anchorToSave, ctkn);
		}

		/// <summary>
		/// Persists an anchor to local storage. The anchor stays loaded; this only writes it to the
		/// device so it can be recovered later.
		/// </summary>
		public async Awaitable<bool> TrySaveAsync(ARAnchor anchor, CancellationToken ctkn = default)
		{
			if (anchor == null)
				throw new ArgumentNullException(nameof(anchor));

			ThrowIfDisposed();

			if (!canSaveAnchors)
			{
				Debug.LogWarning("This runtime cannot save anchors to local storage.");
				return false;
			}

			SerializableGuid guid = anchor.trackableId;
			Result<SerializableGuid> result = await anchorManager.TrySaveAnchorAsync(anchor, ctkn);

			if (!result.status.IsSuccess())
			{
				Debug.LogWarning($"Failed to save anchor {guid} locally: {result.status}");
				return false;
			}

			// The registry addresses saved anchors by trackable id, which Meta's runtime promises is
			// the same value it hands back here. If that ever stops holding, the anchor is on disk
			// but nothing in here can ask for it again — so say so loudly rather than report success.
			if (result.value != guid)
			{
				savedGuidSet.Add(result.value);
				Debug.LogError($"Anchor {guid} saved under a different guid ({result.value}); " +
					"it cannot be loaded back by trackable id.");
				return false;
			}

			savedGuidSet.Add(guid);
			return true;
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
			return true;
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
		/// </summary>
		private async Awaitable<(bool answered, T result)> WaitOrGiveUp<T>(
			OVRTask<T> task, CancellationToken ctkn)
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

			float deadline = time + OvrRequestTimeoutSeconds;
			while (!answered && time < deadline)
				await Awaitable.NextFrameAsync(ctkn);

			return (answered, value);
		}

		private void ReplaceSavedGuids(NativeArray<SerializableGuid> ids)
		{
			savedGuidSet.Clear();
			foreach (SerializableGuid id in ids)
				savedGuidSet.Add(id);
		}

		/// <summary>
		/// Which of these saved anchors localize in the physical space the headset is standing
		/// in right now. This is the cheap first phase of map discovery:
		/// probing through Meta's locatable API leaves the scene untouched,
		/// and only the chosen map's anchors ever get committed to real ARAnchors.
		///
		/// Anchors saved in one space occasionally localize in another; a non-empty result is
		/// a strong hint, not proof, of which room this is.
		/// </summary>
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

			foreach ((OVRAnchor anchor, OVRLocatable locatable, OVRTask<bool> enable) in probes)
			{
				(bool answeredEnable, bool enabled) = await WaitOrGiveUp(enable, ctkn);

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
			if (source.HasFlag(AnchorSource.Local) && canLoadSavedAnchors)
			{
				AnchorLoadResult local = await TryLoadSavedAnchorAsync(guid);
				if (local.succeeded)
					return local;
			}

			if (source.HasFlag(AnchorSource.Shared))
				return await TryLoadSharedAnchorAsync(guid);

			return AnchorLoadResult.Failed;
		}

		internal async Awaitable<AnchorLoadResult> TryLoadSavedAnchorAsync(SerializableGuid guid)
		{
			Result<ARAnchor> result = await anchorManager.TryLoadAnchorAsync(guid);

			if (!result.status.IsSuccess() || result.value == null)
			{
				Debug.LogWarning($"Failed to load saved anchor {guid}: {result.status}");
				return AnchorLoadResult.Failed;
			}

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

			savedGuidSet.Add(guid);
			return AnchorLoadResult.Materialized(result.value);
		}

		internal async Awaitable<AnchorLoadResult> TryLoadSharedAnchorAsync(SerializableGuid guid)
		{
			if (simulatingSharedAnchors)
				return await SimulateSharedAnchorDownloadAsync(guid);

			MetaOpenXRAnchorSubsystem meta = metaAnchorSubsystem;
			if (meta == null)
			{
				LogNoSharedAnchorsOnce();
				return AnchorLoadResult.Failed;
			}

			List<XRAnchor> downloaded = new(1);

			meta.sharedAnchorsGroupId = guid;
			XRResultStatus result =
				await anchorManager.TryLoadAllSharedAnchorsAsync(downloaded, null);

			if (result.IsError())
			{
				Debug.LogWarning($"Failed to load shared anchor {guid}: {result}");
				return AnchorLoadResult.Failed;
			}

			if (downloaded.Count == 0)
			{
				Debug.LogWarning($"Shared anchor group {guid} did not contain any anchors.");
				return AnchorLoadResult.Failed;
			}

			return AnchorLoadResult.Pending;
		}

		/// <summary>
		/// The editor's stand-in for downloading a shared anchor: mint a local anchor where the
		/// colocation layer says this one belongs, and let the handle hold it under the guid
		/// that was asked for. A minted anchor cannot be given someone else's trackable id, so
		/// the handle keeps its own guid as its address and remembers the stand-in's id.
		///
		/// Nothing physical is measured: the peer ends up agreeing with whoever published the
		/// pose. That is all two editors sharing no room can do, and precisely why
		/// <see cref="simulatingSharedAnchors"/> never opens in a build.
		/// </summary>
		private async Awaitable<AnchorLoadResult> SimulateSharedAnchorDownloadAsync(
			SerializableGuid guid)
		{
			Pose? pose = SimulatedSharedAnchorPose?.Invoke(guid);
			if (pose == null)
			{
				Debug.LogWarning($"Nothing knows where anchor {guid} belongs, so its download " +
					"cannot be simulated.");
				return AnchorLoadResult.Failed;
			}

			Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(pose.Value);

			if (!result.status.IsSuccess() || result.value == null)
			{
				if (result.value != null)
					RemoveAnchor(result.value);

				Debug.LogWarning($"Failed to mint a stand-in for shared anchor {guid}: " +
					$"{result.status}");
				return AnchorLoadResult.Failed;
			}

			// The handle is created before the load starts, so its absence means the lease was
			// released while the runtime was busy — nothing wants this anymore.
			if (disposed || !handles.TryGetValue(guid, out AnchorHandle handle))
			{
				RemoveAnchor(result.value);
				return AnchorLoadResult.Failed;
			}

			LogSimulatingSharedAnchorsOnce();
			handle.BindSimulatedAnchor(result.value.trackableId);
			simulatedAnchorHandles[result.value.trackableId] = handle;
			return AnchorLoadResult.Materialized(result.value);
		}

		private void LogSimulatingSharedAnchorsOnce()
		{
			if (loggedSimulatedSharing)
				return;

			loggedSimulatedSharing = true;
			Debug.LogWarning("Simulating Meta's shared anchors for editor testing. Downloaded " +
				"anchors are minted locally at the poses the session publishes, so alignment " +
				"between peers is assumed rather than measured. Never happens in a build.", this);
		}

		/// <summary>Forgets a handle's simulated stand-in, if it has one.</summary>
		internal void UnbindSimulatedAnchor(AnchorHandle handle)
		{
			if (!handle.isSimulated)
				return;

			simulatedAnchorHandles.Remove(handle.simulatedAnchorId);
			handle.ClearSimulatedAnchor();
		}

		internal void RemoveAnchor(ARAnchor anchor)
		{
			if (anchor == null)
				return;

			if (anchorManager != null)
				anchorManager.TryRemoveAnchor(anchor);

			if (anchor.gameObject != null)
				Object.Destroy(anchor.gameObject);
		}

		internal void TryEvict(AnchorHandle handle)
		{
			if (!handle.canEvict)
				return;

			UnbindSimulatedAnchor(handle);

			if (handles.TryGetValue(handle.guid, out AnchorHandle registered) &&
			    ReferenceEquals(registered, handle))
				handles.Remove(handle.guid);
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

		/// <summary>
		/// The handle addressed by this id, or the one a simulated stand-in with this id is
		/// standing in for.
		/// </summary>
		private bool TryGetHandleFor(SerializableGuid id, out AnchorHandle handle) =>
			handles.TryGetValue(id, out handle) ||
			simulatedAnchorHandles.TryGetValue(id, out handle);

		private async void ReconciliationLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					reconciliationSnapshot.Clear();
					reconciliationSnapshot.AddRange(handles.Values);

					foreach (AnchorHandle handle in reconciliationSnapshot)
						handle.Reconcile();
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
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

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			lifetimeCtknSrc.Cancel();
			if (anchorManager != null)
				anchorManager.trackablesChanged.RemoveListener(OnTrackablesChanged);
			simulatedAnchorHandles.Clear();
			SimulatedSharedAnchorPose = null;
			lifetimeCtknSrc.Dispose();
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
		public static AnchorLoadResult Pending => new(true, null);
		public static AnchorLoadResult Materialized(ARAnchor anchor) => new(true, anchor);

		private AnchorLoadResult(bool succeeded, ARAnchor anchor)
		{
			this.succeeded = succeeded;
			this.anchor = anchor;
		}

		public bool succeeded { get; }
		public ARAnchor anchor { get; }
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

		public AnchorHandle Handle { get; }

		/// <summary>Where this lease permits its handle to load the anchor from.</summary>
		public AnchorSource Source { get; }

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
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
	/// </summary>
	public sealed class AnchorHandle
	{
		public enum State
		{
			Unloaded,
			Loading,
			Materializing,
			Active,
			Removing
		}

		private const float RetryStepSeconds = 3f;
		private const float MaximumRetrySeconds = 30f;

		private readonly AnchorRegistry registry;

		private int localLeaseCount;
		private int sharedLeaseCount;
		private bool loadInFlight;
		private bool materializedDuringLoad;
		private int failedLoadCount;
		private float retryAt;

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
		/// Whether the anchor this handle holds is a locally minted stand-in for the one its
		/// <see cref="guid"/> names, which the editor's shared-anchor simulation produces.
		/// The handle keeps its own guid as its address either way.
		/// </summary>
		public bool isSimulated { get; private set; }

		/// <summary>The stand-in's own trackable id. Meaningless unless <see cref="isSimulated"/>.</summary>
		internal SerializableGuid simulatedAnchorId { get; private set; }

		internal void BindSimulatedAnchor(SerializableGuid standInId)
		{
			isSimulated = true;
			simulatedAnchorId = standInId;
		}

		internal void ClearSimulatedAnchor()
		{
			isSimulated = false;
			simulatedAnchorId = default;
		}

		/// <summary>Whether an observed anchor is the one this handle is waiting for.</summary>
		private bool Matches(ARAnchor observedAnchor) =>
			observedAnchor.trackableId == (TrackableId)guid ||
			(isSimulated && observedAnchor.trackableId == (TrackableId)simulatedAnchorId);

		/// <summary>Everywhere the current leases collectively allow this anchor to load from.</summary>
		public AnchorSource source =>
			(localLeaseCount > 0 ? AnchorSource.Local : AnchorSource.None) |
			(sharedLeaseCount > 0 ? AnchorSource.Shared : AnchorSource.None);

		internal bool canEvict =>
			!desiredLoaded &&
			!loadInFlight &&
			state == State.Unloaded &&
			anchor == null;

		internal void Retain(ARAnchor observedAnchor, AnchorSource leaseSource)
		{
			if (leaseSource.HasFlag(AnchorSource.Local))
				localLeaseCount++;

			if (leaseSource.HasFlag(AnchorSource.Shared))
				sharedLeaseCount++;

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
			anchor = null;
			registry.UnbindSimulatedAnchor(this);
			SetState(State.Unloaded);
			Reconcile();
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
			if (desiredLoaded)
			{
				if (anchor != null)
				{
					SetState(State.Active);
					return;
				}

				if (loadInFlight || state == State.Materializing)
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

			SetState(State.Unloaded);
			registry.TryEvict(this);
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

				// A local load hands the anchor straight back rather than routing it through
				// trackablesChanged, so adopt it here.
				if (result.anchor != null)
					ObserveAnchor(result.anchor);

				if (anchor != null)
				{
					failedLoadCount = 0;
					retryAt = 0;
					SetState(State.Active);
				}
				else if (result.succeeded && !materializedDuringLoad)
				{
					failedLoadCount = 0;
					retryAt = 0;
					SetState(State.Materializing);
				}
				else
				{
					SetState(State.Unloaded);
					ScheduleRetry();
				}

				Reconcile();
			}
		}

		private void ObserveAnchor(ARAnchor observedAnchor)
		{
			if (!Matches(observedAnchor))
				throw new ArgumentException("The observed anchor does not match this handle.", nameof(observedAnchor));

			if (loadInFlight)
				materializedDuringLoad = true;

			anchor = observedAnchor;
			failedLoadCount = 0;
			retryAt = 0;
			SetState(State.Active);
		}

		private void RemoveAnchor()
		{
			ARAnchor anchorToRemove = anchor;

			SetState(State.Removing);
			anchor = null;
			registry.UnbindSimulatedAnchor(this);
			registry.RemoveAnchor(anchorToRemove);
			SetState(State.Unloaded);

			Reconcile();
		}

		private void ScheduleRetry()
		{
			if (!desiredLoaded)
			{
				retryAt = 0;
				return;
			}

			failedLoadCount++;
			retryAt = registry.time +
				Mathf.Min(RetryStepSeconds * failedLoadCount, MaximumRetrySeconds);
		}

		private void SetState(State next)
		{
			if (state == next)
				return;

			state = next;
			StateChanged.Invoke(this);
		}
	}
}
