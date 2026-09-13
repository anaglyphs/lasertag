using System;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Only shared definitions travel here, projected into the committed session frame.</summary>
	internal sealed class MapSpaceSessionSync
	{
		private struct TagRecord { public int id; public Pose pose; }
		private struct AnchorRecord { public Guid id; public int tagId; public Pose pose; }
		private readonly SyncList<TagRecord> tags;
		private readonly SyncList<AnchorRecord> anchors;
		public MapSpaceSessionSync(string prefix = "space") { tags = new(prefix + ".tags"); anchors = new(prefix + ".anchors"); }
		public void Register()
		{
			tags.Register(); anchors.Register();
			tags.ValidateAdd = (_, _) => false; tags.ValidateRemove = (_, _) => false; tags.ValidateClear = _ => false;
			anchors.ValidateAdd = (_, _) => false; anchors.ValidateRemove = (_, _) => false; anchors.ValidateClear = _ => false;
		}
		public void Unregister() { tags.Unregister(); anchors.Unregister(); }
		public void Stage(MapSpace space)
		{
			tags.Clear(); anchors.Clear();
			foreach (var tag in space.tags) tags.Add(new() { id = tag.id, pose = space.Frame.ToCanonical(tag.canonPose) });
			foreach (var anchor in space.anchors)
				if (Guid.TryParse(anchor.guid, out Guid guid)) anchors.Add(new() { id = guid, tagId = anchor.tagId, pose = space.Frame.ToCanonical(anchor.canonPose) });
		}
		public MapSpace Read(MapIdentity identity)
		{
			MapSpace space = new()
			{
				id = identity.spaceId.ToString("N"), name = identity.spaceName.ToString(), version = identity.spaceVersion.ToString("N"),
				canonicalFrameId = identity.frameId.ToString("N"), storageFrameId = identity.frameId.ToString("N"),
				preferredColocationMethod = identity.preferredColocationMethod, tagSizeCm = identity.tagSizeCm,
				initializationPending = identity.initializationPending, hasPendingSetup = identity.hasPendingSetup,
				pendingSetupMethod = identity.pendingSetupMethod, pendingSetupIntent = identity.pendingSetupIntent,
				firstTagId = identity.firstTagId, secondTagId = identity.secondTagId, referenceSourceId = identity.spaceId.ToString("N")
			};
			foreach (var tag in tags) space.tags.Add(new() { id = tag.id, canonPose = tag.pose });
			foreach (var anchor in anchors) space.anchors.Add(new() { guid = anchor.id.ToString("N"), tagId = anchor.tagId, canonPose = anchor.pose });
			return space;
		}
	}
}
