using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// A named collection of gameplay objects plus the references that tie it to a physical
	/// space. World space is the canon frame: every pose in here is a world pose, internally
	/// consistent with every other pose in the same map. Only one map can be loaded at a time,
	/// because loading a map means adopting its frame.
	///
	/// The anchor list is always THIS device's realization of the map. In shared-anchor mode
	/// the lists converge on identical guids across devices because anchors were transported
	/// between headsets; in tag mode each device mints its own. Nothing durable may depend on
	/// guid equality across devices.
	/// </summary>
	[Serializable]
	public class GameMap
	{
		public string id;

		public string name;

		/// <summary>Revision of authored content. Storage and local anchor maintenance do not change it.</summary>
		public string version;

		/// <summary>Set on the first local edit; a dirty copy forks instead of being replaced.</summary>
		public bool dirty;

		/// <summary>DateTime UTC ticks. Most recently used maps get probed and loaded first.</summary>
		public long lastUsed;

		public long lastEdited;

		public List<MapObjectEntry> objects = new();
		public List<MapAnchorEntry> anchors = new();
		public List<MapTagEntry> tags = new();

		/// <summary>The size a map starts life at, until someone measures their tags.</summary>
		public const float DefaultTagSizeCm = 10f;

		/// <summary>
		/// The physical edge length of this map's AprilTags, in centimeters. Solving a tag's pose
		/// scales with it, so a registered pose only means anything at the size it was registered
		/// at — which is why this travels with the map rather than being a device setting. Zero
		/// only on a map authored before sizes were recorded, whose tags are of unknown size; the
		/// last size this device adopted stands in.
		/// </summary>
		public float tagSizeCm;

		/// <summary>
		/// A map records whether it has tags rather than being bound to a colocation method:
		/// a tag map can be hosted in shared-anchor mode, but not the other way around.
		/// </summary>
		public bool HasTags => tags.Count > 0;

		/// <summary>Nothing has been authored into it yet, so starting another blank map
		/// would just mint a second one of these.</summary>
		public bool IsEmpty => objects.Count == 0 && anchors.Count == 0 && tags.Count == 0;

		/// <summary>A detached document; callers never receive the owner's mutable lists.</summary>
		public GameMap Clone() => new()
		{
			id = id, name = name, version = version, dirty = dirty,
			lastUsed = lastUsed, lastEdited = lastEdited, tagSizeCm = tagSizeCm,
			objects = new(objects), anchors = new(anchors), tags = new(tags)
		};

		public bool TryGetAnchor(string guid, out MapAnchorEntry entry)
		{
			foreach (MapAnchorEntry anchor in anchors)
				if (anchor.guid == guid)
				{
					entry = anchor;
					return true;
				}

			entry = default;
			return false;
		}

		public bool TryGetTag(int tagId, out MapTagEntry entry)
		{
			foreach (MapTagEntry tag in tags)
				if (tag.id == tagId)
				{
					entry = tag;
					return true;
				}

			entry = default;
			return false;
		}

		public void SetAnchorWithTag(string guid, Pose canonPose, int tagId)
		{
			for (int i = 0; i < anchors.Count; i++)
				if (anchors[i].guid == guid)
				{
					// Republishing an unchanged anchor is common (every session start);
					// don't churn the list or the file for it.
					if (anchors[i].canonPose == canonPose && anchors[i].tagId == tagId)
						return;

					anchors[i] = new MapAnchorEntry { guid = guid, canonPose = canonPose, tagId = tagId };
					return;
				}

			anchors.Add(new MapAnchorEntry { guid = guid, canonPose = canonPose, tagId = tagId });
		}

		public void SetTag(int tagId, Pose canonPose)
		{
			for (int i = 0; i < tags.Count; i++)
				if (tags[i].id == tagId)
				{
					tags[i] = new MapTagEntry { id = tagId, canonPose = canonPose };
					return;
				}

			tags.Add(new MapTagEntry { id = tagId, canonPose = canonPose });
		}
	}

	[Serializable]
	public struct MapObjectEntry
	{
		public string prefabId;
		public Pose pose;
	}

	[Serializable]
	public struct MapAnchorEntry
	{
		/// <summary>
		/// The anchor's guid, which on this runtime is simultaneously its trackable id, its
		/// local-storage save id, and its shared group id.
		/// </summary>
		public string guid;

		/// <summary>Where the anchor SHOULD be, in this map's world frame.</summary>
		public Pose canonPose;

		/// <summary>
		/// The registered tag this anchor stands in for, or -1 for a roaming anchor. A tag
		/// anchor's canon pose gets rewritten from its tag's observations as the anchor
		/// drifts.
		/// </summary>
		public int tagId;
	}

	[Serializable]
	public struct MapTagEntry
	{
		public int id;

		/// <summary>
		/// Where the tag SHOULD be, in this map's world frame. Registered before hosting, as a
		/// map-authoring step, so it is always expressed in the map's own frame.
		/// </summary>
		public Pose canonPose;
	}
}
