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
		public ColocationManager.ColocationMethod method;
		public Guid spaceId;
		public Guid frameId;
		public Guid spaceVersion;
		public Guid context;
		public Guid referenceContext;
		public Guid scan;
		public FixedString64Bytes spaceName;
		public float tagSizeCm;
		public int firstTagId;
		public int secondTagId;
		public bool initializationPending, hasPendingSetup;
		public ColocationManager.ColocationMethod pendingSetupMethod;
		public ReferenceTransitionIntent pendingSetupIntent;
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
		private readonly MapSpaceSessionSync spaceSync = new();

		public event Action AuthorityReady = delegate { };
		public event Action<MapIdentity, List<MapObjectEntry>, MapSpace> Received = delegate { };
		public event Action ChangingMapChanged = delegate { };
		public bool IsChangingMap => changing.Value;

		public void Register()
		{
			spaceSync.Register();
			objectRecords.Register();
			objectRecords.ValidateAdd = (_, _) => false;
			objectRecords.ValidateRemove = (_, _) => false;
			objectRecords.ValidateClear = _ => false;
			identity.Validate = (_, _) => false;
			changing.Validate = (_, _) => false;
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
			spaceSync.Unregister();
		}

		public void SetChanging(bool value) => changing.Value = value;

		public void Publish(GameMap map, MapSpace space, ColocationManager.ColocationMethod selectedMethod,
			Guid context, Guid referenceContext, Guid scan)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || map == null || space == null) return;
			if (!Guid.TryParseExact(map.id, "N", out Guid id) ||
			    !Guid.TryParseExact(map.version, "N", out Guid version)) return;

			Guid spaceVersion = Guid.Parse(space.version), frameId = Guid.Parse(space.canonicalFrameId);
			if (identity.Value.id == id && identity.Value.version == version && identity.Value.spaceVersion == spaceVersion &&
				identity.Value.method == selectedMethod && identity.Value.frameId == frameId && identity.Value.context == context &&
				identity.Value.referenceContext == referenceContext && identity.Value.scan == scan) return;
			spaceSync.Stage(space);
			objectRecords.Clear();
			foreach (MapObjectEntry entry in map.objects)
			{
				ObjectRecord record = new() { pose = space.Frame.ToCanonical(entry.pose) };
				record.prefabId.CopyFromTruncated(entry.prefabId ?? "");
				objectRecords.Add(record);
			}
			FixedString64Bytes name = default;
			name.CopyFromTruncated(map.name ?? "");
			FixedString64Bytes spaceName = default;
			spaceName.CopyFromTruncated(space.name ?? "");
			identity.Value = new MapIdentity
			{
				id = id, version = version, name = name,
				preferredColocationMethod = space.preferredColocationMethod, method = selectedMethod,
				spaceId = Guid.Parse(space.id), frameId = frameId, spaceVersion = spaceVersion,
				context = context, referenceContext = referenceContext, scan = scan, spaceName = spaceName, tagSizeCm = space.tagSizeCm,
				firstTagId = space.firstTagId, secondTagId = space.secondTagId,
				initializationPending = space.initializationPending, hasPendingSetup = space.hasPendingSetup,
				pendingSetupMethod = space.pendingSetupMethod, pendingSetupIntent = space.pendingSetupIntent
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
			Received.Invoke(identity.Value, objects, spaceSync.Read(identity.Value));
		}
	}
}
