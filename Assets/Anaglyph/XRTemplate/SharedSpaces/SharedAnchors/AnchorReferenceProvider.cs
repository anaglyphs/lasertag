using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Holds a set of spatial anchors and the canon poses they are supposed to occupy, and
	/// presents each tracked one as a <see cref="ColocationReference"/>.
	///
	/// This owns anchor *plumbing* only — creating, saving, loading, sharing, probing, and
	/// keeping AR Foundation trackables alive. It has no idea what a map is: whoever drives
	/// it decides which anchors to hold, what their canon poses are, and when to mint more.
	/// That keeps every AR Foundation type behind this class, so callers can speak plain
	/// <see cref="Guid"/> and never reference the AR packages.
	///
	/// Everything is keyed by anchor guid, which on this runtime is simultaneously the
	/// trackable id, the local-storage save id, and the shared group id.
	/// </summary>
	[DefaultExecutionOrder(-200)]
	public class AnchorReferenceProvider : MonoBehaviour, IColocationReferenceSource
	{
		public static AnchorReferenceProvider Instance { get; private set; }

		private sealed class HeldAnchor
		{
			public AnchorLease lease;
			public Pose canon;
		}

		private readonly Dictionary<Guid, HeldAnchor> held = new();

		private AnchorRegistry registry;
		private ARAnchorManager anchorManager;
		private MetaOpenXRAnchorSubsystem metaAnchorSubsystem;

		private CancellationTokenSource lifetimeCtknSrc;

		/// <summary>
		/// Raised once an adopted or minted anchor is materialized AND persisted locally, i.e.
		/// once it is durable. Downloading a shared anchor does not save it — persisting is an
		/// explicit step this provider takes on the receiver's behalf, and it is what lets a
		/// later session re-enter the space with no host and no internet.
		/// </summary>
		public event Action<Guid> AnchorPersisted = delegate { };

		/// <summary>False where there is no anchor runtime at all (in-editor).</summary>
		public bool IsAvailable => registry != null;

		/// <summary>One mint at a time; anchor creation is not reentrant.</summary>
		public bool IsMinting { get; private set; }

		public IReadOnlyCollection<Guid> HeldAnchors => held.Keys;

		private void Awake()
		{
			Instance = this;
			lifetimeCtknSrc = new CancellationTokenSource();

#if !UNITY_EDITOR
			anchorManager = FindFirstObjectByType<ARAnchorManager>();
			metaAnchorSubsystem = (MetaOpenXRAnchorSubsystem)anchorManager.subsystem;
			registry = new AnchorRegistry(anchorManager, metaAnchorSubsystem);
#endif
		}

		private void OnDestroy()
		{
			lifetimeCtknSrc?.Cancel();
			ReleaseAll();
			registry?.Dispose();
		}

		// ------- references ----------------------------------------

		public void GetColocationReferences(List<ColocationReference> results)
		{
			foreach (HeldAnchor entry in held.Values)
			{
				AnchorHandle handle = entry.lease.Handle;
				if (handle.state != AnchorHandle.State.Active) continue;
				if (handle.anchor.trackingState != TrackingState.Tracking) continue;

				Transform t = handle.anchor.transform;

				// An anchor's rotation is as trustworthy as its position, so a single one is
				// enough to fully constrain a fit.
				results.Add(new ColocationReference(
					new Pose(t.position, t.rotation), entry.canon, hasReliableRotation: true));
			}
		}

		/// <summary>Where an anchor currently appears, if it is loaded and tracking.</summary>
		public bool TryGetObserved(Guid guid, out Pose observed)
		{
			observed = default;

			if (!held.TryGetValue(guid, out HeldAnchor entry))
				return false;

			AnchorHandle handle = entry.lease.Handle;
			if (handle.state != AnchorHandle.State.Active) return false;
			if (handle.anchor.trackingState != TrackingState.Tracking) return false;

			Transform t = handle.anchor.transform;
			observed = new Pose(t.position, t.rotation);
			return true;
		}

		// ------- holding -------------------------------------------

		/// <summary>
		/// Start holding an anchor, loading it from local storage and/or the shared group as
		/// <paramref name="source"/> permits. Idempotent; a second adopt only updates canon.
		/// </summary>
		public void Adopt(Guid guid, Pose canon, AnchorSource source)
		{
			if (registry == null)
				return;

			if (held.TryGetValue(guid, out HeldAnchor existing))
			{
				existing.canon = canon;
				return;
			}

			SerializableGuid trackableGuid = ToSerializable(guid);
			HeldAnchor entry = new()
			{
				lease = registry.Acquire(trackableGuid, source),
				canon = canon,
			};

			held[guid] = entry;
			PersistWhenActive(guid, entry);
		}

		public void SetCanon(Guid guid, Pose canon)
		{
			if (held.TryGetValue(guid, out HeldAnchor entry))
				entry.canon = canon;
		}

		public void Release(Guid guid)
		{
			if (!held.Remove(guid, out HeldAnchor entry))
				return;

			entry.lease.Dispose();
		}

		public void ReleaseAll()
		{
			foreach (HeldAnchor entry in held.Values)
				entry.lease.Dispose();

			held.Clear();
		}

		// Waits for AR Foundation to materialize the anchor, then makes sure it is on disk.
		private async void PersistWhenActive(Guid guid, HeldAnchor entry)
		{
			try
			{
				CancellationToken ctkn = lifetimeCtknSrc.Token;

				while (entry.lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);

					if (!held.TryGetValue(guid, out HeldAnchor current) || current != entry)
						return; // released or replaced while waiting
				}

				SerializableGuid trackableGuid = ToSerializable(guid);

				if (!registry.IsSaved(trackableGuid))
				{
					bool saved = await registry.TrySaveAsync(trackableGuid, ctkn);

					if (!saved)
					{
						Debug.LogWarning($"Anchor {guid} could not be saved locally; it will " +
							"be gone at the end of this session.");
						return;
					}
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

		// ------- minting -------------------------------------------

		/// <summary>
		/// Creates an anchor at <paramref name="createAt"/>, saves it locally, and starts
		/// holding it with <paramref name="canon"/> as its canon pose. These differ whenever
		/// the anchor stands in for something whose canon pose is already known (a registered
		/// tag), and are identical for a roaming anchor that defines its own.
		///
		/// Returns <see cref="Guid.Empty"/> if the anchor could not be created or persisted —
		/// an unsaveable anchor is useless, since no later session could load it back.
		/// </summary>
		public async Awaitable<Guid> MintAsync(Pose createAt, Pose canon, CancellationToken ctkn)
		{
			if (registry == null || IsMinting)
				return Guid.Empty;

			IsMinting = true;

			AnchorLease lease = null;
			bool established = false;

			try
			{
				Result<ARAnchor> result = await anchorManager.TryAddAnchorAsync(createAt);
				if (!result.status.IsSuccess() || result.value == null)
					throw new Exception("Failed to create new anchor!");

				lease = registry.Acquire(result.value, AnchorSource.Local);
				Guid guid = ((SerializableGuid)result.value.trackableId).guid;

				ctkn.ThrowIfCancellationRequested();

				bool saved = await registry.TrySaveAsync(result.value, ctkn);
				if (!saved)
					return Guid.Empty;

				ctkn.ThrowIfCancellationRequested();

				held[guid] = new HeldAnchor { lease = lease, canon = canon };
				established = true;

				return guid;
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
				IsMinting = false;

				if (lease != null && !established)
					lease.Dispose();
			}

			return Guid.Empty;
		}

		// ------- sharing -------------------------------------------

		/// <summary>
		/// Warns the user if this headset cannot share anchors at all, so a host does not
		/// silently run a session no joiner can align to.
		/// </summary>
		public void WarnIfSharingUnsupported()
		{
			if (metaAnchorSubsystem == null)
				return;

			Supported support = metaAnchorSubsystem.isSharedAnchorsSupported;
			if (support == Supported.Supported)
				return;

			Debug.LogWarning($"Shared anchors are not enabled/supported! {support}");

			UserErrors.Raise("Shared spatial anchors unavailable",
				$"This headset reports shared anchor support as '{support}'. " +
				"Joiners will not be able to align. Try AprilTag colocation instead.");
		}

		/// <summary>
		/// Uploads a held anchor to its shared group so other headsets can download it,
		/// waiting for it to materialize first. Meta's shares are transient, so this has to
		/// be redone every session for anchors that peers might not already hold.
		/// </summary>
		public async Awaitable<bool> ShareAsync(Guid guid, CancellationToken ctkn)
		{
			const float retrySeconds = 3f;
			// Retries are silent until it's clear this isn't a transient hiccup
			const int attemptsBeforeTellingUser = 3;
			// A rejected write retries identically to a network failure, so don't hang on it
			// forever — the canon pose is already published and the local save already exists.
			const int maxAttempts = 5;

			if (registry == null)
				return false;

			try
			{
				if (!held.TryGetValue(guid, out HeldAnchor entry))
					return false;

				while (entry.lease.Handle.state != AnchorHandle.State.Active)
				{
					await Awaitable.NextFrameAsync(ctkn);

					if (!held.TryGetValue(guid, out HeldAnchor current) || current != entry)
						return false;
				}

				ARAnchor anchor = entry.lease.Handle.anchor;

				for (int attempt = 1; attempt <= maxAttempts; attempt++)
				{
					ctkn.ThrowIfCancellationRequested();

					metaAnchorSubsystem.sharedAnchorsGroupId = anchor.trackableId;
					XRResultStatus result = await anchorManager.TryShareAnchorAsync(anchor);

					ctkn.ThrowIfCancellationRequested();

					if (!result.IsError())
						return true;

					// nativeStatusCode is the raw XrResult — the only place a network failure
					// and a rejected write (e.g. re-sharing into a group another device
					// created) are distinguishable.
					Debug.LogWarning($"Failed to share anchor {anchor.trackableId}: {result} " +
						$"(native {result.nativeStatusCode})");

					if (attempt == attemptsBeforeTellingUser)
						UserErrors.Raise("Couldn't share a spatial anchor",
							"Shared spatial anchors are uploaded through Meta's servers, so this " +
							"headset needs a working internet connection. Joiners that have " +
							"played this map before are unaffected.");

					await Awaitable.WaitForSecondsAsync(retrySeconds, ctkn);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			return false;
		}

		// ------- storage -------------------------------------------

		/// <summary>Deletes an anchor's local save. Only call for anchors nothing references.</summary>
		public async Awaitable<bool> EraseAsync(Guid guid, CancellationToken ctkn = default)
		{
			if (registry == null)
				return false;

			try
			{
				return await registry.TryEraseSavedAsync(ToSerializable(guid), ctkn);
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}

			return false;
		}

		/// <summary>
		/// Which of these saved anchors localize in the physical space the headset is standing
		/// in right now, with no AR Foundation trackables materialized. The cheap first phase
		/// of working out where you are.
		/// </summary>
		public async Awaitable<HashSet<Guid>> ProbeAsync(IReadOnlyCollection<Guid> guids,
			float timeoutSeconds, CancellationToken ctkn = default)
		{
			HashSet<Guid> localized = new();

			if (registry == null || guids.Count == 0)
				return localized;

			List<SerializableGuid> trackableGuids = new(guids.Count);
			foreach (Guid guid in guids)
				trackableGuids.Add(ToSerializable(guid));

			HashSet<SerializableGuid> found =
				await registry.ProbeLocalizableAsync(trackableGuids, timeoutSeconds, ctkn);

			foreach (SerializableGuid guid in found)
				localized.Add(guid.guid);

			return localized;
		}

		private static SerializableGuid ToSerializable(Guid guid)
		{
			return new SerializableGuid(guid);
		}
	}
}
