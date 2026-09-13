using System;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Single editable owner for the selected space; reads are detached snapshots.</summary>
	public sealed class MapSpaceManager
	{
		private readonly MapSpaceStore store;
		private MapSpace current;
		private string savedJson;
		public MapSpaceManager(MapSpaceStore store) => this.store = store;
		public MapSpace CurrentSpace => current?.Clone();
		public string CurrentId => current?.id;
		public bool HasSpace => current != null;
		public bool HasTags => current != null && current.HasTags;
		public bool HasReferences => current != null && current.HasReferenceBasedData;
		public int AnchorCount => current?.anchors.Count ?? 0;
		public MapSpaceFrame Frame => current != null ? current.Frame : new MapSpaceFrame(null, null, Pose.identity);
		public void Load(MapSpace space) { current = space?.Clone(); savedJson = current == null ? null : JsonUtility.ToJson(current); }
		public void Unload() => current = null;
		public MapSpace Create() { savedJson = null; current = MapSpace.Create(store.GenerateName()); return CurrentSpace; }
		public void MarkUsed() { if (current != null) current.lastUsed = DateTime.UtcNow.Ticks; }
		public bool Save(bool commitSharedContent = false)
		{
			if (current == null) return true;
			var copy = current.Clone();
			if (commitSharedContent) copy.dirty = false;
			string json = JsonUtility.ToJson(copy);
			if (json == savedJson) return true;
			if (!store.Save(copy)) return false;
			current.dirty = copy.dirty; savedJson = json; return true;
		}
		public bool Rename(string name)
		{
			if (current == null || current.name == name) return false;
			current.name = name; current.MarkChanged(); return true;
		}
		public bool SetPreferredMethod(ColocationManager.ColocationMethod method)
		{
			if (current == null || !ColocationManager.IsValidMethod(method) || current.preferredColocationMethod == method) return false;
			current.preferredColocationMethod = method; current.MarkChanged(); return true;
		}
		public void ReplaceReferences(MapSpace snapshot, bool authored)
		{
			if (current == null || snapshot.id != current.id) throw new InvalidOperationException("Space changed during reference capture.");
			current = snapshot.Clone(); if (authored) current.MarkChanged();
		}
		public bool AddMap(GameMap map)
		{
			if (current == null || map.storageFrameId != current.storageFrameId) return false;
			var owner = store.FindOwner(map.id);
			if (owner != null && owner.id != current.id) return false;
			if (!current.mapIds.Contains(map.id)) current.mapIds.Add(map.id);
			return true;
		}
	}
}
