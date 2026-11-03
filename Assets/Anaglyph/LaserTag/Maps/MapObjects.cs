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
	/// The shown map's objects in the scene: placements become instances, and each edit captures
	/// the scene back into the map. Objects leaving through session or scene teardown are not
	/// edits, so they never reach the map. Records whose prefab this build does not know survive
	/// capture, so loading an older map never silently drops them. Edit requests carry the map
	/// and its revision, so a request from a previous map is refused.
	/// </summary>
	public sealed class MapObjects
	{
		private struct Placement
		{
			public Guid mapId;
			public Guid revision;
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		private struct ObjectRequest
		{
			public Guid mapId;
			public Guid revision;
			public ulong objectId;
			public Pose pose;
		}

		private readonly SpaceManager manager;
		private readonly MapObjectDatabase database;
		private readonly SyncEvent<Placement> placeRequest = new("map.object.place", EventRoute.ToAuthority);
		private readonly SyncEvent<ObjectRequest> removeRequest = new("map.object.remove", EventRoute.ToAuthority);
		private readonly SyncEvent<ObjectRequest> moveRequest = new("map.object.move", EventRoute.ToAuthority);
		private readonly HashSet<MapObject> retired = new();
		private readonly List<MapObjectRecord> unresolved = new();

		public MapObjects(SpaceManager manager, MapObjectDatabase database)
		{
			this.manager = manager;
			this.database = database;
		}

		public bool IsReplacementComplete => retired.Count == 0;

		/// <summary>Editing needs the shown map in a trusted frame; an operator edits a space it hosts without a rig.</summary>
		public bool CanEdit => manager.Map != null && !manager.IsChangingMap &&
			(manager.Alignment.IsAligned || HeadsetConfiguration.IsOperatorDevice && manager.Role != SpaceRole.Client);

		private Guid MapId => manager.Map?.Id ?? Guid.Empty;

		public void Register()
		{
			placeRequest.Register();
			removeRequest.Register();
			moveRequest.Register();
			placeRequest.Received += OnPlaceRequested;
			removeRequest.Received += OnRemoveRequested;
			moveRequest.Received += OnMoveRequested;
			MapObject.Removed += OnRemoved;
		}

		public void Unregister()
		{
			MapObject.Removed -= OnRemoved;
			placeRequest.Received -= OnPlaceRequested;
			removeRequest.Received -= OnRemoveRequested;
			moveRequest.Received -= OnMoveRequested;
			placeRequest.Unregister();
			removeRequest.Unregister();
			moveRequest.Unregister();
		}

		private void OnRemoved(MapObject obj) => retired.Remove(obj);

		// ------- edit requests -------------------------------------

		public bool RequestPlace(MapObject prefab, Vector3 position, Quaternion rotation)
		{
			if (!CanEdit || !prefab || string.IsNullOrEmpty(prefab.PrefabId)) return false;
			if (!SyncBus.Active)
			{
				UnityEngine.Object.Instantiate(prefab.gameObject, position, rotation);
				manager.ObjectsEdited();
				return true;
			}
			Placement placement = new() { mapId = MapId, revision = manager.MapRevision, pose = new Pose(position, rotation) };
			placement.prefabId.CopyFromTruncated(prefab.PrefabId);
			placeRequest.Raise(placement);
			return true;
		}

		public bool RequestRemove(MapObject obj)
		{
			if (!CanEdit || !obj || retired.Contains(obj)) return false;
			if (!SyncBus.Active || !obj.NetworkObject.IsSpawned)
			{
				Retire(obj);
				manager.ObjectsEdited();
				return true;
			}
			removeRequest.Raise(new ObjectRequest { mapId = MapId, revision = manager.MapRevision, objectId = obj.NetworkObject.NetworkObjectId });
			return true;
		}

		public void CommitMove(MapObject obj)
		{
			if (!CanEdit || !obj || retired.Contains(obj)) return;
			if (!SyncBus.Active)
			{
				manager.ObjectsEdited();
				return;
			}
			moveRequest.Raise(new ObjectRequest
			{
				mapId = MapId, revision = manager.MapRevision, objectId = obj.NetworkObject.NetworkObjectId,
				pose = new Pose(obj.transform.position, obj.transform.rotation)
			});
		}

		private bool Current(Guid mapId, Guid revision) => mapId == MapId && revision == manager.MapRevision && CanEdit;

		private void OnPlaceRequested(ulong sender, Placement placement)
		{
			if (!Current(placement.mapId, placement.revision) || !Space.ValidPose(placement.pose)) return;
			MapObject prefab = database.FindPrefab(placement.prefabId.ToString());
			if (!prefab || !NetworkManager.Singleton) return;
			NetworkObject.InstantiateAndSpawn(prefab.gameObject, NetworkManager.Singleton,
				ownerClientId: SyncBus.LocalClientId, position: placement.pose.position, rotation: placement.pose.rotation);
			manager.ObjectsEdited();
		}

		private bool TryResolve(ObjectRequest request, out MapObject obj)
		{
			obj = null;
			NetworkManager network = NetworkManager.Singleton;
			return Current(request.mapId, request.revision) && network && network.SpawnManager != null &&
				network.SpawnManager.SpawnedObjects.TryGetValue(request.objectId, out NetworkObject spawned) &&
				spawned.TryGetComponent(out obj) && !retired.Contains(obj);
		}

		private void OnRemoveRequested(ulong sender, ObjectRequest request)
		{
			if (!TryResolve(request, out MapObject obj)) return;
			Retire(obj);
			manager.ObjectsEdited();
		}

		private void OnMoveRequested(ulong sender, ObjectRequest request)
		{
			if (!TryResolve(request, out MapObject obj) || !Space.ValidPose(request.pose)) return;
			obj.transform.SetPositionAndRotation(request.pose.position, request.pose.rotation);
			manager.ObjectsEdited();
		}

		// ------- scene <-> records ---------------------------------

		public List<MapObjectRecord> Capture()
		{
			List<MapObjectRecord> result = new(unresolved);
			foreach (MapObject obj in MapObject.All)
			{
				if (!obj || retired.Contains(obj) || string.IsNullOrEmpty(obj.PrefabId)) continue;
				result.Add(new MapObjectRecord { prefabId = obj.PrefabId, pose = new Pose(obj.transform.position, obj.transform.rotation) });
			}
			return result;
		}

		public void Replace(IReadOnlyList<MapObjectRecord> records)
		{
			// Retire before instantiating so callbacks cannot capture a mixture of both maps.
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj) Retire(obj);
			unresolved.Clear();
			foreach (MapObjectRecord record in records)
			{
				MapObject prefab = database.FindPrefab(record.prefabId);
				if (!prefab)
				{
					unresolved.Add(record);
					Debug.LogWarning($"[{nameof(MapObjects)}] the map references unknown prefab '{record.prefabId}': its placement is kept");
					continue;
				}
				UnityEngine.Object.Instantiate(prefab.gameObject, record.pose.position, record.pose.rotation);
			}
		}

		public void RemoveLocalObjects()
		{
			unresolved.Clear();
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj && obj.IsLocalOnly) Retire(obj);
		}

		private void Retire(MapObject obj)
		{
			if (retired.Add(obj)) obj.RemoveIfPermitted();
		}

		/// <summary>Network-spawns the objects placed offline, when this device starts hosting.</summary>
		public void SpawnLocalObjects()
		{
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj && !retired.Contains(obj)) obj.SpawnIfLocal();
		}
	}
}
