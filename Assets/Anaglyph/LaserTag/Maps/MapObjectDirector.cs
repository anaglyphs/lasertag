using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.Netcode.SyncVariables;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// The map's gameplay objects: placing and removing them across the session, populating the
	/// world from a loaded map, and snapshotting the world back into one.
	///
	/// Only the session authority spawns map objects — that is what lets one peer clear and
	/// repopulate the world when the map changes — so in a session a placement is a request, and
	/// offline it is a plain instantiate.
	/// </summary>
	internal sealed class MapObjectDirector
	{
		private struct MapObjectPlacement
		{
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		private readonly MapObjectDatabase database;
		private readonly Action contentChanged;

		private readonly SyncEvent<MapObjectPlacement> placeRequest =
			new("map.object.place", EventRoute.ToAuthority);
		private readonly SyncEvent<ulong> removeRequest =
			new("map.object.remove", EventRoute.ToAuthority);

		private readonly List<MapObject> objectScratch = new();

		private bool isQuitting;

		/// <param name="contentChanged">Raised when this device's authority acts on a peer's
		/// place or remove request, which diverges the authority's copy of the map.</param>
		public MapObjectDirector(MapObjectDatabase database, Action contentChanged)
		{
			this.database = database;
			this.contentChanged = contentChanged ?? throw new ArgumentNullException(nameof(contentChanged));
		}

		public void Register()
		{
			placeRequest.Register();
			placeRequest.Received += OnPlaceRequested;
			removeRequest.Register();
			removeRequest.Received += OnRemoveRequested;
		}

		public void Unregister()
		{
			removeRequest.Received -= OnRemoveRequested;
			removeRequest.Unregister();
			placeRequest.Received -= OnPlaceRequested;
			placeRequest.Unregister();
		}

		/// <summary>
		/// Map objects are still alive during OnApplicationQuit, so that is the last save which
		/// can record them; every save after it runs against a world being torn down.
		/// </summary>
		public void MarkQuitting() => isQuitting = true;

		// ------- placement ---------------------------------------

		public void RequestPlace(MapObject prefab, Vector3 position, Quaternion rotation)
		{
			if (prefab == null)
				return;

			if (!SyncBus.Active)
			{
				UnityEngine.Object.Instantiate(prefab.gameObject, position, rotation);
				return;
			}

			if (string.IsNullOrEmpty(prefab.PrefabId))
			{
				Debug.LogError($"Map object prefab '{prefab.name}' has no prefab id, so no peer " +
					"can resolve it.", prefab);
				return;
			}

			MapObjectPlacement placement = new() { pose = new Pose(position, rotation) };
			placement.prefabId.CopyFromTruncated(prefab.PrefabId);
			placeRequest.Raise(placement);
		}

		public void RequestRemove(MapObject obj)
		{
			if (obj == null)
				return;

			// Local-only leftovers never reached the network, so no peer has to be told.
			if (!SyncBus.Active || !obj.NetworkObject.IsSpawned)
			{
				UnityEngine.Object.Destroy(obj.gameObject);
				return;
			}

			removeRequest.Raise(obj.NetworkObject.NetworkObjectId);
		}

		private void OnPlaceRequested(ulong sender, MapObjectPlacement placement)
		{
			if (database == null || NetworkManager.Singleton == null)
				return;

			string prefabId = placement.prefabId.ToString();
			MapObject prefab = database.FindPrefab(prefabId);
			if (prefab == null)
			{
				Debug.LogWarning($"Peer {sender} asked to place unknown prefab '{prefabId}'.");
				return;
			}

			NetworkObject.InstantiateAndSpawn(prefab.gameObject, NetworkManager.Singleton,
				ownerClientId: SyncBus.LocalClientId,
				position: placement.pose.position, rotation: placement.pose.rotation);

			contentChanged();
		}

		private void OnRemoveRequested(ulong sender, ulong networkObjectId)
		{
			NetworkManager manager = NetworkManager.Singleton;
			if (manager == null || manager.SpawnManager == null)
				return;

			if (!manager.SpawnManager.SpawnedObjects.TryGetValue(
				    networkObjectId, out NetworkObject spawned))
				return;

			// Only map objects are removable this way; the id arrives from a peer and would
			// otherwise be a despawn primitive for any NetworkObject in the session.
			if (!spawned.TryGetComponent(out MapObject mapObject))
			{
				Debug.LogWarning($"Peer {sender} asked to remove a non-map object.");
				return;
			}

			mapObject.RemoveIfPermitted();
			contentChanged();
		}

		// ------- world <-> map -----------------------------------

		/// <summary>
		/// Whether <see cref="MapObject.All"/> currently describes the world. It does not while
		/// the app is quitting or a network shutdown is despawning objects: the list empties
		/// through teardown rather than through an edit, and snapshotting it then would persist
		/// a map with no objects in it.
		/// </summary>
		public bool CanSnapshot
		{
			get
			{
				if (isQuitting)
					return false;

				NetworkManager manager = NetworkManager.Singleton;
				return manager == null || !manager.ShutdownInProgress;
			}
		}

		public void Snapshot(GameMap map)
		{
			if (map == null)
				return;

			// A client's world is populated entirely by the authority's spawns, so an empty one
			// nearly always means they have not arrived yet — during a join above all, where the
			// world is deliberately cleared first. Overwriting a saved list with that would empty
			// this device's copy of the map. The next snapshot with objects in it corrects a map
			// the host really did empty.
			if (MapObject.All.Count == 0 && map.objects.Count > 0 &&
			    SyncBus.Active && !SyncBus.IsAuthority)
				return;

			map.objects.Clear();
			foreach (MapObject obj in MapObject.All)
			{
				if (!obj)
					continue;

				if (string.IsNullOrEmpty(obj.PrefabId))
				{
					Debug.LogWarning($"Map object '{obj.name}' has no prefab id; not saving it.");
					continue;
				}

				Transform transform = obj.transform;
				map.objects.Add(new MapObjectEntry
				{
					prefabId = obj.PrefabId,
					pose = new Pose(transform.position, transform.rotation),
				});
			}
		}

		public void Instantiate(GameMap map)
		{
			if (map == null)
				return;

			foreach (MapObjectEntry entry in map.objects)
			{
				MapObject prefab = database != null ? database.FindPrefab(entry.prefabId) : null;

				if (prefab == null)
				{
					Debug.LogWarning($"Map references unknown prefab '{entry.prefabId}'.");
					continue;
				}

				UnityEngine.Object.Instantiate(
					prefab.gameObject, entry.pose.position, entry.pose.rotation);
			}
		}

		public void RemoveAll()
		{
			objectScratch.Clear();
			objectScratch.AddRange(MapObject.All);

			foreach (MapObject obj in objectScratch)
				if (obj)
					obj.RemoveIfPermitted();

			objectScratch.Clear();
		}

		/// <summary>Network-spawns the objects placed offline, as this device starts hosting.</summary>
		public void SpawnLocalObjects()
		{
			objectScratch.Clear();
			objectScratch.AddRange(MapObject.All);

			foreach (MapObject obj in objectScratch)
				if (obj)
					obj.SpawnIfLocal();

			objectScratch.Clear();
		}
	}
}
