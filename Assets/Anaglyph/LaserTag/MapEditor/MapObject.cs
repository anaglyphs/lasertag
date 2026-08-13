using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Maps;
using Anaglyph.Netcode.SyncVariables;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor
{
	public class MapObject : NetworkBehaviour
	{
		[SerializeField] private bool movable = true;
		public bool Movable => movable;

		// Stable across devices because it derives from the prefab asset's name, which ships
		// identically in every build. It is how a map file refers to this object's prefab.
		[SerializeField] private string prefabId;
		public string PrefabId => prefabId;

		// Child object hierarchy that only contains visual components
		[SerializeField] private GameObject visuals;
		public GameObject Visuals => visuals;

		/// <summary>Every live map object, local-only or network-spawned alike.</summary>
		public static IReadOnlyList<MapObject> All => all;
		private static readonly List<MapObject> all = new();

		public static event Action<MapObject> Added = delegate { };
		public static event Action<MapObject> Removed = delegate { };

		/// <summary>
		/// A deliberate edit performed on THIS device — place, move, delete. Remote peers'
		/// edits arrive through the network and do not raise this; only local edits make a
		/// map copy dirty (fork-on-edit tracks local divergence, not shared state).
		/// </summary>
		public static event Action LocalEditOccurred = delegate { };

		public static void NotifyLocalEdit() => LocalEditOccurred.Invoke();

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			all.Clear();
			Added = delegate { };
			Removed = delegate { };
			LocalEditOccurred = delegate { };
		}

		private bool shouldDelete;

		private void OnValidate()
		{
			if (string.IsNullOrEmpty(prefabId))
				prefabId = gameObject.name;
		}

		private void Awake()
		{
			NetworkObject.DontDestroyWithOwner = true;
			NetworkObject.DestroyWithScene = true;

			all.Add(this);
			Added.Invoke(this);
		}

		public override void OnDestroy()
		{
			all.Remove(this);
			Removed.Invoke(this);
			base.OnDestroy();
		}

		private void Start()
		{
			TrySpawn();
		}

		// Map objects belong to the session authority, so that one peer can clear and repopulate
		// the world when the map changes. Objects placed offline stay local; MapManager spawns
		// them when this device starts hosting. A client never spawns one of its own — its
		// placements are requests, and the object it gets back is the authority's.
		private void TrySpawn()
		{
			if (!NetworkObject.IsSpawned && NetworkManager.IsConnectedClient && SyncBus.IsAuthority)
				NetworkObject.Spawn();
		}

		/// <summary>Network-spawns a local-only object. Host-side, at session start.</summary>
		public void SpawnIfLocal()
		{
			TrySpawn();
		}

		public bool IsLocalOnly => !NetworkObject.IsSpawned;

		/// <summary>
		/// Removes this object as far as this device is permitted to, for teardown flows that
		/// clear the world (unloading a map, adopting or switching the session's map).
		/// Local-only objects — and anything left over once the session is gone — are destroyed
		/// outright; a spawned object this peer has authority over is despawned, which removes
		/// it for everyone.
		///
		/// The one object the authority does not already own is one a peer is currently holding,
		/// since a grab takes ownership for its duration. Those are claimed back and despawned
		/// when ownership lands, so a clear still finishes — just not within this call.
		/// </summary>
		/// <returns>Whether the object was removed by the time this returned.</returns>
		public bool RemoveIfPermitted()
		{
			NetworkManager manager = NetworkManager.Singleton;
			bool sessionLive = manager != null && manager.IsListening && !manager.ShutdownInProgress;

			if (!sessionLive || !NetworkObject.IsSpawned)
			{
				Destroy(gameObject);
				return true;
			}

			if (NetworkObject.HasAuthority)
			{
				NetworkObject.Despawn();
				return true;
			}

			if (SyncBus.IsAuthority)
			{
				shouldDelete = true;
				TryTakeOwnership();
			}

			return false;
		}

		public void TryTakeOwnership()
		{
			if (NetworkManager.IsConnectedClient)
			{
				if (NetworkObject.IsOwnershipRequestRequired)
					NetworkObject.RequestOwnership();
				else
					NetworkObject.ChangeOwnership(NetworkManager.LocalClientId);
			}
		}

		/// <summary>
		/// Hands a held object back to the session authority. Ownership only leaves the
		/// authority for the duration of a grab; leaving it with the grabber would mean the
		/// authority could no longer clear the world to load a different map.
		/// </summary>
		public void ReleaseOwnership()
		{
			if (!NetworkObject.IsSpawned || !SyncBus.Active || !NetworkObject.IsOwner)
				return;

			ulong authority = SyncBus.Current.OwnerClientId;
			if (NetworkObject.OwnerClientId != authority)
				NetworkObject.ChangeOwnership(authority);
		}

		/// <summary>
		/// Removes this object for everyone. Offline that is a plain destroy; in a session it
		/// is a request to the authority, which owns every spawned map object.
		/// </summary>
		public bool TryDelete()
		{
			// Routed through the manager offline as well as in a session: it owns the rule about
			// when the map may be edited, and a local destroy is just as much of an edit.
			if (MapManager.Instance != null)
				return MapManager.Instance.RequestRemoveObject(this);

			if (!NetworkManager.IsConnectedClient)
			{
				Destroy(gameObject);
				return true;
			}

			return false;
		}

		public bool CanManage()
		{
			return !NetworkManager.IsConnectedClient || NetworkObject.IsOwner;
		}

		protected override void OnOwnershipChanged(ulong previous, ulong current)
		{
			if (shouldDelete && current == NetworkManager.LocalClientId)
				NetworkObject.Despawn();
		}
	}
}
