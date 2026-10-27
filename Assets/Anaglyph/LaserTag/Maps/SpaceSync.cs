using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Unity.Collections;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	public enum SpaceRequestKind : byte
	{
		SetMethod, RegisterTag, RemoveTag, SetTagSize, SetPair, ForgetPair,
		TagSetupBegin, TagSetupMeasure, TagSetupFinish, TagSetupCancel
	}

	/// <summary>A client's ask of the host, in the space and frame the client sees.</summary>
	public struct SpaceRequest
	{
		public Guid spaceId;
		public Guid frameId;
		public SpaceRequestKind kind;
		public Method method;
		public int tag;
		public int second;
		public float size;
		public Pose pose;
		public int trackingGeneration;
	}

	public struct RequestRejected
	{
		public ulong recipient;
		public SpaceRequestKind kind;
	}

	/// <summary>
	/// Commits the lists that precede it on the bus: whoever sees a header sees the tags,
	/// anchors and objects it describes. <see cref="revision"/> is new on every publish;
	/// <see cref="mapRevision"/> is new each time a map is shown and scopes edit requests.
	/// </summary>
	public struct SpaceHeader
	{
		public Guid spaceId;
		public FixedString64Bytes spaceName;
		public Guid frameId;
		public AlignmentSettings alignment;
		public Guid mapId;
		public FixedString64Bytes mapName;
		public Guid mapRevision;
		public Guid revision;
		public bool changing;
	}

	/// <summary>
	/// The session's space on the wire. The host publishes the shown space and map: three
	/// lists, then the header that commits them, all on the bus's ordered channel. Clients
	/// render whatever the header describes and never save it. Clients ask for changes with
	/// one request event; the host answers refusals with one rejection event.
	/// </summary>
	public sealed class SpaceSync
	{
		private struct AnchorRecord
		{
			public Guid id;
			public int tagId;
			public Pose pose;
		}

		private struct ObjectRecord
		{
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		private readonly SpaceManager manager;
		private readonly SpaceAlignment alignment;
		private readonly SyncList<TagRef> tags = new("space.tags");
		private readonly SyncList<AnchorRecord> anchors = new("space.anchors");
		private readonly SyncList<ObjectRecord> objects = new("space.objects");
		private readonly SyncVariable<SpaceHeader> header = new("space.header");
		private readonly SyncEvent<SpaceRequest> request = new("space.request", EventRoute.ToAuthority);
		private readonly SyncEvent<RequestRejected> rejected = new("space.request.rejected", EventRoute.ViaAuthority);
		private string published;

		public SpaceSync(SpaceManager manager, SpaceAlignment alignment)
		{
			this.manager = manager;
			this.alignment = alignment;
		}

		public SpaceHeader Header => header.Value;

		public void Register()
		{
			// only the host writes; a client's writes are its requests
			tags.ValidateAdd = (_, _) => false; tags.ValidateRemove = (_, _) => false; tags.ValidateClear = _ => false;
			anchors.ValidateAdd = (_, _) => false; anchors.ValidateRemove = (_, _) => false; anchors.ValidateClear = _ => false;
			objects.ValidateAdd = (_, _) => false; objects.ValidateRemove = (_, _) => false; objects.ValidateClear = _ => false;
			header.Validate = (_, _) => false;
			rejected.Validate = (sender, _) => SyncBus.IsAuthority && sender == SyncBus.LocalClientId;
			tags.Register();
			anchors.Register();
			objects.Register();
			header.Register();
			request.Register();
			rejected.Register();
			header.Changed += OnHeaderChanged;
			header.Synced += OnHeaderSynced;
			request.Received += OnRequest;
			rejected.Received += OnRejected;
			SyncBus.Deactivated += OnDeactivated;
		}

		public void Unregister()
		{
			SyncBus.Deactivated -= OnDeactivated;
			rejected.Received -= OnRejected;
			request.Received -= OnRequest;
			header.Synced -= OnHeaderSynced;
			header.Changed -= OnHeaderChanged;
			rejected.Unregister();
			request.Unregister();
			header.Unregister();
			objects.Unregister();
			anchors.Unregister();
			tags.Unregister();
		}

		private void OnDeactivated() => published = null;

		// ------- host -------------------------------------------------

		/// <summary>Puts the shown space on the wire when anything in it changed since the last publish.</summary>
		public void Publish(Space space, Map map, Guid mapRevision, bool changing)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			string wire = space == null ? "" : $"{JsonUtility.ToJson(space)}|{map?.id}|{mapRevision}|{changing}";
			if (wire == published) return;
			published = wire;
			tags.Clear();
			anchors.Clear();
			objects.Clear();
			if (space == null)
			{
				header.Value = default;
				return;
			}
			foreach (TagRef tag in space.tags) tags.Add(tag);
			foreach (AnchorRef anchor in space.anchors)
				if (Guid.TryParseExact(anchor.guid, "N", out Guid guid)) anchors.Add(new AnchorRecord { id = guid, tagId = anchor.tagId, pose = anchor.pose });
			if (map != null)
				foreach (MapObjectRecord record in map.objects)
				{
					ObjectRecord entry = new() { pose = record.pose };
					entry.prefabId.CopyFromTruncated(record.prefabId ?? "");
					objects.Add(entry);
				}
			SpaceHeader value = new()
			{
				spaceId = space.Id, frameId = space.FrameId, alignment = space.alignment,
				mapId = map?.Id ?? Guid.Empty, mapRevision = mapRevision, revision = Guid.NewGuid(), changing = changing
			};
			value.spaceName.CopyFromTruncated(space.name ?? "");
			value.mapName.CopyFromTruncated(map?.name ?? "");
			header.Value = value;
		}

		private void OnRequest(ulong sender, SpaceRequest value)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			if (!alignment.HandleRequest(sender, value)) Reject(sender, value.kind);
		}

		private void Reject(ulong sender, SpaceRequestKind kind)
		{
			if (sender == SyncBus.LocalClientId) manager.ReportRejection();
			else rejected.Raise(new RequestRejected { recipient = sender, kind = kind });
		}

		// ------- client -----------------------------------------------

		public void Request(SpaceRequest value) => request.Raise(value);

		private void OnRejected(ulong _, RequestRejected value)
		{
			if (SyncBus.Active && !SyncBus.IsAuthority && value.recipient == SyncBus.LocalClientId) manager.ReportRejection();
		}

		private void OnHeaderChanged(SpaceHeader _, SpaceHeader value) => Receive(value);
		private void OnHeaderSynced() => Receive(header.Value);

		// the document the header commits, sanitized like anything else that enters from outside
		private void Receive(SpaceHeader value)
		{
			if (!SyncBus.Active || SyncBus.IsAuthority) return;
			if (value.spaceId == Guid.Empty)
			{
				manager.ShowSessionSpace(null, Guid.Empty, false);
				return;
			}
			Space space = new()
			{
				id = value.spaceId.ToString("N"), name = value.spaceName.ToString(), frameId = value.frameId.ToString("N"),
				alignment = value.alignment, lastUsed = DateTime.UtcNow.Ticks
			};
			foreach (TagRef tag in tags) space.tags.Add(tag);
			foreach (AnchorRecord anchor in anchors) space.anchors.Add(new AnchorRef { guid = anchor.id.ToString("N"), tagId = anchor.tagId, pose = anchor.pose });
			if (value.mapId != Guid.Empty)
			{
				Map map = new() { id = value.mapId.ToString("N"), name = value.mapName.ToString(), lastUsed = DateTime.UtcNow.Ticks };
				foreach (ObjectRecord record in objects) map.objects.Add(new MapObjectRecord { prefabId = record.prefabId.ToString(), pose = record.pose });
				space.maps.Add(map);
			}
			space.Sanitize();
			manager.ShowSessionSpace(space, value.mapRevision, value.changing);
		}
	}
}
