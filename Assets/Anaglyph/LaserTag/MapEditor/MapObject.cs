using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class MapObject : NetworkBehaviour
	{
		[SerializeField] private bool movable = true;
		public bool Movable => movable;

		// Stable across devices because it derives from the prefab asset's name, which ships
		// identically in every build. It is how a map file refers to this object's prefab.
		[SerializeField] private string prefabId;
		public string PrefabId => prefabId;

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

		// Objects placed while a session is up network-spawn immediately. Objects placed
		// offline stay local; MapManager spawns them when this device starts hosting.
		private void TrySpawn()
		{
			if (!NetworkObject.IsSpawned && NetworkManager.IsConnectedClient)
				NetworkObject.Spawn();
		}

		/// <summary>Network-spawns a local-only object. Host-side, at session start.</summary>
		public void SpawnIfLocal()
		{
			TrySpawn();
		}

		public bool IsLocalOnly => !NetworkObject.IsSpawned;

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

		public void TryDelete()
		{
			if (!NetworkManager.IsConnectedClient)
			{
				Destroy(gameObject);
				return;
			}

			shouldDelete = true;

			if (NetworkObject.IsOwner)
				NetworkObject.Despawn();
			else
				TryTakeOwnership();
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
