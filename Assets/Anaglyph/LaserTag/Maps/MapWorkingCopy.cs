using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Owns the editable map document. Reads return detached snapshots; only these operations
	/// change the document. Networking, scene objects, alignment and UI are coordinated above it.
	/// Alignment and membership are owned by MapSpaceWorkingCopy; this document contains only layout data.
	/// </summary>
	public sealed class MapWorkingCopy
	{
		private readonly MapStore store;
		private GameMap current;
		private string savedJson;

		public MapWorkingCopy(MapStore store) => this.store = store;
		public GameMap CurrentMap => current?.Clone();
		public string CurrentId => current?.id;
		public bool HasMap => current != null;
		public bool IsEmpty => current == null || current.IsEmpty;

		public void Load(GameMap map) { current = map?.Clone(); savedJson = current == null ? null : JsonUtility.ToJson(current); }
		public void Unload() => current = null;

		public GameMap Create(string storageFrameId)
		{
			savedJson = null;
			current = new GameMap
			{
				id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"),
				name = store.GenerateName(), storageFrameId = storageFrameId,
				lastUsed = DateTime.UtcNow.Ticks, lastEdited = DateTime.UtcNow.Ticks
			};
			return CurrentMap;
		}

		public void MarkUsed()
		{
			if (current != null)
				current.lastUsed = DateTime.UtcNow.Ticks;
		}

		private void ContentChanged()
		{
			current.version = Guid.NewGuid().ToString("N");
			current.lastEdited = DateTime.UtcNow.Ticks;
			current.dirty = true;
		}

		public bool Rename(string name)
		{
			if (current == null || current.name == name)
				return false;
			current.name = name;
			ContentChanged();
			return true;
		}

		public bool SetObjects(IReadOnlyList<MapObjectEntry> objects)
		{
			if (current == null || SamePlacements(current.objects, objects))
				return false;
			current.objects = new List<MapObjectEntry>(objects);
			ContentChanged();
			return true;
		}

		/// <summary>Commit shared content only after its exact snapshot reaches storage.</summary>
		public bool Save(bool commitSharedContent = false)
		{
			if (current == null)
				return true;
			GameMap snapshot = current.Clone();
			if (commitSharedContent)
				snapshot.dirty = false;
			string json = JsonUtility.ToJson(snapshot);
			if (json == savedJson) return true;
			if (!store.Save(snapshot))
				return false;
			current.dirty = snapshot.dirty; savedJson = json;
			return true;
		}

		private static bool SamePlacements(IReadOnlyList<MapObjectEntry> a, IReadOnlyList<MapObjectEntry> b)
		{
			if (a.Count != b.Count) return false;
			for (int i = 0; i < a.Count; i++)
				if (a[i].prefabId != b[i].prefabId || !MapSpaceFrame.Near(a[i].pose, b[i].pose, .00001f, .001f)) return false;
			return true;
		}
	}
}
