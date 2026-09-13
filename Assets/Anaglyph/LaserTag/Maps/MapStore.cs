using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Separate layout JSON files. Alignment records are owned by MapSpaceStore.</summary>
	public sealed class MapStore
	{
		internal static string CatalogRoot => Path.Combine(Application.persistentDataPath, "map-catalog-v2");
		private readonly JsonDocumentStore<GameMap> documents;
		public static MapStore Default { get; private set; } = new(Path.Combine(CatalogRoot, "maps"));
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init() => Default = new(Path.Combine(CatalogRoot, "maps"));
		public MapStore(string directoryPath) => documents = new(directoryPath, m => m.id, m => m.Clone(), m => m.Validate());
		public event Action Changed { add => documents.Changed += value; remove => documents.Changed -= value; }
		public IReadOnlyCollection<GameMap> Maps => GetByLastUsed();
		public DocumentReadStatus Read(string id, out GameMap map) => documents.Read(id, out map);
		public bool TryGet(string id, out GameMap map) => Read(id, out map) == DocumentReadStatus.Found;
		public bool Save(GameMap map) => documents.Save(map);
		public bool Delete(string id) => documents.Delete(id);
		public void Refresh() => documents.Refresh();
		public List<GameMap> GetByLastUsed()
		{
			var result = documents.All;
			result.Sort((a, b) => a.lastUsed == b.lastUsed ? string.CompareOrdinal(a.id, b.id) : b.lastUsed.CompareTo(a.lastUsed));
			return result;
		}
		public string GenerateName()
		{
			int highest = 0;
			foreach (var map in documents.All)
				if (map.name != null && map.name.StartsWith("Map ") && int.TryParse(map.name.Substring(4), out int n)) highest = Math.Max(highest, n);
			return $"Map {highest + 1}";
		}
	}
}
