using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public sealed class MapSpaceStore
	{
		private readonly JsonDocumentStore<MapSpace> documents;
		public static MapSpaceStore Default { get; private set; } = new(Path.Combine(MapStore.CatalogRoot, "spaces"));
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init() => Default = new(Path.Combine(MapStore.CatalogRoot, "spaces"));
		public MapSpaceStore(string directory) => documents = new(directory, s => s.id, s => s.Clone(), s => s.Validate());
		public event Action Changed { add => documents.Changed += value; remove => documents.Changed -= value; }
		public bool IsAvailable => documents.IsAvailable;
		public bool TryGet(string id, out MapSpace space) => documents.Read(id, out space) == DocumentReadStatus.Found;
		public bool Save(MapSpace space) => documents.Save(space);
		public bool Delete(string id) => documents.Delete(id);
		public void Refresh() => documents.Refresh();
		public List<MapSpace> Spaces
		{
			get
			{
				var result = documents.All;
				result.Sort((a, b) => a.lastUsed == b.lastUsed ? string.CompareOrdinal(a.id, b.id) : b.lastUsed.CompareTo(a.lastUsed));
				return result;
			}
		}
		public string GenerateName()
		{
			int highest = 0;
			foreach (var space in Spaces)
				if (space.name != null && space.name.StartsWith("Space ") && int.TryParse(space.name.Substring(6), out int n)) highest = Math.Max(highest, n);
			return $"Space {highest + 1}";
		}
		public MapSpace FindOwner(string mapId)
		{
			foreach (var space in Spaces) if (space.mapIds.Contains(mapId)) return space;
			return null;
		}
		public MapSpace FindAssociation(string remoteId)
		{
			foreach (var space in Spaces)
				if (space.id == remoteId || space.associations.Exists(a => a.remoteSpaceId == remoteId)) return space;
			return null;
		}
		public bool IsAnchorReferenced(string guid)
		{
			if (!IsAvailable) return true; // Unreadable catalog entries may still own this native save.
			foreach (var space in Spaces) if (space.TryGetAnchor(guid, out _)) return true;
			return false;
		}
		/// <summary>Run after transaction recovery. Never prune uncertain reads or delete unreferenced files.</summary>
		public bool PruneMembership(MapStore maps)
		{
			HashSet<string> owned = new();
			bool success = true;
			foreach (MapSpace space in Spaces)
			{
				List<string> valid = new();
				foreach (string id in space.mapIds)
				{
					if (!MapSpace.ValidId(id) || owned.Contains(id)) continue;
					var status = maps.Read(id, out var map);
					if (status is DocumentReadStatus.Missing or DocumentReadStatus.Invalid) continue;
					if (map != null && map.storageFrameId != space.storageFrameId) continue;
					owned.Add(id); valid.Add(id);
				}
				if (valid.Count == space.mapIds.Count) continue;
				space.mapIds = valid;
				if (!Save(space)) success = false;
			}
			return success;
		}
	}
}
