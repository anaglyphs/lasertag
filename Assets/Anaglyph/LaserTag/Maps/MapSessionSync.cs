using System;
using Anaglyph.Netcode.SyncVariables;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// The session's map identity. Colocation providers synchronize their own state and map
	/// objects replicate through NGO, so only identity belongs here.
	/// </summary>
	public struct MapIdentity
	{
		public Guid id;
		public Guid version;
		public FixedString64Bytes name;
	}

	/// <summary>
	/// Replicates which map a session is using, and adopts it on the peers that receive it.
	///
	/// Adoption is fork-on-conflict: a joiner holding its own edited copy of the same map id
	/// forks those edits off under a new id before taking the authority's version, so
	/// collaborative editing never silently overwrites local work.
	/// </summary>
	internal sealed class MapSessionSync
	{
		private readonly MapManager owner;
		private readonly float switchTimeoutSeconds;

		private readonly SyncVariable<MapIdentity> mapIdentity = new("map.identity");
		private readonly SyncVariable<bool> mapChanging = new("map.changing");

		private bool authoritySessionStarted;
		private float mapChangeStartedTime;

		public MapSessionSync(MapManager owner, float switchTimeoutSeconds)
		{
			this.owner = owner;
			this.switchTimeoutSeconds = switchTimeoutSeconds;
		}

		/// <summary>Whether the session is mid-map-change, and so has no trustworthy frame.</summary>
		public bool IsChangingMap => mapChanging.Value;
		public event Action ChangingMapChanged = delegate { };

		/// <summary>
		/// Whether this peer has taken the session's published map. Until it has, its own copy is
		/// about to be replaced and provider state must not be recorded into it.
		/// </summary>
		public bool SessionMapAdopted { get; private set; }

		/// <summary>The session's map id as "N", or null when nothing has been published.</summary>
		public string SessionMapId =>
			mapIdentity.Value.id == Guid.Empty ? null : MapGuid.ToString(mapIdentity.Value.id);

		public void Register()
		{
			mapIdentity.Register();
			mapIdentity.Changed += OnIdentityChanged;
			mapIdentity.Synced += OnIdentitySynced;

			mapChanging.Register();
			mapChanging.Changed += OnChangingChanged;

			SyncBus.Deactivated += OnBusDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		public void Unregister()
		{
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= OnBusDeactivated;

			mapChanging.Changed -= OnChangingChanged;
			mapChanging.Unregister();

			mapIdentity.Synced -= OnIdentitySynced;
			mapIdentity.Changed -= OnIdentityChanged;
			mapIdentity.Unregister();
		}

		private void OnChangingChanged(bool _, bool __) => ChangingMapChanged.Invoke();

		// ------- authority ---------------------------------------

		public void PublishIdentity()
		{
			GameMap map = owner.CurrentMap;
			if (!SyncBus.Active || !SyncBus.IsAuthority || map == null)
				return;

			if (!MapGuid.TryParse(map.id, out Guid id) ||
			    !MapGuid.TryParse(map.version, out Guid version))
			{
				Debug.LogError($"Map '{map.name}' has an unusable id or version; " +
					"peers cannot be told which map this session is using.");
				return;
			}

			FixedString64Bytes name = default;
			name.CopyFromTruncated(map.name ?? "");
			mapIdentity.Value = new MapIdentity { id = id, version = version, name = name };
		}

		/// <summary>Opens the hold every peer keeps while it re-aligns onto an incoming map.</summary>
		public void BeginMapChange()
		{
			mapChanging.Value = true;
			mapChangeStartedTime = Time.time;
		}

		public void TickMapChange(bool frameAgrees)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !mapChanging.Value)
				return;

			if (frameAgrees)
			{
				mapChanging.Value = false;
				return;
			}

			if (Time.time - mapChangeStartedTime < switchTimeoutSeconds)
				return;

			// Releasing the hold is not a claim that the frame is good — the trust check still
			// fails on its own terms, and everything gated on it stays gated. It stops the session
			// sitting in a state it has no way out of.
			Debug.LogWarning($"Map change did not align within {switchTimeoutSeconds}s. " +
				"Releasing the hold; the world frame remains untrusted.");
			mapChanging.Value = false;
		}

		private void AuthoritySessionStart()
		{
			if (authoritySessionStarted)
				return;

			authoritySessionStarted = true;

			GameMap map = owner.EnsureCurrentMap();
			if (map == null)
				return;

			map.lastUsed = DateTime.UtcNow.Ticks;
			owner.SaveCurrentMap();
			owner.Objects.SpawnLocalObjects();
		}

		private void OnBusDeactivated()
		{
			authoritySessionStarted = false;
			SessionMapAdopted = false;
		}

		/// <summary>
		/// Authority moved. A promoted peer has to run the flow it skipped when it joined as a
		/// client; a demoted one goes back to following the published identity, which it must
		/// re-adopt before its provider snapshots count as describing the session's map.
		/// </summary>
		private void OnAuthorityChanged(bool isAuthority)
		{
			if (!SyncBus.Active)
				return;

			authoritySessionStarted = false;
			SessionMapAdopted = false;

			if (isAuthority)
				AuthoritySessionStart();
		}

		private void OnIdentitySynced()
		{
			if (SyncBus.Active && SyncBus.IsAuthority)
				AuthoritySessionStart();
			else
				TryAdopt();
		}

		private void OnIdentityChanged(MapIdentity _, MapIdentity __) => TryAdopt();

		// ------- adoption ----------------------------------------

		private void TryAdopt()
		{
			if (!SyncBus.Active || SyncBus.IsAuthority)
				return;

			MapIdentity identity = mapIdentity.Value;
			if (identity.id == Guid.Empty)
				return;

			string id = MapGuid.ToString(identity.id);
			string version = MapGuid.ToString(identity.version);
			string name = identity.name.ToString();
			bool firstAdopt = !SessionMapAdopted;
			SessionMapAdopted = true;

			GameMap current = owner.CurrentMap;

			if (current != null && current.id == id)
			{
				current.version = version;
				current.baseVersion = version;
				current.name = name;
				current.dirty = false;
				owner.Colocation.AdoptProviderState(current);

				if (firstAdopt)
					owner.Objects.RemoveAll();

				MapStore.Save(current);
				return;
			}

			if (current != null)
				owner.SaveCurrentMap();

			owner.SetCurrentMapSilently(null);
			owner.Objects.RemoveAll();

			GameMap adopted;
			if (MapStore.TryGet(id, out GameMap local))
			{
				if (local.dirty && local.version != version)
					MapStore.Fork(local);

				// The object list is deliberately left as it is. It is replaced by the first
				// snapshot taken once the session's objects arrive, and until then a stale list
				// is worth more than an empty one if this device never gets that far.
				adopted = local;
			}
			else
			{
				adopted = new GameMap { id = id };
			}

			adopted.name = name;
			adopted.version = version;
			adopted.baseVersion = version;
			adopted.dirty = false;
			adopted.lastUsed = DateTime.UtcNow.Ticks;

			owner.RebaseOnto(adopted);
			owner.Colocation.AdoptProviderState(adopted);
			MapStore.Save(adopted);
			owner.RaiseCurrentMapChanged();
		}
	}
}
