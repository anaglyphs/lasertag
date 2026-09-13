using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>A gameplay layout. Poses use its parent space's immutable storage frame.</summary>
	[Serializable]
	public class GameMap
	{
		public const int SchemaVersion = 1;
		public int schemaVersion = SchemaVersion;
		public string id;
		public string name;
		public string storageFrameId;
		public string version;
		public bool dirty;
		public long lastUsed;
		public long lastEdited;
		public List<MapObjectEntry> objects = new();
		public bool IsEmpty => objects.Count == 0;

		public GameMap Clone() => new()
		{
			schemaVersion = schemaVersion, id = id, name = name, storageFrameId = storageFrameId,
			version = version, dirty = dirty, lastUsed = lastUsed, lastEdited = lastEdited,
			objects = new(objects)
		};

		internal bool Validate() => schemaVersion == SchemaVersion && MapSpace.ValidId(id) &&
			MapSpace.ValidId(version) && MapSpace.ValidId(storageFrameId) && objects != null &&
			objects.TrueForAll(o => !string.IsNullOrEmpty(o.prefabId) && MapSpaceFrame.ValidPose(o.pose));
	}

	[Serializable]
	public struct MapObjectEntry
	{
		public string prefabId;
		public Pose pose;
	}
}
