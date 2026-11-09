using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using Object = UnityEngine.Object;
using SerializableGuid = UnityEngine.XR.ARSubsystems.SerializableGuid;

namespace Anaglyph.XR.SharedSpaces
{
	/// <summary>
	/// One anchor the session authority shares through the headset runtime, for the others to
	/// find the authority's frame by: where the runtime tracks it on each headset, against where
	/// the authority says it stands. It lives for the session and is saved nowhere. A runtime
	/// that shares no anchors (any but Meta's) leaves it empty. Sharing and downloading go
	/// through the runtime's cloud, so neither is tried without internet.
	/// </summary>
	public sealed class SessionAnchor : IDisposable
	{
		public struct Published
		{
			/// <summary>The anchor's id, which is also the group it is shared with. Empty while there is none.</summary>
			public Guid guid;
			/// <summary>Where the anchor stands in the authority's frame.</summary>
			public Pose canon;
		}

		private const float RetrySeconds = 10f;
		private const float OfflineRecheckSeconds = 2f;
		private const float SurfaceSeconds = 5f;
		private const float ManagerLookupSeconds = 5f;
		private const int EnhancedSpatialServicesOff = -1000169004;

		private readonly SyncVariable<Published> published = new("colocation.visual.sessionAnchor");
		private readonly Func<Pose, Pose> canonOf;
		private readonly Func<bool> hasInternet;
		private readonly Action<string> outcome;

		private ARAnchorManager manager;
		private float nextManagerLookup;
		private ARAnchor held;
		// the anchor held or being downloaded; empty while one is being made
		private Guid guid;
		// the authority's alignment the anchor was made in; -1 for one downloaded
		private int frame = -1;
		private bool busy;
		private int request;
		private float retryAt;
		private string lastFailure;

		/// <param name="canonOf">Maps a world pose, as it stands now, into the frame this headset is aligned to.</param>
		/// <param name="hasInternet">Asked only when about to share or download, every <see cref="OfflineRecheckSeconds"/> at most while it says no.</param>
		public SessionAnchor(Func<Pose, Pose> canonOf, Func<bool> hasInternet, Action<string> outcome)
		{
			this.canonOf = canonOf;
			this.hasInternet = hasInternet;
			this.outcome = outcome;
			published.Register();
		}

		public void Dispose()
		{
			Release();
			published.Unregister();
		}

		private MetaOpenXRAnchorSubsystem Sharing
		{
			get
			{
				if (manager == null && Time.unscaledTime >= nextManagerLookup)
				{
					nextManagerLookup = Time.unscaledTime + ManagerLookupSeconds;
					manager = Object.FindFirstObjectByType<ARAnchorManager>();
				}

				return manager != null && manager.subsystem is MetaOpenXRAnchorSubsystem meta &&
				       meta.isSharedAnchorsSupported == Supported.Supported ? meta : null;
			}
		}

		/// <summary>
		/// The authority, every frame: keeps one anchor shared, made at <paramref name="mintAt"/>,
		/// for as long as <paramref name="alignmentGeneration"/> stays the alignment it was made in.
		/// </summary>
		public void UpdateAuthority(bool aligned, int alignmentGeneration, Pose mintAt)
		{
			Guid current = published.Value.guid;
			bool any = held != null || busy || current != Guid.Empty;
			if (!aligned || any && (frame != alignmentGeneration || guid != current)) Drop();

			if (!aligned || held != null || busy || published.Value.guid != Guid.Empty || Time.unscaledTime < retryAt) return;
			MetaOpenXRAnchorSubsystem meta = Sharing;
			if (meta != null && Online()) Mint(meta, alignmentGeneration, mintAt);
		}

		/// <summary>Everyone else, every frame: downloads the authority's anchor while <paramref name="wanted"/>.</summary>
		public void UpdateJoiner(bool wanted)
		{
			Guid current = published.Value.guid;
			if (guid != current && (held != null || busy)) Release();

			if (!wanted || current == Guid.Empty || held != null || busy || Time.unscaledTime < retryAt) return;
			MetaOpenXRAnchorSubsystem meta = Sharing;
			if (meta != null && Online()) Load(meta, current);
		}

		/// <summary>The anchor where the runtime tracks it now, and where the authority says it stands.</summary>
		public bool TryGetTracked(out Pose live, out Pose canon)
		{
			live = canon = default;
			Published now = published.Value;
			if (held == null || guid == Guid.Empty || guid != now.guid || held.trackingState != TrackingState.Tracking) return false;
			live = PoseOf(held);
			canon = now.canon;
			return true;
		}

		/// <summary>The frame the anchor was shared in is gone, or no one is to align by it: lets go of it, and the authority withdraws it.</summary>
		public void Drop()
		{
			Release();
			if (SyncBus.IsAuthority && published.Value.guid != Guid.Empty) published.Value = default;
		}

		private void Release()
		{
			if (busy) request++;
			if (held != null) Remove(held);
			held = null;
			guid = Guid.Empty;
		}

		private void Remove(ARAnchor anchor)
		{
			if (anchor == null) return;
			if (manager != null) manager.TryRemoveAnchor(anchor);
			if (anchor != null) Object.Destroy(anchor.gameObject);
		}

		private static Pose PoseOf(ARAnchor anchor) => new(anchor.transform.position, anchor.transform.rotation);

		private async void Mint(MetaOpenXRAnchorSubsystem meta, int alignmentGeneration, Pose at)
		{
			busy = true;
			frame = alignmentGeneration;
			guid = Guid.Empty;
			int mine = ++request;
			ARAnchor minted = null;
			try
			{
				Result<ARAnchor> added = await manager.TryAddAnchorAsync(at);
				minted = added.value;
				if (mine != request) return;
				if (!added.status.IsSuccess() || minted == null)
				{
					Failed($"could not be made ({added.status})");
					return;
				}

				SerializableGuid id = minted.trackableId;
				meta.sharedAnchorsGroupId = id;
				XRResultStatus shared = await manager.TryShareAnchorAsync(minted);
				if (mine != request || minted == null || !SyncBus.Active || !SyncBus.IsAuthority) return;
				if (shared.IsError())
				{
					Failed($"could not be shared ({Describe(shared)})");
					return;
				}

				held = minted;
				minted = null;
				guid = id.guid;
				lastFailure = null;
				published.Value = new Published { guid = guid, canon = canonOf(PoseOf(held)) };
				outcome("Session anchor: shared");
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				retryAt = Time.unscaledTime + RetrySeconds;
			}
			finally
			{
				Remove(minted);
				busy = false;
			}
		}

		private async void Load(MetaOpenXRAnchorSubsystem meta, Guid wanted)
		{
			busy = true;
			frame = -1;
			guid = wanted;
			int mine = ++request;
			ARAnchor surfaced = null;
			try
			{
				List<XRAnchor> downloaded = new(1);
				meta.sharedAnchorsGroupId = new SerializableGuid(wanted);
				XRResultStatus status = await manager.TryLoadAllSharedAnchorsAsync(downloaded, null);
				if (status.IsError() || downloaded.Count == 0)
				{
					if (mine == request) Failed($"could not be downloaded ({(status.IsError() ? Describe(status) : "nothing was shared")})");
					return;
				}

				// its object appears through the manager some frames after the download
				float deadline = Time.unscaledTime + SurfaceSeconds;
				while (!manager.trackables.TryGetTrackable(downloaded[0].trackableId, out surfaced))
				{
					if (Time.unscaledTime > deadline)
					{
						if (mine == request) Failed("was downloaded but never appeared");
						return;
					}

					await Awaitable.NextFrameAsync();
				}

				if (mine != request) return;
				held = surfaced;
				surfaced = null;
				lastFailure = null;
				outcome("Session anchor: downloaded, aligning once the headset finds it in the room");
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				retryAt = Time.unscaledTime + RetrySeconds;
			}
			finally
			{
				Remove(surfaced);
				busy = false;
			}
		}

		private bool Online()
		{
			if (hasInternet()) return true;
			Failed("needs an internet connection", OfflineRecheckSeconds);
			return false;
		}

		private void Failed(string why, float retrySeconds = RetrySeconds)
		{
			retryAt = Time.unscaledTime + retrySeconds;
			if (why == lastFailure) return;
			lastFailure = why;
			outcome($"Session anchor {why}: aligning by camera where there is one, trying again every {retrySeconds:F0} s");
		}

		private static string Describe(XRResultStatus status) => status.nativeStatusCode == EnhancedSpatialServicesOff
			? "Enhanced Spatial Services is off in the headset's settings"
			: status.ToString();
	}
}
