using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag
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

		/// <summary>Revision of this copy's content. Re-minted on every save while dirty.</summary>
		public string version;

		/// <summary>
		/// The received version this copy derives from — the host's <see cref="version"/> at
		/// the moment this device adopted the map. Empty on maps created on this device.
		/// Together with <see cref="dirty"/> this drives fork-on-edit.
		/// </summary>
		public string baseVersion;

		/// <summary>Set on the first local edit; a dirty copy forks instead of being replaced.</summary>
		public bool dirty;

		/// <summary>DateTime UTC ticks. Most recently used maps get probed and loaded first.</summary>
		public long lastUsed;

		public long lastEdited;

		public List<MapObjectEntry> objects = new();
		public List<MapAnchorEntry> anchors = new();
		public List<MapTagEntry> tags = new();

		/// <summary>
		/// A map records whether it has tags rather than being bound to a colocation method:
		/// a tag map can be hosted in shared-anchor mode, but not the other way around.
		/// </summary>
		public bool HasTags => tags.Count > 0;

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

		public void SetAnchor(string guid, Pose canonPose)
		{
			for (int i = 0; i < anchors.Count; i++)
				if (anchors[i].guid == guid)
				{
					// Preserve an existing tag association: updating a canon pose must not
					// orphan a per-tag anchor.
					SetAnchorWithTag(guid, canonPose, anchors[i].tagId);
					return;
				}

			anchors.Add(new MapAnchorEntry { guid = guid, canonPose = canonPose, tagId = -1 });
		}

		public void SetAnchorWithTag(string guid, Pose canonPose, int tagId)
		{
			for (int i = 0; i < anchors.Count; i++)
				if (anchors[i].guid == guid)
				{
					anchors[i] = new MapAnchorEntry { guid = guid, canonPose = canonPose, tagId = tagId };
					return;
				}

			anchors.Add(new MapAnchorEntry { guid = guid, canonPose = canonPose, tagId = tagId });
		}

		public bool TryGetAnchorByTag(int tagId, out MapAnchorEntry entry)
		{
			foreach (MapAnchorEntry anchor in anchors)
				if (anchor.tagId == tagId)
				{
					entry = anchor;
					return true;
				}

			entry = default;
			return false;
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
