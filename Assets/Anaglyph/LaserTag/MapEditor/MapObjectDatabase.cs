using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.MapEditor
{
	/// <summary>
	/// Every map object the game can place, grouped into the categories the palette shows.
	/// It is also what a saved map's prefab ids resolve through, so an object missing from
	/// here can neither be placed nor reloaded — one list, one truth.
	/// </summary>
	[CreateAssetMenu(fileName = "Map Objects", menuName = "Lasertag/Map Object Database")]
	public class MapObjectDatabase : ScriptableObject
	{
		[Serializable]
		public class Entry
		{
			[Tooltip("Fallback caption when no localized name is assigned. Falls back to the prefab's name.")]
			[SerializeField] private string displayName;
			[SerializeField] private LocalizedString localizedName;

			[Tooltip("Thumbnail on the palette button")]
			[SerializeField] private VectorImage icon;

			[SerializeField] private MapObject prefab;

			public VectorImage Icon => icon;
			public MapObject Prefab => prefab;

			public string DisplayName =>
				localizedName != null && !localizedName.IsEmpty ? localizedName.GetLocalizedString() :
				!string.IsNullOrWhiteSpace(displayName) ? displayName :
				prefab != null ? prefab.name : "Missing";
		}

		[Serializable]
		public class Category
		{
			[SerializeField] private string name = "Category";
			[SerializeField] private LocalizedString localizedName;

			[Tooltip("Icon on the category tab")]
			[SerializeField] private VectorImage icon;

			[Tooltip("Hidden from the palette unless debug mode is on")]
			[SerializeField] private bool debugOnly;

			[SerializeField] private List<Entry> objects = new();

			public string Name => localizedName != null && !localizedName.IsEmpty ? localizedName.GetLocalizedString() : name;
			public VectorImage Icon => icon;
			public bool DebugOnly => debugOnly;
			public IReadOnlyList<Entry> Objects => objects;
		}

		[SerializeField] private List<Category> categories = new();

		public IReadOnlyList<Category> Categories => categories;

		/// <summary>Resolves a saved <see cref="MapObject.PrefabId"/> back to its prefab.</summary>
		public MapObject FindPrefab(string prefabId)
		{
			if (string.IsNullOrEmpty(prefabId))
				return null;

			foreach (Category category in categories)
			{
				foreach (Entry entry in category.Objects)
				{
					MapObject prefab = entry.Prefab;

					if (prefab != null && prefab.PrefabId == prefabId)
						return prefab;
				}
			}

			return null;
		}

		// Two entries sharing a prefab id is silent corruption at load time: every saved
		// instance of either would come back as whichever one this database lists first.
		private void OnValidate()
		{
			HashSet<string> seen = new();

			foreach (Category category in categories)
			{
				foreach (Entry entry in category.Objects)
				{
					MapObject prefab = entry.Prefab;

					if (prefab == null || string.IsNullOrEmpty(prefab.PrefabId))
						continue;

					if (!seen.Add(prefab.PrefabId))
						Debug.LogError($"'{name}' lists prefab id '{prefab.PrefabId}' twice. " +
							"Saved maps cannot tell those objects apart.", this);
				}
			}
		}
	}
}
