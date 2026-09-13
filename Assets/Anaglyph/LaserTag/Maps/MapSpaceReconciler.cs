using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Pure coordinate and identity adoption. Verification of physical observations is performed by the controller.</summary>
	public static class MapSpaceReconciler
	{
		public static bool TryKnownOffset(MapSpace local, string targetFrame, out Pose offset)
		{
			if (local.canonicalFrameId == targetFrame) { offset = local.canonicalFromStorage; return true; }
			foreach (var binding in local.knownFrames)
				if (binding.frameId == targetFrame) { offset = binding.canonicalFromStorage; return true; }
			offset = default; return false;
		}
		public static bool HasFrameContradiction(MapSpace local, MapSpace remote, Pose offset)
		{
			foreach (var anchor in remote.anchors)
				if (local.TryGetAnchor(anchor.guid, out var saved) && !MapSpaceFrame.Near(MapSpaceFrame.Compose(offset, saved.canonPose), anchor.canonPose, .08f, 5f)) return true;
			return false;
		}
		public static MapSpace Rebase(MapSpace local, string frameId, Pose canonicalFromStorage)
		{
			if (!MapSpace.ValidId(frameId) || !MapSpaceFrame.ValidOffset(canonicalFromStorage))
				throw new ArgumentException("Invalid canonical frame mapping.");
			MapSpace result = local.Clone();
			if (result.canonicalFrameId == frameId && MapSpaceFrame.Near(result.canonicalFromStorage, canonicalFromStorage, .00001f, .001f)) return result;
			result.previousCanonicalFrameId = result.canonicalFrameId;
			result.previousOffset = result.canonicalFromStorage;
			RememberFrame(result, result.canonicalFrameId, result.canonicalFromStorage);
			result.canonicalFrameId = frameId; result.canonicalFromStorage = canonicalFromStorage; result.frameRevision++;
			RememberFrame(result, frameId, canonicalFromStorage);
			return result;
		}
		private static void RememberFrame(MapSpace space, string id, Pose offset)
		{
			space.knownFrames.RemoveAll(f => f.frameId == id);
			space.knownFrames.Add(new() { frameId = id, canonicalFromStorage = offset });
		}
		public static MapSpace Undo(MapSpace space) => MapSpace.ValidId(space.previousCanonicalFrameId)
			? Rebase(space, space.previousCanonicalFrameId, space.previousOffset) : space.Clone();

		/// <summary>Host DTO poses are canonical. Existing client poses never change during this operation.</summary>
		public static MapSpace ImportReferences(MapSpace local, MapSpace remote, Pose verifiedOffset)
		{
			MapSpace result = Rebase(local, remote.canonicalFrameId, verifiedOffset);
			if (result.referenceSourceId != remote.id || result.referenceDirty) result.RetainActiveReferences();
			result.referenceSourceId = remote.id; result.referenceVersion = remote.version; result.referenceDirty = false;
			result.version = Guid.NewGuid().ToString("N");
			result.tags = remote.tags.ConvertAll(t => new MapTagEntry { id = t.id, canonPose = result.Frame.ToStorage(t.canonPose) });
			result.anchors = remote.anchors.ConvertAll(a => new MapAnchorEntry { guid = a.guid, tagId = a.tagId, canonPose = result.Frame.ToStorage(a.canonPose) });
			result.tagSizeCm = remote.tagSizeCm;
			result.preferredColocationMethod = remote.preferredColocationMethod;
			result.initializationPending = remote.initializationPending;
			result.hasPendingSetup = remote.hasPendingSetup;
			result.pendingSetupMethod = remote.pendingSetupMethod;
			result.pendingSetupIntent = remote.pendingSetupIntent;
			result.firstTagId = remote.firstTagId; result.secondTagId = remote.secondTagId;
			result.associations.RemoveAll(a => a.remoteSpaceId == remote.id);
			result.associations.Add(new() { remoteSpaceId = remote.id, frameId = remote.canonicalFrameId });
			result.lastUsed = DateTime.UtcNow.Ticks;
			return result;
		}
		public static List<GameMap> PrepareMapImport(MapSpace space, GameMap canonicalMap, GameMap existing)
		{
			List<GameMap> writes = new();
			if (existing != null && existing.dirty && existing.version != canonicalMap.version)
			{
				var fork = existing.Clone(); fork.id = Guid.NewGuid().ToString("N"); fork.name += " (local copy)";
				space.mapIds.Add(fork.id); writes.Add(fork);
			}
			var imported = ImportMap(canonicalMap, space);
			if (!space.mapIds.Contains(imported.id)) space.mapIds.Add(imported.id);
			writes.Add(imported); return writes;
		}

		public static GameMap ImportMap(GameMap canonicalMap, MapSpace local)
		{
			GameMap result = canonicalMap.Clone();
			result.storageFrameId = local.storageFrameId;
			result.objects = canonicalMap.objects.ConvertAll(o => new MapObjectEntry { prefabId = o.prefabId, pose = local.Frame.ToStorage(o.pose) });
			result.dirty = false; result.lastUsed = DateTime.UtcNow.Ticks;
			return result;
		}
	}
}
