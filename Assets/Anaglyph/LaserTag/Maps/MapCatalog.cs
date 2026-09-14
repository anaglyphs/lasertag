using System;
using System.Collections.Generic;
using System.Linq;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	public enum CatalogOperationStatus { Completed, Pending, Rejected }

	public readonly struct CatalogOperationResult
	{
		public CatalogOperationStatus Status { get; }
		public bool IsCompleted => Status == CatalogOperationStatus.Completed;
		public bool IsPending => Status == CatalogOperationStatus.Pending;
		public CatalogOperationResult(CatalogOperationStatus status) => Status = status;
	}

	internal enum CatalogChangeKind { Membership, MapCreated, SpaceActivated, MapDeleted, SpaceDeleted }

	internal sealed class CatalogChange
	{
		public CatalogChangeKind Kind { get; }
		public MapSpace Space { get; }
		public GameMap Map { get; }
		public string DeletedMapId { get; }
		public Guid MapContext { get; }
		public Guid ReferenceContext { get; }

		public CatalogChange(CatalogChangeKind kind, MapSpace space, GameMap map, string deletedMapId, MapVisitContext visit)
		{
			Kind = kind;
			Space = space?.Clone();
			Map = map?.Clone();
			DeletedMapId = deletedMapId;
			MapContext = visit.MapContext;
			ReferenceContext = visit.ReferenceContext;
		}
	}

	/// <summary>Owns catalog writes, their durable completion and unused native-save cleanup.</summary>
	internal sealed class MapCatalog
	{
		private readonly MapVisitContext visit;
		private readonly HashSet<string> anchorsToErase = new();
		private CatalogChange pendingChange;
		public MapWorkingCopy MapDocument { get; }
		public MapSpaceWorkingCopy SpaceDocument { get; }
		public MapCatalogJournal Journal { get; }
		public MapStore MapStore { get; }
		public MapSpaceStore SpaceStore { get; }
		public string DirectoryPath { get; }
		public bool HasPending => pendingChange != null || Journal.HasPending;
		public event Action<CatalogChange> Committed = delegate { };
		public event Action RetryRequested = delegate { };

		public MapCatalog(MapStore mapStore, MapSpaceStore spaceStore, string directory, MapVisitContext visit)
		{
			MapStore = mapStore;
			SpaceStore = spaceStore;
			DirectoryPath = directory;
			this.visit = visit;
			MapDocument = new(mapStore);
			SpaceDocument = new(spaceStore);
			Journal = new(mapStore, spaceStore, directory);
			if (Journal.Recover()) spaceStore.PruneMembership(mapStore);
		}

		public CatalogOperationResult NewMap()
		{
			var space = SpaceDocument.CurrentSpace;
			if (HasPending || space == null) return Rejected();
			var map = NewLayout(space);
			space.mapIds.Add(map.id);
			return Commit(Change(CatalogChangeKind.MapCreated, space, map), new[] { map });
		}

		public CatalogOperationResult DuplicateMap(string id)
		{
			var space = SpaceDocument.CurrentSpace;
			if (HasPending || space == null || !MapStore.TryGet(id, out var source)) return Rejected();
			var owner = SpaceStore.FindOwner(id);
			if (owner == null) return Rejected();
			var copy = source.Clone();
			copy.id = Guid.NewGuid().ToString("N");
			copy.version = Guid.NewGuid().ToString("N");
			copy.name += " (copy)";
			copy.storageFrameId = space.storageFrameId;
			copy.objects = source.objects.ConvertAll(entry => new MapObjectEntry
			{
				prefabId = entry.prefabId,
				pose = owner.canonicalFrameId == space.canonicalFrameId
					? space.Frame.ToStorage(owner.Frame.ToCanonical(entry.pose)) : entry.pose
			});
			space.mapIds.Add(copy.id);
			return Commit(Change(CatalogChangeKind.Membership, space, copy), new[] { copy });
		}

		public CatalogOperationResult CreateSpace(bool automatic, Method initialMethod = Method.MetaSharedAnchor)
		{
			if (HasPending) return Rejected();
			var space = MapSpace.Create(SpaceStore.GenerateName());
			space.automaticallyCreated = automatic;
			space.initializationPending = true;
			space.preferredColocationMethod = initialMethod;
			if (automatic) MapSpaceStartup.ConfigureDraft(space, initialMethod);
			return ActivateSpaceWithNewMap(space);
		}

		public CatalogOperationResult ActivateSpaceWithNewMap(MapSpace space)
		{
			if (HasPending || space == null) return Rejected();
			space = space.Clone();
			var map = NewLayout(space);
			space.mapIds.Add(map.id);
			return Commit(Change(CatalogChangeKind.SpaceActivated, space, map), new[] { map });
		}

		public CatalogOperationResult DeleteMap(string id)
		{
			if (HasPending) return Rejected();
			var owner = SpaceStore.FindOwner(id);
			if (owner == null) return Rejected();
			owner.mapIds.Remove(id);
			return Commit(Change(CatalogChangeKind.MapDeleted, owner, deletedMapId: id), deletions: new[] { id });
		}

		public CatalogOperationResult DeleteSpace(string id)
		{
			if (HasPending || !SpaceStore.TryGet(id, out var space)) return Rejected();
			var deletions = space.mapIds.ToArray();
			space.mapIds.Clear();
			return Commit(Change(CatalogChangeKind.SpaceDeleted, space), deletions: deletions, deleteSpace: true);
		}

		public GameMap NewLayout(MapSpace space) => new()
		{
			id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"),
			storageFrameId = space.storageFrameId, name = MapStore.GenerateName(),
			lastUsed = DateTime.UtcNow.Ticks, lastEdited = DateTime.UtcNow.Ticks
		};

		public GameMap SelectMap(MapSpace space, string id = null)
		{
			if (space == null) return null;
			if (id != null && space.mapIds.Contains(id) && MapStore.TryGet(id, out var selected)) return selected;
			return MapStore.GetByLastUsed().FirstOrDefault(map => space.mapIds.Contains(map.id));
		}

		private CatalogChange Change(CatalogChangeKind kind, MapSpace space, GameMap map = null, string deletedMapId = null) =>
			new(kind, space, map, deletedMapId, visit);

		public CatalogOperationResult Commit(CatalogChange change, IReadOnlyList<GameMap> writes = null,
			IReadOnlyList<string> deletions = null, bool deleteSpace = false)
		{
			if (HasPending || change == null) return Rejected();
			pendingChange = change;
			if (Journal.Commit(change.Space, writes, deletions, deleteSpace))
			{
				CompletePendingChange();
				return new(CatalogOperationStatus.Completed);
			}
			if (!Journal.HasPending)
			{
				pendingChange = null;
				return Rejected();
			}
			RetryRequested.Invoke();
			return new(CatalogOperationStatus.Pending);
		}

		public bool RetryPending()
		{
			if (!Journal.Recover())
			{
				RetryRequested.Invoke();
				return false;
			}
			CompletePendingChange();
			return true;
		}

		private void CompletePendingChange()
		{
			var change = pendingChange;
			pendingChange = null;
			if (change == null) return;
			if (change.Kind == CatalogChangeKind.SpaceDeleted)
				foreach (var anchor in change.Space.AllAnchors()) QueueAnchorErasure(anchor.guid);
			Committed.Invoke(change);
		}

		public bool SaveDocuments(bool commitShared)
		{
			if (HasPending) return false;
			return MapDocument.Save(commitShared) && SpaceDocument.Save(commitShared);
		}

		public void QueueAnchorErasure(string guid) => anchorsToErase.Add(guid);

		public void EraseUnreferencedAnchors(Action<string> erase)
		{
			if (HasPending || !SpaceStore.IsAvailable) return;
			foreach (var guid in anchorsToErase)
				if (!SpaceStore.IsAnchorReferenced(guid)) erase(guid);
			anchorsToErase.Clear();
		}

		private static CatalogOperationResult Rejected() => new(CatalogOperationStatus.Rejected);
	}
}
