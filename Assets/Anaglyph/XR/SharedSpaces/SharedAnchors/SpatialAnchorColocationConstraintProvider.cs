using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	public readonly struct AnchorConstraintData
	{
		public AnchorConstraintData(Guid guid, Pose canonPose, int bindingId = -1)
		{
			this.guid = guid;
			this.canonPose = canonPose;
			this.bindingId = bindingId;
		}

		public readonly Guid guid;
		public readonly Pose canonPose;
		/// <summary>Optional embedding-layer association; the provider does not interpret it.</summary>
		public readonly int bindingId;
	}

	public struct AnchorConstraintState
	{
		public Pose canonPose;
		public int bindingId;
	}

	/// <summary>
	/// A complete shared-anchor colocation strategy. It owns the synchronized guid/canon-pose
	/// set, loads and persists those anchors through <see cref="AnchorRegistry"/>, shares them
	/// from the designated headset, and mints additional constraints as that headset explores.
	/// A game-specific map system may import and export <see cref="Constraints"/>, but is not
	/// required for the provider to align a session.
	/// </summary>
	[DefaultExecutionOrder(-200)]
	public partial class SpatialAnchorColocationConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public static SpatialAnchorColocationConstraintProvider Instance { get; private set; }

		private sealed class HeldAnchor
		{
			public AnchorLease lease;
			public Pose canon;
			public AnchorSource source;
			/// <summary>Cancels the entry's in-flight <see cref="PersistWhenActive"/> loop.</summary>
			public CancellationTokenSource persistCtknSrc;
		}

		private readonly SyncDictionary<Guid, AnchorConstraintState> constraints =
			new("colocation.anchors.canon");
		public IReadOnlyDictionary<Guid, AnchorConstraintState> Constraints => constraints;
		public event Action ConstraintsChanged = delegate { };
		public event Action<Guid> AnchorPersisted = delegate { };

		[Tooltip("Distance from every existing anchor required before minting another")]
		[SerializeField] private float newAnchorDistance = 6f;

		[SerializeField] private LayerMask placementRaycastLayerMask = Physics.DefaultRaycastLayers;

		private Colocator colocator;

		/// <summary>
		/// The embedding game may suppress automatic minting, for example when a tag-enabled map
		/// requires every anchor to have a parent tag.
		/// </summary>
		public bool RoamingMintEnabled { get; set; } = true;

		/// <summary>
		/// Optional additional safety gate supplied by the embedding game. With no gate, an empty
		/// provider defines the current frame and an established provider requires localization.
		/// </summary>
		public Func<bool> MintingGate { get; set; }

		private readonly Dictionary<Guid, HeldAnchor> held = new();
		private readonly List<Guid> heldRemovalScratch = new();
		private readonly HashSet<Guid> sharesInFlight = new();

		private AnchorRegistry registry;
		private CancellationTokenSource lifetimeCtknSrc;
		private CancellationTokenSource runCtknSrc;
		private int stateGeneration;

		public bool IsAvailable => registry != null && registry.IsAvailable;
		public bool IsRunning { get; private set; }
		public bool IsMinting { get; private set; }

		private void Awake()
		{
			Instance = this;
			colocator = GetComponent<Colocator>() ?? FindFirstObjectByType<Colocator>();
			registry = AnchorRegistry.Instance ?? FindFirstObjectByType<AnchorRegistry>();
			if (registry == null)
				Debug.LogError(
					"SpatialAnchorConstraintProvider requires an AnchorRegistry in the scene.", this);

			lifetimeCtknSrc = new CancellationTokenSource();

			constraints.ResetOnDeactivate = false;
			constraints.ValidateSet = (_, _, _) => false;
			constraints.ValidateRemove = (_, _) => false;
			constraints.ValidateClear = _ => false;
			constraints.Register();
			RegisterSession();
			constraints.Changed += OnConstraintsChanged;
			constraints.Synced += OnConstraintsSynced;

			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		private void OnDestroy()
		{
			StopProviding();
			lifetimeCtknSrc?.Cancel();
			UnregisterSession();

			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.Activated -= OnBusActivated;

			constraints.Synced -= OnConstraintsSynced;
			constraints.Changed -= OnConstraintsChanged;
			constraints.Unregister();

			if (Instance == this)
				Instance = null;
		}

		// ------- provider lifecycle ------------------------------

		public void StartProviding()
		{
			if (IsRunning)
				return;

			IsRunning = true;
			InvalidateReferenceContext();
			stateGeneration++;
			runCtknSrc = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCtknSrc.Token);
			ReconcileHeld();
			MintLoop(runCtknSrc.Token);

			if (SyncBus.Active && IsLocalMinter)
				ShareAll();
		}

		public void StopProviding()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			InvalidateReferenceContext();
			stateGeneration++;
			runCtknSrc?.Cancel();
			runCtknSrc?.Dispose();
			runCtknSrc = null;
			ReleaseAll();
		}

		private void OnBusActivated()
		{
			InvalidateReferenceContext();
			if (!IsRunning)
				return;

			if (SyncBus.IsAuthority)
			{
				ReconcileHeld();
				ShareAllAfterActivation();
			}
			else
			{
				// The combined snapshot is the session authority's complete set. Do not keep
				// aligning against this peer's previous offline map while it is in flight.
				ReleaseAll();
			}
		}

		private async void ShareAllAfterActivation()
		{
			try
			{
				await Awaitable.NextFrameAsync(lifetimeCtknSrc.Token);
				ShareAll();
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void OnBusDeactivated()
		{
			InvalidateLocalFrame();
			ReleasePreparation();
			if (IsRunning)
				ReconcileHeld();
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			InvalidateReferenceContext();
			if (!IsRunning)
				return;

			ReconcileHeld();
			if (IsLocalMinter)
				ShareAll();
		}

		private void OnConstraintsSynced()
		{
			if (IsRunning)
				ReconcileHeld();
		}

		private void OnConstraintsChanged(SyncDictionary<Guid, AnchorConstraintState>.EventData _)
		{
			foreach (Guid guid in preparationSaves)
				if (constraints.ContainsKey(guid)) publishedPreparationSaves.Add(guid);
			stateGeneration++;
			if (IsRunning)
			{
				ReconcileHeld();
				if (IsLocalMinter) ShareAll();
			}

			ConstraintsChanged.Invoke();
		}

		// ------- state import/export ------------------------------

		/// <summary>
		/// Replaces the provider's canonical constraint state. Only the offline peer or session
		/// authority may inject state; clients receive the same data from this provider's sync.
		/// </summary>
		public void SetConstraints(IEnumerable<AnchorConstraintData> next)
		{
			if (!SyncBus.IsAuthority)
			{
				Debug.LogWarning("Trying to set constraints from anchors while not the authority!");
				return;
			}

			InvalidateReferenceContext();
			List<KeyValuePair<Guid, AnchorConstraintState>> replacement = new();
			foreach (AnchorConstraintData entry in next)
				replacement.Add(new KeyValuePair<Guid, AnchorConstraintState>(entry.guid,
					new AnchorConstraintState
					{
						canonPose = entry.canonPose,
						bindingId = entry.bindingId,
					}));

			constraints.ReplaceAll(replacement,
				(a, b) => a.canonPose == b.canonPose && a.bindingId == b.bindingId);

			// Sharing otherwise only happens as a session comes up, which is enough while the
			// constraint set is fixed for the session's lifetime. State injected into a live
			// session — the embedding game changing which map is loaded — has to be uploaded
			// too, or peers receive guids they have no way to localize.
			if (SyncBus.Active)
				ShareAll();
		}

		// ------- constraints and leases ----------------------------

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning)
				return;

			foreach (HeldAnchor entry in held.Values)
			{
				ARAnchor anchor = entry.lease.Handle.anchor;
				if (anchor == null || anchor.trackingState != TrackingState.Tracking)
					continue;

				Transform t = anchor.transform;
				results.Add(new ColocationConstraint(
					new Pose(t.position, t.rotation), entry.canon, hasReliableRotation: true));
			}
		}

		private AnchorSource CurrentSource =>
			SyncBus.Active ? AnchorSource.Any : AnchorSource.Local;

		private void ReconcileHeld()
		{
			if (!IsRunning || !IsAvailable)
				return;

			AnchorSource source = CurrentSource;

			foreach ((Guid guid, AnchorConstraintState state) in constraints)
			{
				Pose canon = state.canonPose;
				if (held.TryGetValue(guid, out HeldAnchor existing))
				{
					existing.canon = canon;
					if (existing.source == source)
						continue;

					AnchorLease replacement = registry.Acquire(ToSerializable(guid), source);
					AnchorLease previous = existing.lease;
					existing.lease = replacement;
					existing.source = source;
					previous.Dispose();
					RestartPersist(guid, existing);
					continue;
				}

				HeldAnchor added = new()
				{
					lease = registry.Acquire(ToSerializable(guid), source),
					canon = canon,
					source = source,
				};

				held.Add(guid, added);
				RestartPersist(guid, added);
			}

			heldRemovalScratch.Clear();
			foreach (Guid guid in held.Keys)
				if (!constraints.ContainsKey(guid))
					heldRemovalScratch.Add(guid);

			foreach (Guid guid in heldRemovalScratch)
				Release(guid);
		}

		private void Release(Guid guid)
		{
			if (!held.Remove(guid, out HeldAnchor entry))
				return;

			CancelPersist(entry);
			entry.lease.Dispose();
		}

		private void ReleaseAll()
		{
			foreach (HeldAnchor entry in held.Values)
			{
				CancelPersist(entry);
				entry.lease.Dispose();
			}

			held.Clear();
		}

		/// <summary>
		/// Cancels any persist loop already running for the entry before starting a new one, so
		/// that re-acquiring a lease (a source change) cannot stack loops on the same anchor.
		/// </summary>
		private void RestartPersist(Guid guid, HeldAnchor entry)
		{
			CancelPersist(entry);
			entry.persistCtknSrc =
				CancellationTokenSource.CreateLinkedTokenSource(lifetimeCtknSrc.Token);
			PersistWhenActive(guid, entry, entry.persistCtknSrc);
		}

		private static void CancelPersist(HeldAnchor entry)
		{
			// The loop itself disposes the source once it has finished unwinding.
			entry.persistCtknSrc?.Cancel();
			entry.persistCtknSrc = null;
		}

		private async void PersistWhenActive(Guid guid, HeldAnchor entry,
			CancellationTokenSource ctknSrc)
		{
			try
			{
				CancellationToken ctkn = ctknSrc.Token;

				// The registry keeps retrying an anchor that will not load yet, so this waits with
				// it. False means the registry itself is gone. Cancellation is the other exit, and
				// it arrives when the entry is released or re-acquired.
				if (!await entry.lease.Handle.WaitForAnchorAsync(ctkn))
					return;

				// Asked once the anchor is live, so the answer is the running runtime's and not
				// a not-yet-started subsystem's. Nowhere to persist to is not a failure: the
				// anchor still tracks for this session, it just won't be there for the next one.
				if (!registry.canSaveAnchors)
					return;

				if (registry.IsSaved(ToSerializable(guid)))
				{
					AnchorPersisted.Invoke(guid);
					return;
				}

				// The registry holds the anchor for the write, which it cannot be told to abandon,
				// so releasing the entry underneath this is safe.
				if (!await registry.TrySaveAsync(entry.lease, ctkn))
				{
					Debug.LogWarning($"Anchor {guid} could not be saved locally.");
					return;
				}

				AnchorPersisted.Invoke(guid);
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
				if (entry.persistCtknSrc == ctknSrc)
					entry.persistCtknSrc = null;

				ctknSrc.Dispose();
			}
		}

		// ------- designated headset minting -----------------------

		private bool CanMintNow => IsRunning && RoamingMintEnabled && IsLocalMinter && LocallyReady &&
			(!SyncBus.Active || CanShareAnchors) && (MintingGate?.Invoke() ??
			(constraints.Count == 0 || ColocationManagerState() == ColocationAlignmentState.Localized));

		public static bool HasNearbyAnchor(IEnumerable<AnchorConstraintState> anchors, Vector3 position, float distance)
		{
			float distanceSq = distance * distance;
			foreach (AnchorConstraintState anchor in anchors)
				if ((anchor.canonPose.position - position).sqrMagnitude <= distanceSq) return true;
			return false;
		}

		private async void MintLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.FixedUpdateAsync(ctkn);
					if (!CanMintNow || IsMinting || pendingMints.Count > 0 ||
						MainXRRig.Instance == null || MainXRRig.Camera == null) continue;
					if (HasNearbyAnchor(constraints.Values, MainXRRig.Camera.transform.position, newAnchorDistance)) continue;
					try { await MintAsync(PoseUnderPlayer(), ctkn); }
					catch (OperationCanceledException) when (ctkn.IsCancellationRequested) { throw; }
					catch (Exception e) { Debug.LogException(e); }
					// Native failures should not cause a mint/save/upload attempt every physics tick.
					await Awaitable.WaitForSecondsAsync(1f, ctkn);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
		}

		private ColocationAlignmentState ColocationManagerState() =>
			colocator != null ? colocator.AlignmentState : ColocationAlignmentState.Stopped;

		private Pose PoseUnderPlayer()
		{
			Vector3 headPosition = MainXRRig.Camera.transform.position;
			Pose pose = new(headPosition - Vector3.up * 1.5f, Quaternion.identity);
			if (Physics.Raycast(new Ray(headPosition, Vector3.down), out RaycastHit hit, 2f,
				placementRaycastLayerMask, QueryTriggerInteraction.Ignore)) pose.position = hit.point;
			return pose;
		}

		private async Awaitable MintAsync(Pose pose, CancellationToken ctkn)
		{
			if (IsMinting) return;
			IsMinting = true;
			int generation = stateGeneration;
			int frame = localFrameGeneration;
			bool inSession = SyncBus.Active;
			AnchorOperation operation = NewOperation();
			bool Current() => generation == stateGeneration && frame == localFrameGeneration && CanMintNow &&
				inSession == SyncBus.Active && (!inSession || OperationIsCurrent(operation));
			try
			{
				await AnchorMinting.TryMintWithAsyncCommit(registry, pose, async minted =>
				{
					if (!Current()) return false;
					if (!inSession)
					{
						constraints.Set(minted.guid, new AnchorConstraintState { canonPose = pose, bindingId = -1 });
						if (minted.saved) AnchorPersisted.Invoke(minted.guid);
						minted.lease.Dispose(); // ReconcileHeld owns the accepted reference now.
						return true;
					}
					if (!await ShareBeforePublishing(minted.lease, Current, ctkn)) return false;
					pendingMints.Add(minted.guid, new PendingMint
					{
						lease = minted.lease, operation = operation, deadline = Time.realtimeSinceStartupAsDouble + 20
					});
					mintProposal.Raise(new AnchorProposal { operation = operation, guid = minted.guid, canon = pose, bindingId = -1 });
					return true; // PendingMint owns the lease until the ordered reply arrives.
				}, commitTakesLease: true, ctkn);
			}
			finally { IsMinting = false; }
		}

		// ------- sharing ------------------------------------------

		public bool CanShareAnchors => IsAvailable && registry.canShareAnchors;

		private async Awaitable<bool> ShareBeforePublishing(AnchorLease lease, Func<bool> stillCurrent,
			CancellationToken token)
		{
			for (int attempt = 1; attempt <= 5; attempt++)
			{
				if (!stillCurrent()) return false;
				XRResultStatus result = await registry.TryShareAsync(lease, token);
				if (!stillCurrent()) return false;
				if (!result.IsError()) return true;
				Debug.LogWarning($"Failed to share anchor {lease.Handle.guid}: {result} (attempt {attempt}).");
				if (attempt == 3)
					UserErrors.RaiseLocalized(UserErrorArea.Game, "error.share-title", "error.share-details");
				if (attempt < 5) await Awaitable.WaitForSecondsAsync(3f, token);
			}
			return false;
		}

		/// <summary>
		/// Uploads currently tracked local realizations while the old strategy keeps aligning.
		/// Does not replace constraints or start this provider. The caller commits only the
		/// successfully uploaded UUIDs, with their latest canonical poses, after preparation.
		/// </summary>
		public async Awaitable PrepareSharingAsync(IReadOnlyList<AnchorConstraintData> candidates,
			ISet<Guid> shared, CancellationToken token)
		{
			if (!SyncBus.Active || !IsLocalMinter || !CanShareAnchors) return;
			AnchorOperation operation = NewOperation();
			int frame = localFrameGeneration;
			foreach (AnchorConstraintData candidate in candidates)
			{
				token.ThrowIfCancellationRequested();
				using AnchorLease lease = registry.Acquire(ToSerializable(candidate.guid), AnchorSource.Local);
				ARAnchor anchor = lease.Handle.anchor;
				if (anchor == null || anchor.trackingState != TrackingState.Tracking) continue;
				XRResultStatus result = await registry.TryShareAsync(lease, token);
				token.ThrowIfCancellationRequested();
				if (!IsLocalMinter || !OperationIsCurrent(operation) || frame != localFrameGeneration) return;
				if (!result.IsError()) shared.Add(candidate.guid);
			}
		}

		/// <summary>
		/// Reports whether anchors can be shared at all, telling the user when they can't.
		/// Only Meta's runtime shares anchors.
		/// </summary>
		public bool WarnIfSharingUnsupported()
		{
			if (!IsAvailable)
				return false;

			Supported support = registry.sharedAnchorsSupport;
			if (support == Supported.Supported)
				return true;

			Debug.LogWarning($"Shared anchors are unavailable: {support}");
			UserErrors.RaiseLocalized(UserErrorArea.Game, "error.anchors-title", "error.anchors-details");
			return false;
		}

		private void ShareAll()
		{
			if (!IsRunning || !SyncBus.Active || !IsLocalMinter)
				return;

			if (!WarnIfSharingUnsupported())
				return;

			foreach (Guid guid in constraints.Keys)
				Share(guid);
		}

		private async void Share(Guid guid)
		{
			// Retrying an upload the runtime has no API for only produces noise.
			if (!IsAvailable || !registry.canShareAnchors)
				return;

			if (!sharesInFlight.Add(guid))
				return;

			AnchorOperation operation = NewOperation();
			const int maxAttempts = 5;
			const int attemptsBeforeTellingUser = 3;

			try
			{
				CancellationToken ctkn = runCtknSrc?.Token ?? lifetimeCtknSrc.Token;
				if (!held.TryGetValue(guid, out HeldAnchor entry))
					return;

				// A source change replaces the entry's lease, but a guid keeps one handle for the
				// registry's lifetime, so this stays the right one to watch.
				AnchorHandle handle = entry.lease.Handle;

				// Only a loaded anchor can be uploaded, so this waits for one. Failed means the
				// registry is gone; every other way out is checked each frame below.
				while (handle.anchor == null)
				{
					if (handle.state == AnchorHandle.State.Failed)
					{
						Debug.LogWarning($"Not sharing anchor {guid}: it never loaded locally.");
						return;
					}

					await Awaitable.NextFrameAsync(ctkn);
					if (!IsRunning || !IsLocalMinter || !OperationIsCurrent(operation) ||
					    !held.TryGetValue(guid, out HeldAnchor current) || current != entry)
						return;
				}

				for (int attempt = 1; attempt <= maxAttempts; attempt++)
				{
					if (!IsRunning || !IsLocalMinter || !OperationIsCurrent(operation)) return;
					// The registry holds the anchor for each upload, which it cannot be told to
					// abandon; the entry's lease is what keeps it loaded between attempts.
					XRResultStatus result = await registry.TryShareAsync(entry.lease, ctkn);
					if (!result.IsError())
						return;

					Debug.LogWarning($"Failed to share anchor {guid}: {result} " +
						$"(native {result.nativeStatusCode})");

					if (attempt == attemptsBeforeTellingUser)
						UserErrors.RaiseLocalized(UserErrorArea.Game, "error.share-title", "error.share-details");

					await Awaitable.WaitForSecondsAsync(3f, ctkn);

					// Nothing is waiting on an upload for a constraint that is gone.
					if (!IsRunning || !IsLocalMinter || !OperationIsCurrent(operation) ||
					    !held.TryGetValue(guid, out HeldAnchor stillHeld) || stillHeld != entry)
						return;
				}
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
				sharesInFlight.Remove(guid);
				if (IsRunning && IsLocalMinter && SyncBus.Active && !OperationIsCurrent(operation)) Share(guid);
			}
		}

		// ------- persistence utilities ---------------------------

		public async Awaitable<bool> EraseAsync(Guid guid, CancellationToken ctkn = default)
		{
			return IsAvailable && await registry.TryEraseSavedAsync(ToSerializable(guid), ctkn);
		}

		/// <summary>
		/// Asks the device which of <paramref name="guids"/> localize in the physical space it is
		/// standing in. Null where the runtime could not answer, which leaves
		/// <see cref="IsAnchorSaved"/> reporting only what this process has seen first-hand — not
		/// enough to conclude an anchor is absent.
		/// </summary>
		public async Awaitable<HashSet<Guid>> RefreshLocalizableAsync(IReadOnlyCollection<Guid> guids,
			float probeTimeoutSeconds, CancellationToken ctkn = default)
		{
			if (!IsAvailable || guids == null || guids.Count == 0)
				return null;

			List<SerializableGuid> requested = new(guids.Count);
			foreach (Guid guid in guids)
				requested.Add(ToSerializable(guid));

			HashSet<SerializableGuid> found =
				await registry.TryRefreshLocalizableAsync(requested, probeTimeoutSeconds, ctkn);

			if (found == null)
				return null;

			HashSet<Guid> localized = new(found.Count);
			foreach (SerializableGuid guid in found)
				localized.Add(guid.guid);

			return localized;
		}

		/// <summary>
		/// Whether this device is known to hold an anchor locally. Only as complete as the last
		/// <see cref="RefreshLocalizableAsync"/>; treat a false without one as "not known".
		/// </summary>
		public bool IsAnchorSaved(Guid guid) =>
			IsAvailable && registry.IsSaved(ToSerializable(guid));

		private static SerializableGuid ToSerializable(Guid guid) => new(guid);
	}
}
