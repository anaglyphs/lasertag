using System;
using System.Collections.Generic;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// Owns the editable map document. Reads return detached snapshots; only these operations
	/// change the document. Networking, scene objects, alignment and UI are coordinated above it.
	/// The flat GameMap file format is retained for existing saves, but authored content and
	/// device anchor records have separate update paths and revision rules.
	/// </summary>
	public sealed class MapManager
	{
		private readonly MapStore store;
		private GameMap current;
		private string preservedConflict;
		private string preservedForkId;

		public MapManager(MapStore store) => this.store = store;
		public GameMap CurrentMap => current?.Clone();
		public string CurrentId => current?.id;
		public bool HasMap => current != null;
		public bool IsEmpty => current == null || current.IsEmpty;
		public bool HasTags => current != null && current.HasTags;
		public int AnchorCount => current?.anchors.Count ?? 0;
		public int TaggedAnchorCount
		{
			get
			{
				int count = 0;
				if (current == null)
					return count;

				foreach (MapAnchorEntry anchor in current.anchors)
					if (anchor.tagId >= 0)
						count++;
				return count;
			}
		}

		public void Load(GameMap map) => current = map?.Clone();
		public void Unload() => current = null;

		public GameMap Create()
		{
			current = new GameMap
			{
				id = Guid.NewGuid().ToString("N"), version = Guid.NewGuid().ToString("N"),
				name = store.GenerateName(), tagSizeCm = GameMap.DefaultTagSizeCm,
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
			if (current == null || Equal(current.objects, objects))
				return false;
			current.objects = new List<MapObjectEntry>(objects);
			ContentChanged();
			return true;
		}

		public bool SetTags(IReadOnlyList<MapTagEntry> tags, float sizeCm)
		{
			if (current == null)
				return false;
			if (sizeCm <= 0f)
				sizeCm = current.tagSizeCm;
			if (Equal(current.tags, tags) && current.tagSizeCm == sizeCm)
				return false;
			current.tags = new List<MapTagEntry>(tags);
			current.tagSizeCm = sizeCm;
			ContentChanged();
			return true;
		}

		/// <summary>Local realization maintenance never creates a shared content revision.</summary>
		public bool SetAnchors(IReadOnlyList<MapAnchorEntry> anchors)
		{
			if (current == null || Equal(current.anchors, anchors))
				return false;
			current.anchors = new List<MapAnchorEntry>(anchors);
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
			if (!store.Save(snapshot))
				return false;
			current.dirty = snapshot.dirty;
			return true;
		}

		/// <summary>
		/// Preserves divergent local work before adopting a received document, including when
		/// the same map is already open. Failure to save the fork aborts adoption.
		/// </summary>
		public bool TryAdopt(GameMap incoming)
		{
			if (!TryPreserveLocalEdits(incoming))
				return false;

			GameMap adopted = incoming.Clone();
			adopted.dirty = false;
			adopted.lastUsed = DateTime.UtcNow.Ticks;
			if (!store.Save(adopted))
				return false;
			current = adopted;
			return true;
		}

		private bool TryPreserveLocalEdits(GameMap incoming)
		{
			GameMap local = current;
			if (local == null || local.id != incoming.id)
				store.TryGet(incoming.id, out local);
			if (local == null || !local.dirty || local.version == incoming.version)
				return true;

			string conflict = local.id + ":" + local.version;
			if (preservedConflict == conflict && store.TryGet(preservedForkId, out _))
				return true;

			GameMap fork = local.Clone();
			fork.id = Guid.NewGuid().ToString("N");
			fork.name += " (fork)";
			if (!store.Save(fork))
				return false;

			preservedConflict = conflict;
			preservedForkId = fork.id;
			return true;
		}

		private static bool Equal<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
		{
			if (a.Count != b.Count)
				return false;
			var comparer = EqualityComparer<T>.Default;
			for (int i = 0; i < a.Count; i++)
				if (!comparer.Equals(a[i], b[i]))
					return false;
			return true;
		}
	}
}
