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
	/// Projects object placements into the scene and captures the authority's authored world.
	/// Retired objects are excluded immediately, even when NGO ownership delays their despawn.
	/// Clients persist the complete document from MapSessionSync, never a partial spawned scene.
	/// </summary>
	internal sealed class MapObjectDirector
	{
		private struct Placement
		{
			public Guid mapId;
			public Guid context;
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		private struct ObjectRequest
		{
			public Guid mapId;
			public Guid context;
			public ulong objectId;
			public Pose pose;
		}

		private readonly MapObjectDatabase database;
		private readonly Action contentChanged;
		private readonly Func<bool> canEdit;
		private readonly SyncEvent<Placement> placeRequest = new("map.object.place", EventRoute.ToAuthority);
		private readonly SyncEvent<ObjectRequest> removeRequest = new("map.object.remove", EventRoute.ToAuthority);
		private readonly SyncEvent<ObjectRequest> moveRequest = new("map.object.move", EventRoute.ToAuthority);
		private readonly HashSet<MapObject> retired = new();
		private readonly List<MapObjectEntry> unresolved = new();
		private Guid mapId;
		private Guid context;
		private readonly Func<MapSpaceFrame> frame;

		public MapObjectDirector(MapObjectDatabase database, Action contentChanged, Func<bool> canEdit, Func<MapSpaceFrame> frame)
		{
			this.database = database;
			this.contentChanged = contentChanged;
			this.canEdit = canEdit;
			this.frame = frame;
		}

		public void SetContext(string id, Guid operation) { Guid.TryParse(id, out mapId); context = operation; }
		public bool IsReplacementComplete => retired.Count == 0;
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
		public bool RequestPlace(MapObject prefab, Vector3 position, Quaternion rotation)
		{
			if (!prefab || string.IsNullOrEmpty(prefab.PrefabId))
				return false;
			if (!SyncBus.Active)
			{
				UnityEngine.Object.Instantiate(prefab.gameObject, position, rotation);
				contentChanged();
				return true;
			}
			Placement placement = new() { mapId = mapId, context = context, pose = new Pose(position, rotation) };
			placement.prefabId.CopyFromTruncated(prefab.PrefabId);
			placeRequest.Raise(placement);
			return true;
		}

		public bool RequestRemove(MapObject obj)
		{
			if (!obj || retired.Contains(obj))
				return false;
			if (!SyncBus.Active || !obj.NetworkObject.IsSpawned)
			{
				Retire(obj);
				contentChanged();
				return true;
			}
			removeRequest.Raise(new ObjectRequest { mapId = mapId, context = context, objectId = obj.NetworkObject.NetworkObjectId });
			return true;
		}

		public void RequestMove(MapObject obj)
		{
			if (!obj || retired.Contains(obj))
				return;
			if (!SyncBus.Active)
			{
				contentChanged();
				return;
			}
			moveRequest.Raise(new ObjectRequest
			{
				mapId = mapId, context = context, objectId = obj.NetworkObject.NetworkObjectId,
				pose = new Pose(obj.transform.position, obj.transform.rotation)
			});
		}

		private void OnPlaceRequested(ulong sender, Placement placement)
		{
			if (placement.mapId != mapId || placement.context != context || !canEdit() || !Finite(placement.pose))
				return;
			MapObject prefab = database ? database.FindPrefab(placement.prefabId.ToString()) : null;
			if (!prefab || !NetworkManager.Singleton)
				return;
			NetworkObject.InstantiateAndSpawn(prefab.gameObject, NetworkManager.Singleton,
				ownerClientId: SyncBus.LocalClientId, position: placement.pose.position, rotation: placement.pose.rotation);
			contentChanged();
		}

		private bool TryResolve(ObjectRequest request, out MapObject obj)
		{
			obj = null;
			NetworkManager manager = NetworkManager.Singleton;
			return request.mapId == mapId && request.context == context && canEdit() && manager && manager.SpawnManager != null &&
				manager.SpawnManager.SpawnedObjects.TryGetValue(request.objectId, out NetworkObject spawned) &&
				spawned.TryGetComponent(out obj) && !retired.Contains(obj);
		}

		private void OnRemoveRequested(ulong sender, ObjectRequest request)
		{
			if (!TryResolve(request, out MapObject obj))
				return;
			Retire(obj);
			contentChanged();
		}

		private void OnMoveRequested(ulong sender, ObjectRequest request)
		{
			if (!TryResolve(request, out MapObject obj) || !Finite(request.pose))
				return;
			obj.transform.SetPositionAndRotation(request.pose.position, request.pose.rotation);
			contentChanged();
		}

		private static bool Finite(Pose pose) =>
			Finite(pose.position.x) && Finite(pose.position.y) && Finite(pose.position.z) &&
			Finite(pose.rotation.x) && Finite(pose.rotation.y) && Finite(pose.rotation.z) && Finite(pose.rotation.w);
		private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		public bool TryCapture(out List<MapObjectEntry> result)
		{
			result = null;
			NetworkManager manager = NetworkManager.Singleton;
			if (manager && manager.ShutdownInProgress)
				return false;
			result = Capture(false);
			return true;
		}

		public List<MapObjectEntry> CaptureLocal() => Capture(localOnly: true);

		private List<MapObjectEntry> Capture(bool localOnly)
		{
			List<MapObjectEntry> result = new(unresolved);
			foreach (MapObject obj in MapObject.All)
			{
				if (!obj || retired.Contains(obj) || (localOnly && !obj.IsLocalOnly) || string.IsNullOrEmpty(obj.PrefabId))
					continue;
				result.Add(new MapObjectEntry { prefabId = obj.PrefabId, pose = frame().ToStorage(new Pose(obj.transform.position, obj.transform.rotation)) });
			}
			return result;
		}

		public void Replace(IReadOnlyList<MapObjectEntry> placements)
		{
			// Retire before instantiating so callbacks cannot capture a mixture of both maps.
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj)
					Retire(obj);
			unresolved.Clear();
			foreach (MapObjectEntry entry in placements)
			{
				MapObject prefab = database ? database.FindPrefab(entry.prefabId) : null;
				if (!prefab)
				{
					unresolved.Add(entry);
					Debug.LogWarning($"Map references unknown prefab '{entry.prefabId}'; preserving its saved placement.");
					continue;
				}
				Pose pose = frame().ToCanonical(entry.pose);
				UnityEngine.Object.Instantiate(prefab.gameObject, pose.position, pose.rotation);
			}
		}

		public void RemoveLocalObjects()
		{
			unresolved.Clear();
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj && obj.IsLocalOnly)
					Retire(obj);
		}

		private void Retire(MapObject obj)
		{
			if (!retired.Add(obj))
				return;
			obj.RemoveIfPermitted();
		}

		public void SpawnLocalObjects()
		{
			foreach (MapObject obj in new List<MapObject>(MapObject.All))
				if (obj && !retired.Contains(obj))
					obj.SpawnIfLocal();
		}
	}
}
