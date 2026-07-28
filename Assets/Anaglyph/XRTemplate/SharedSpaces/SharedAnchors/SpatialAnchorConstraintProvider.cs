using Anaglyph.Netcode;
using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public readonly struct AnchorReferenceData
	{
		public AnchorReferenceData(Guid guid, Pose canonPose, int bindingId = -1)
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

	public struct AnchorReferenceState
	{
		public Pose canonPose;
		public int bindingId;
	}

	/// <summary>
	/// A complete shared-anchor colocation strategy. It owns the synchronized guid/canon-pose
	/// set, loads and persists those anchors through <see cref="AnchorRegistry"/>, shares them
	/// from the session authority, and mints additional references as the authority explores.
	/// A game-specific map system may import and export <see cref="References"/>, but is not
	/// required for the provider to align a session.
	/// </summary>
	[DefaultExecutionOrder(-200)]
	public class SpatialAnchorConstraintProvider : MonoBehaviour, IColocationConstraintProvider
	{
		public static SpatialAnchorConstraintProvider Instance { get; private set; }

		private sealed class HeldAnchor
		{
			public AnchorLease lease;
			public Pose canon;
			public AnchorSource source;
		}

		private readonly SyncDictionary<Guid, AnchorReferenceState> references =
			new("colocation.anchors.canon");
		public IReadOnlyDictionary<Guid, AnchorReferenceState> References => references;
		public event Action ReferencesChanged = delegate { };
		public event Action<Guid> AnchorPersisted = delegate { };

		[Tooltip("Distance from every existing anchor required before minting another")]
		[SerializeField] private float newAnchorDistance = 6f;

		[SerializeField] private LayerMask placementRaycastLayerMask = Physics.DefaultRaycastLayers;

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
		private readonly List<Guid> guidScratch = new();
		private readonly List<AnchorReferenceData> referenceScratch = new();
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
			registry = AnchorRegistry.Instance ?? FindFirstObjectByType<AnchorRegistry>();
			lifetimeCtknSrc = new CancellationTokenSource();

			references.ResetOnDeactivate = false;
			references.Register();
			references.Changed += OnReferencesChanged;
			references.Synced += OnReferencesSynced;

			SyncBus.Activated += OnBusActivated;
			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		private void OnDestroy()
		{
			StopProviding();
			lifetimeCtknSrc?.Cancel();

			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= OnBusDeactivated;
			SyncBus.Activated -= OnBusActivated;

			references.Synced -= OnReferencesSynced;
			references.Changed -= OnReferencesChanged;
			references.Unregister();

			if (Instance == this)
				Instance = null;
		}

		// ------- provider lifecycle ------------------------------

		public void StartProviding()
		{
			if (IsRunning)
				return;

			IsRunning = true;
			stateGeneration++;
			runCtknSrc = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCtknSrc.Token);
			ReconcileHeld();
			MintLoop(runCtknSrc.Token);

			if (SyncBus.Active && SyncBus.IsAuthority)
				ShareAll();
		}

		public void StopProviding()
		{
			if (!IsRunning)
				return;

			IsRunning = false;
			stateGeneration++;
			runCtknSrc?.Cancel();
			runCtknSrc?.Dispose();
			runCtknSrc = null;
			ReleaseAll();
		}

		private void OnBusActivated()
		{
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
			if (IsRunning)
				ReconcileHeld();
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			if (!IsRunning)
				return;

			ReconcileHeld();
			if (isAuthority)
				ShareAll();
		}

		private void OnReferencesSynced()
		{
			if (IsRunning)
				ReconcileHeld();
		}

		private void OnReferencesChanged(SyncDictionary<Guid, AnchorReferenceState>.EventData _)
		{
			stateGeneration++;
			if (IsRunning)
				ReconcileHeld();

			ReferencesChanged.Invoke();
		}

		// ------- state import/export ------------------------------

		/// <summary>
		/// Replaces the provider's canonical reference state. Only the offline peer or session
		/// authority may inject state; clients receive the same data from this provider's sync.
		/// </summary>
		public void SetReferences(IEnumerable<AnchorReferenceData> next)
		{
			if (!SyncBus.IsAuthority)
				return;

			referenceScratch.Clear();
			referenceScratch.AddRange(next);

			guidScratch.Clear();
			foreach (Guid guid in references.Keys)
			{
				bool retained = false;
				foreach (AnchorReferenceData entry in referenceScratch)
					if (entry.guid == guid)
					{
						retained = true;
						break;
					}

				if (!retained)
					guidScratch.Add(guid);
			}

			foreach (Guid guid in guidScratch)
				references.Remove(guid);

			foreach (AnchorReferenceData entry in referenceScratch)
			{
				AnchorReferenceState state = new()
				{
					canonPose = entry.canonPose,
					bindingId = entry.bindingId,
				};

				if (!references.TryGetValue(entry.guid, out AnchorReferenceState existing) ||
				    existing.canonPose != state.canonPose || existing.bindingId != state.bindingId)
					references.Set(entry.guid, state);
			}
		}

		// ------- references and leases ----------------------------

		public void GetColocationReferences(List<ColocationConstraint> results)
		{
			if (!IsRunning)
				return;

			foreach (HeldAnchor entry in held.Values)
			{
				AnchorHandle handle = entry.lease.Handle;
				if (handle.state != AnchorHandle.State.Active) continue;
				if (handle.anchor.trackingState != TrackingState.Tracking) continue;

				Transform t = handle.anchor.transform;
				results.Add(new ColocationConstraint(
					new Pose(t.position, t.rotation), entry.canon, hasReliableRotation: true));
			}
		}

		private AnchorSource CurrentSource =>
			SyncBus.Active && !SyncBus.IsAuthority ? AnchorSource.Any : AnchorSource.Local;

		private void ReconcileHeld()
		{
			if (!IsRunning || !IsAvailable)
				return;

			AnchorSource source = CurrentSource;

			foreach ((Guid guid, AnchorReferenceState state) in references)
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
					PersistWhenActive(guid, existing);
					continue;
				}

				HeldAnchor added = new()
				{
					lease = registry.Acquire(ToSerializable(guid), source),
					canon = canon,
					source = source,
				};

				held.Add(guid, added);
				PersistWhenActive(guid, added);
			}

			guidScratch.Clear();
			foreach (Guid guid in held.Keys)
				if (!references.ContainsKey(guid))
					guidScratch.Add(guid);

			foreach (Guid guid in guidScratch)
				Release(guid);
		}

		private void Release(Guid guid)
		{
			if (!held.Remove(guid, out HeldAnchor entry))
				return;

			entry.lease.Dispose();
		}

		private void ReleaseAll()
		{
			foreach (HeldAnchor entry in held.Values)
				entry.lease.Dispose();

			held.Clear();
		}

		private async void PersistWhenActive(Guid guid, HeldAnchor entry)
		{
			try
			{
				CancellationToken ctkn = lifetimeCtknSrc.Token;

				while (entry.lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);
					if (!held.TryGetValue(guid, out HeldAnchor current) || current != entry)
						return;
				}

				SerializableGuid serializableGuid = ToSerializable(guid);
				if (!registry.IsSaved(serializableGuid) &&
				    !await registry.TrySaveAsync(serializableGuid, ctkn))
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
		}

		// ------- authority minting -------------------------------

		private async void MintLoop(CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.FixedUpdateAsync(ctkn);

					if (!RoamingMintEnabled || !SyncBus.IsAuthority || IsMinting || !IsAvailable)
						continue;
					if (MintingGate != null && !MintingGate())
						continue;
					if (MintingGate == null && references.Count > 0 &&
					    ColocationManagerState() != ColocationState.Localized)
						continue;
					if (MainXRRig.Camera == null)
						continue;

					float3 headPosition = MainXRRig.Camera.transform.position;
					float closestDistanceSq = float.MaxValue;
					foreach (AnchorReferenceState state in references.Values)
						closestDistanceSq = math.min(closestDistanceSq,
							math.distancesq((float3)state.canonPose.position, headPosition));

					if (closestDistanceSq > newAnchorDistance * newAnchorDistance)
						await MintUnderPlayer(ctkn);
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

		private static ColocationState ColocationManagerState()
		{
			ReferenceColocator colocator = FindFirstObjectByType<ReferenceColocator>();
			return colocator != null ? colocator.State : ColocationState.Stopped;
		}

		private async Awaitable MintUnderPlayer(CancellationToken ctkn)
		{
			Vector3 headPosition = MainXRRig.Camera.transform.position;
			Pose pose = new(headPosition - Vector3.up * 1.5f, Quaternion.identity);
			Ray ray = new(headPosition, Vector3.down);

			if (Physics.Raycast(ray, out RaycastHit hit, 2f, placementRaycastLayerMask,
				    QueryTriggerInteraction.Ignore))
				pose.position = hit.point;

			await MintAsync(pose, ctkn);
		}

		private async Awaitable MintAsync(Pose pose, CancellationToken ctkn)
		{
			if (IsMinting)
				return;

			IsMinting = true;
			int generation = stateGeneration;
			AnchorLease minted = null;
			Guid guid = Guid.Empty;
			bool saved = false;
			bool established = false;

			try
			{
				minted = await registry.TryMintAsync(pose, ctkn);
				if (minted == null)
					return;

				guid = minted.Handle.guid.guid;
				saved = await registry.TrySaveAsync(minted.Handle.anchor, ctkn);
				if (!saved)
					return;

				ctkn.ThrowIfCancellationRequested();
				if (!IsRunning || !RoamingMintEnabled ||
				    generation != stateGeneration || !SyncBus.IsAuthority)
					return;

				references.Set(guid, new AnchorReferenceState
				{
					canonPose = pose,
					bindingId = -1,
				});
				established = true;
				AnchorPersisted.Invoke(guid);

				if (SyncBus.Active)
					Share(guid);
			}
			finally
			{
				minted?.Dispose();
				IsMinting = false;

				if (saved && !established && registry != null && registry.IsAvailable)
				{
					try
					{
						await registry.TryEraseSavedAsync(
							ToSerializable(guid), CancellationToken.None);
					}
					catch (ObjectDisposedException)
					{
					}
				}
			}
		}

		// ------- sharing ------------------------------------------

		public void WarnIfSharingUnsupported()
		{
			if (!IsAvailable || registry.sharedAnchorsSupport == Supported.Supported)
				return;

			Supported support = registry.sharedAnchorsSupport;
			Debug.LogWarning($"Shared anchors are unavailable: {support}");
			UserErrors.Raise("Shared spatial anchors unavailable",
				$"This headset reports shared anchor support as '{support}'.");
		}

		private void ShareAll()
		{
			if (!IsRunning || !SyncBus.Active || !SyncBus.IsAuthority)
				return;

			WarnIfSharingUnsupported();
			foreach (Guid guid in references.Keys)
				Share(guid);
		}

		private async void Share(Guid guid)
		{
			if (!sharesInFlight.Add(guid))
				return;

			const int maxAttempts = 5;
			const int attemptsBeforeTellingUser = 3;

			try
			{
				CancellationToken ctkn = runCtknSrc?.Token ?? lifetimeCtknSrc.Token;
				if (!held.TryGetValue(guid, out HeldAnchor entry))
					return;

				while (entry.lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);
					if (!IsRunning || !SyncBus.Active || !SyncBus.IsAuthority ||
					    !held.TryGetValue(guid, out HeldAnchor current) || current != entry)
						return;
				}

				for (int attempt = 1; attempt <= maxAttempts; attempt++)
				{
					XRResultStatus result =
						await registry.TryShareAsync(ToSerializable(guid), ctkn);
					if (!result.IsError())
						return;

					Debug.LogWarning($"Failed to share anchor {guid}: {result} " +
						$"(native {result.nativeStatusCode})");

					if (attempt == attemptsBeforeTellingUser)
						UserErrors.Raise("Couldn't share a spatial anchor",
							"Shared anchors require a working internet connection.");

					await Awaitable.WaitForSecondsAsync(3f, ctkn);
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
			}
		}

		// ------- persistence utilities ---------------------------

		public async Awaitable<bool> EraseAsync(Guid guid, CancellationToken ctkn = default)
		{
			return IsAvailable && await registry.TryEraseSavedAsync(ToSerializable(guid), ctkn);
		}

		public async Awaitable<HashSet<Guid>> ProbeAsync(IReadOnlyCollection<Guid> guids,
			float timeoutSeconds, CancellationToken ctkn = default)
		{
			HashSet<Guid> localized = new();
			if (!IsAvailable || guids.Count == 0)
				return localized;

			List<SerializableGuid> serializableGuids = new(guids.Count);
			foreach (Guid guid in guids)
				serializableGuids.Add(ToSerializable(guid));

			HashSet<SerializableGuid> found =
				await registry.ProbeLocalizableAsync(serializableGuids, timeoutSeconds, ctkn);
			foreach (SerializableGuid guid in found)
				localized.Add(guid.guid);

			return localized;
		}

		private static SerializableGuid ToSerializable(Guid guid) => new(guid);
	}
}
