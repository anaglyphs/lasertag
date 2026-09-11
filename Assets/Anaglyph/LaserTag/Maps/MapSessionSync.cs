using System;
using System.Collections.Generic;
using Anaglyph.Netcode.SyncVariables;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.LaserTag.Maps
{
	public struct MapIdentity
	{
		public Guid id;
		public Guid version;
		public FixedString64Bytes name;
		public ColocationManager.ColocationMethod preferredColocationMethod;
		public bool systemFrameForTagSetup;
	}

	/// <summary>
	/// Transports committed map identity and authored object placements. Colocation providers
	/// transport their own references. The identity commits the preceding object-list writes
	/// on SyncBus's ordered channel; an empty list is a complete document, independent of NGO
	/// spawn timing. This class never loads documents, edits the scene or calls game policy.
	/// </summary>
	internal sealed class MapSessionSync
	{
		private struct ObjectRecord
		{
			public FixedString64Bytes prefabId;
			public Pose pose;
		}

		private readonly SyncList<ObjectRecord> objectRecords = new("map.objects");
		private readonly SyncVariable<MapIdentity> identity = new("map.identity");
		private readonly SyncVariable<bool> changing = new("map.changing");
		private bool authorityStarted;

		public event Action AuthorityReady = delegate { };
		public event Action<MapIdentity, List<MapObjectEntry>> Received = delegate { };
		public event Action ChangingMapChanged = delegate { };
		public bool IsChangingMap => changing.Value;

		public void Register()
		{
			objectRecords.Register();
			identity.Register();
			identity.Changed += OnIdentityChanged;
			identity.Synced += OnSynced;
			changing.Register();
			changing.Changed += OnChangingChanged;
			SyncBus.Deactivated += OnDeactivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		public void Unregister()
		{
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Deactivated -= OnDeactivated;
			changing.Changed -= OnChangingChanged;
			changing.Unregister();
			identity.Synced -= OnSynced;
			identity.Changed -= OnIdentityChanged;
			identity.Unregister();
			objectRecords.Unregister();
		}

		public void SetChanging(bool value) => changing.Value = value;

		public void Publish(GameMap map)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || map == null) return;
			if (!Guid.TryParseExact(map.id, "N", out Guid id) ||
			    !Guid.TryParseExact(map.version, "N", out Guid version)) return;

			if (identity.Value.id == id && identity.Value.version == version) return;
			objectRecords.Clear();
			foreach (MapObjectEntry entry in map.objects)
			{
				ObjectRecord record = new() { pose = entry.pose };
				record.prefabId.CopyFromTruncated(entry.prefabId ?? "");
				objectRecords.Add(record);
			}
			FixedString64Bytes name = default;
			name.CopyFromTruncated(map.name ?? "");
			identity.Value = new MapIdentity
			{
				id = id, version = version, name = name,
				preferredColocationMethod = map.preferredColocationMethod,
				systemFrameForTagSetup = map.systemFrameForTagSetup
			};
		}

		private void OnChangingChanged(bool _, bool __) => ChangingMapChanged.Invoke();
		private void OnDeactivated() => authorityStarted = false;
		private void OnAuthorityChanged(bool _)
		{
			authorityStarted = false;
			if (SyncBus.Active && SyncBus.IsAuthority) OnSynced();
		}
		private void OnSynced()
		{
			if (!SyncBus.Active) return;
			if (!SyncBus.IsAuthority) { Receive(); return; }
			if (authorityStarted) return;
			authorityStarted = true;
			AuthorityReady.Invoke();
		}
		private void OnIdentityChanged(MapIdentity _, MapIdentity __) => Receive();
		private void Receive()
		{
			if (!SyncBus.Active || SyncBus.IsAuthority || identity.Value.id == Guid.Empty) return;
			List<MapObjectEntry> objects = new();
			foreach (ObjectRecord record in objectRecords)
				objects.Add(new MapObjectEntry { prefabId = record.prefabId.ToString(), pose = record.pose });
			Received.Invoke(identity.Value, objects);
		}
	}
}
