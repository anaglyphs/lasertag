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
	/// </summary>
	[DefaultExecutionOrder(-300)]
	public sealed class AnchorRegistry : MonoBehaviour, IDisposable
	{
		public static AnchorRegistry Instance { get; private set; }

		private ARAnchorManager anchorManager;
		private MetaOpenXRAnchorSubsystem anchorSubsystem;
		private readonly Dictionary<SerializableGuid, AnchorHandle> handles = new();
		private readonly HashSet<SerializableGuid> savedGuidSet = new();
		private readonly CancellationTokenSource lifetimeCtknSrc = new();

		private readonly List<AnchorHandle> reconciliationSnapshot = new();

		private bool disposed;

		/// <summary>False where the configured anchor runtime is unavailable.</summary>
		public bool IsAvailable => anchorManager != null && anchorSubsystem != null && !disposed;

		private void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Debug.LogError("Only one AnchorRegistry may own AR anchor handles.", this);
				enabled = false;
				return;
			}

			Instance = this;

#if !UNITY_EDITOR
			anchorManager = FindFirstObjectByType<ARAnchorManager>();
			if (anchorManager == null)
			{
				Debug.LogError("AnchorRegistry requires an ARAnchorManager.", this);
				return;
			}

			anchorSubsystem = anchorManager.subsystem as MetaOpenXRAnchorSubsystem;
			if (anchorSubsystem == null)
			{
				Debug.LogError("AnchorRegistry requires the Meta OpenXR anchor subsystem.", this);
				return;
			}

			TryRefreshSavedGuidsAsync(lifetimeCtknSrc.Token);

			anchorManager.trackablesChanged.AddListener(OnTrackablesChanged);
			ReconciliationLoop(lifetimeCtknSrc.Token);
#endif
		}

		private void OnDestroy()
		{
			Dispose();

			if (Instance == this)
				Instance = null;
		}

		public AnchorLease Acquire(SerializableGuid guid, AnchorSource source)
		{
			ThrowIfDisposed();
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

			ThrowIfDisposed();
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
			ThrowIfDisposed();
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

		public Supported sharedAnchorsSupport =>
			anchorSubsystem != null ? anchorSubsystem.isSharedAnchorsSupported : Supported.Unknown;

		/// <summary>Shares one loaded anchor into the Meta group addressed by its guid.</summary>
		public async Awaitable<XRResultStatus> TryShareAsync(SerializableGuid guid,
			CancellationToken ctkn = default)
		{
			ThrowIfDisposed();
			ctkn.ThrowIfCancellationRequested();

			ARAnchor anchor =
				handles.TryGetValue(guid, out AnchorHandle handle) && handle.anchor != null
					? handle.anchor
					: anchorManager.GetAnchor(guid);

			if (anchor == null)
			{
				Debug.LogWarning($"Cannot share anchor {guid}: it is not loaded.");
				return new XRResultStatus(XRResultStatus.StatusCode.UnknownError);
			}

			anchorSubsystem.sharedAnchorsGroupId = guid;
			XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);
			ctkn.ThrowIfCancellationRequested();
			return result;
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

		public bool canSaveAnchors => anchorManager.descriptor?.supportsSaveAnchor ?? false;
		public bool canLoadSavedAnchors => anchorManager.descriptor?.supportsLoadAnchor ?? false;
		public bool canEraseSavedAnchors => anchorManager.descriptor?.supportsEraseAnchor ?? false;

		public bool canEnumerateSavedAnchors =>
			metaDiscoveryAvailable || (anchorManager.descriptor?.supportsGetSavedAnchorIds ?? false);

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

			ARAnchor anchorToSave =
				handles.TryGetValue(guid, out AnchorHandle handle) && handle.anchor != null
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
		/// Discovery is scoped to this app, not to the room you happen to be standing in: it
		/// returns everything Lasertag ever saved on this headset, including anchors belonging to
		/// somewhere else entirely. Which of them actually localize is the test for where you are.
		/// </summary>
		public async Awaitable<bool> TryRefreshSavedGuidsAsync(CancellationToken ctkn = default)
		{
			ThrowIfDisposed();

			if (metaDiscoveryAvailable)
				return await TryDiscoverSavedGuidsAsync(ctkn);

			// Anywhere that isn't a Quest, take AR Foundation's own enumeration if the provider
			// happens to implement it.
			if (!(anchorManager.descriptor?.supportsGetSavedAnchorIds ?? false))
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

			OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> result =
				await OVRAnchor.FetchAnchorsAsync(discovered, options);

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

		private void ReplaceSavedGuids(NativeArray<SerializableGuid> ids)
		{
			savedGuidSet.Clear();
			foreach (SerializableGuid id in ids)
				savedGuidSet.Add(id);
		}

		/// <summary>
		/// Which of these saved anchors localize in the physical space the headset is standing
		/// in right now — with NO AR Foundation trackables materialized. This is the cheap
		/// first phase of map discovery: probing through Meta's locatable API leaves the scene
		/// untouched, and only the chosen map's anchors ever get committed to real ARAnchors.
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
			OVRResult<List<OVRAnchor>, OVRAnchor.FetchResult> fetchResult =
				await OVRAnchor.FetchAnchorsAsync(fetched, new OVRAnchor.FetchOptions
				{
					Uuids = uuids
				});

			if (ctkn.IsCancellationRequested || disposed)
				return localized;

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
				bool enabled = await enable;

				if (enabled &&
				    locatable.TryGetSpatialAnchorPose(out OVRLocatable.TrackingSpacePose pose) &&
				    pose.IsPositionTracked)
					localized.Add(new SerializableGuid(anchor.Uuid));

				// Leave nothing running behind the probe: an enabled locatable keeps the
				// runtime tracking it, and the same UUID may be loaded through AR Foundation
				// afterwards. (Whether the two stacks interfere at all is still unverified on
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
			List<XRAnchor> downloaded = new(1);

			anchorSubsystem.sharedAnchorsGroupId = guid;
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

		internal void RemoveAnchor(ARAnchor anchor)
		{
			if (anchor == null)
				return;

			anchorManager.TryRemoveAnchor(anchor);
			if (anchor.gameObject != null)
				Object.Destroy(anchor.gameObject);
		}

		internal void TryEvict(AnchorHandle handle)
		{
			if (!handle.canEvict)
				return;

			if (handles.TryGetValue(handle.guid, out AnchorHandle registered) &&
			    ReferenceEquals(registered, handle))
				handles.Remove(handle.guid);
		}

		private void OnTrackablesChanged(ARTrackablesChangedEventArgs<ARAnchor> eventData)
		{
			foreach (ARAnchor anchor in eventData.added)
				if (handles.TryGetValue(anchor.trackableId, out AnchorHandle handle))
					handle.OnAnchorAdded(anchor);

			foreach ((SerializableGuid guid, ARAnchor _) in eventData.removed)
				if (handles.TryGetValue(guid, out AnchorHandle handle))
					handle.OnAnchorRemoved();
		}

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
			if (addedAnchor.trackableId != (TrackableId)guid)
				return;

			ObserveAnchor(addedAnchor);
			Reconcile();
		}

		internal void OnAnchorRemoved()
		{
			anchor = null;
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
			if (observedAnchor.trackableId != (TrackableId)guid)
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
