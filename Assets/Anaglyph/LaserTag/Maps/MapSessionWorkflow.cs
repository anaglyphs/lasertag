using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Netcode;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Owns incoming session documents, durable adoption and disconnect restoration.</summary>
	internal sealed class MapSessionWorkflow
	{
		private readonly MapCatalog documents;
		private MapWorkingCopy mapDocument => documents.MapDocument;
		private MapSpaceWorkingCopy spaceDocument => documents.SpaceDocument;
		private MapCatalogJournal catalog => documents.Journal;
		private readonly ColocationManager colocation;
		private readonly MapLifecycle lifecycle;
		private readonly MapVisitContext visit;
		private readonly CancellationToken lifetime;
		private readonly Action<MapSpace> preservePrivateReferences;
		private readonly Func<bool> trackingReady;
		private MapIdentity remoteIdentity;
		private MapSpace beforeSessionSpace;
		private GameMap beforeSessionMap;
		private bool transientSession;
		private int generation;

		public MapIdentity RemoteIdentity => remoteIdentity;
		public bool IsTransient => transientSession;
		public bool UsesSessionDocuments => SyncBus.Active &&
			(lifecycle.Phase == MapPhase.FollowingSession || transientSession);
		public int Generation => generation;

		public event Action ChangingFrame = delegate { };
		public event Action<MapIdentity, bool, bool, bool> Activated = delegate { };
		public event Action DocumentsChanged = delegate { };
		public event Action<bool> Restored = delegate { };
		public event Action SaveRequested = delegate { };

		public MapSessionWorkflow(MapCatalog documents, ColocationManager colocation,
			MapLifecycle lifecycle, MapVisitContext visit,
			CancellationToken lifetime, Action<MapSpace> preservePrivateReferences, Func<bool> trackingReady)
		{
			this.documents = documents;
			this.colocation = colocation;
			this.lifecycle = lifecycle;
			this.visit = visit;
			this.lifetime = lifetime;
			this.preservePrivateReferences = preservePrivateReferences;
			this.trackingReady = trackingReady;
		}

		public void BeginSession()
		{
			beforeSessionSpace = spaceDocument.CurrentSpace;
			beforeSessionMap = mapDocument.CurrentMap;
			lifecycle.EnterSession(SyncBus.IsAuthority);
			InvalidateIncoming();
		}

		public void InvalidateIncoming() => generation++;

		public void ApplyCatalogChange(CatalogChange change)
		{
			if (change.Kind == CatalogChangeKind.MapDeleted && beforeSessionMap?.id == change.DeletedMapId)
				beforeSessionMap = null;
			if (beforeSessionSpace == null || beforeSessionSpace.id != change.Space.id)
				return;

			if (change.Kind == CatalogChangeKind.SpaceDeleted)
			{
				beforeSessionSpace = null;
				beforeSessionMap = null;
			}
			else if (beforeSessionSpace.storageFrameId == change.Space.storageFrameId)
				beforeSessionSpace.mapIds = new(change.Space.mapIds);
		}

		public void ResetForLocalSpace()
		{
			transientSession = false;
			remoteIdentity = default;
		}

		public void Receive(MapIdentity identity, List<MapObjectEntry> placements, MapSpace remote)
		{
			if (identity.referenceContext == Guid.Empty || identity.context == Guid.Empty ||
				identity.frameId == Guid.Empty || remote == null || !remote.Validate())
				return;

			var incoming = new GameMap
			{
				id = identity.id.ToString("N"),
				version = identity.version.ToString("N"),
				name = identity.name.ToString(),
				storageFrameId = remote.storageFrameId,
				objects = placements
			};
			if (!incoming.Validate())
				return;

			int operation = ++generation;
			bool referencesChanged = remoteIdentity.spaceVersion != identity.spaceVersion;
			bool newFrame = remoteIdentity.spaceId != identity.spaceId ||
				remoteIdentity.frameId != identity.frameId || visit.ReferenceContext != identity.referenceContext;
			if (!newFrame)
				preservePrivateReferences(remote);

			var local = documents.SpaceStore.FindAssociation(remote.id) ?? beforeSessionSpace;
			remoteIdentity = identity;
			visit.MapContext = identity.context;
			visit.ReferenceContext = identity.referenceContext;
			bool scanChanged = visit.ScanContext != identity.scan;
			visit.ScanContext = identity.scan;
			if (newFrame)
				ChangingFrame.Invoke();

			bool adopted = local != null &&
				(MapSpaceReconciler.TryKnownOffset(local, remote.canonicalFrameId, out var offset) ||
				 CanReuseEmptyDraft(local, out offset)) && TryAdopt(local, remote, incoming, offset);
			if (!adopted)
			{
				transientSession = true;
				spaceDocument.Load(remote);
				mapDocument.Load(incoming);
				CacheSession(remote, incoming);
			}

			lifecycle.FollowSession();
			Activated.Invoke(identity, newFrame, scanChanged, referencesChanged);
			if (adopted)
				return;

			if (local != null)
				ReconcileSession(local, remote, incoming, operation, lifetime);
			else if (beforeSessionSpace == null)
				AdoptNewSessionSpace(remote, incoming);
		}

		public bool SaveTransientSession() => CacheSession(spaceDocument.CurrentSpace, mapDocument.CurrentMap);

		public void RetryAdoption()
		{
			var space = spaceDocument.CurrentSpace;
			var map = mapDocument.CurrentMap;
			if (!transientSession || space == null || map == null)
				return;

			var local = documents.SpaceStore.FindAssociation(remoteIdentity.spaceId.ToString("N"));
			if (local != null && MapSpaceReconciler.TryKnownOffset(local, space.canonicalFrameId, out var offset) &&
				TryAdopt(local, space, map, offset))
				DocumentsChanged.Invoke();
		}

		private bool CanReuseEmptyDraft(MapSpace local, out Pose offset)
		{
			offset = Pose.identity;
			if (local.HasReferenceBasedData)
				return false;

			foreach (var id in local.mapIds)
				if (documents.MapStore.Read(id, out var map) != DocumentReadStatus.Found || !map.IsEmpty)
					return false;
			return true;
		}

		private bool TryAdopt(MapSpace local, MapSpace remote, GameMap incoming, Pose offset)
		{
			var owner = documents.SpaceStore.FindOwner(incoming.id);
			if (catalog.HasPending || MapSpaceReconciler.HasFrameContradiction(local, remote, offset) ||
				(owner != null && owner.id != local.id))
				return false;

			var adopted = MapSpaceReconciler.ImportReferences(local, remote, offset);
			MapSpaceColocationAdapter.CopyCompatiblePrivateAnchors(adopted, remote);
			var read = documents.MapStore.Read(incoming.id, out var previous);
			if (read == DocumentReadStatus.Unavailable)
				return false;

			var writes = MapSpaceReconciler.PrepareMapImport(adopted, incoming, previous);
			var map = writes[writes.Count - 1];
			if (!catalog.Commit(adopted, writes))
			{
				SaveRequested.Invoke();
				return false;
			}

			spaceDocument.Load(adopted);
			mapDocument.Load(map);
			transientSession = false;
			return true;
		}

		private void AdoptNewSessionSpace(MapSpace remote, GameMap map)
		{
			var fresh = MapSpace.Create(remote.name);
			fresh.canonicalFrameId = remote.canonicalFrameId;
			fresh.storageFrameId = remote.canonicalFrameId;
			if (TryAdopt(fresh, remote, map, Pose.identity))
				DocumentsChanged.Invoke();
		}

		private async void ReconcileSession(MapSpace local, MapSpace remote, GameMap map,
			int operation, CancellationToken token)
		{
			if (!local.HasReferenceBasedData || !remote.HasReferenceBasedData)
				return;

			Guid context = visit.ReferenceContext;
			var localReferences = ProbeReferenceSet(local);
			var remoteReferences = ProbeReferenceSet(remote);
			using var localObservation = new MapSpaceReferenceObservation(localReferences,
				ReferenceMethod(localReferences), AnchorRegistry.Instance, colocation.TagProvider);
			using var remoteObservation = new MapSpaceReferenceObservation(remoteReferences,
				ReferenceMethod(remoteReferences), AnchorRegistry.Instance, colocation.TagProvider);
			using var localLease = localObservation.Observe();
			using var remoteLease = remoteObservation.Observe();
			List<ColocationConstraint> localConstraints = new();
			List<ColocationConstraint> remoteConstraints = new();
			double stableSince = -1;
			Pose previousOffset = default;
			int trackingGeneration = colocation.TrackingGeneration;
			try
			{
				while (!token.IsCancellationRequested && SyncBus.Active && !SyncBus.IsAuthority && operation == generation)
				{
					await Awaitable.NextFrameAsync(token);
					if (!IsCurrentReconciliation(remote, operation, context))
						return;

					localConstraints.Clear();
					remoteConstraints.Clear();
					localObservation.GetColocationConstraints(localConstraints);
					remoteObservation.GetColocationConstraints(remoteConstraints);
					if (!trackingReady() ||
						!TryEvaluateReferences(localConstraints, out var localAlignment) ||
						!TryEvaluateReferences(remoteConstraints, out var remoteAlignment))
					{
						stableSince = -1;
						continue;
					}

					var offset = MapSpaceFrame.Compose(
						MapSpaceFrame.Compose(remoteAlignment, MapSpaceFrame.Inverse(localAlignment)),
						local.canonicalFromStorage);
					if (trackingGeneration != colocation.TrackingGeneration || stableSince < 0 ||
						!MapSpaceFrame.Near(offset, previousOffset, .02f, 1))
					{
						stableSince = Time.realtimeSinceStartupAsDouble;
						trackingGeneration = colocation.TrackingGeneration;
					}

					previousOffset = offset;
					if (Time.realtimeSinceStartupAsDouble - stableSince < 1)
						continue;

					if (TryFinishSessionReconciliation(local, remote, map, offset, operation, context))
						DocumentsChanged.Invoke();
					return;
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private static bool TryEvaluateReferences(List<ColocationConstraint> constraints, out Pose alignment) =>
			ColocationFit.TryEvaluate(constraints, out alignment, out var positionError, out var angleError) &&
			positionError <= .05f && angleError <= 5;

		private bool IsCurrentReconciliation(MapSpace remote, int operation, Guid context) =>
			SyncBus.Active && !SyncBus.IsAuthority && operation == generation && context == visit.ReferenceContext &&
			remoteIdentity.spaceId == Guid.Parse(remote.id) &&
			remoteIdentity.frameId == Guid.Parse(remote.canonicalFrameId) &&
			Guid.TryParse(spaceDocument.Frame.CanonicalId, out var frameId) && frameId == remoteIdentity.frameId;

		private bool TryFinishSessionReconciliation(MapSpace local, MapSpace remote, GameMap map,
			Pose offset, int operation, Guid context)
		{
			if (!IsCurrentReconciliation(remote, operation, context))
				return false;
			if (!documents.SpaceStore.TryGet(local.id, out var latestLocal) ||
				latestLocal.storageFrameId != local.storageFrameId ||
				latestLocal.canonicalFrameId != local.canonicalFrameId || latestLocal.frameRevision != local.frameRevision)
				return false;

			var latestRemote = remote.Clone();
			preservePrivateReferences(latestRemote);
			return TryAdopt(latestLocal, latestRemote, map, offset);
		}

		private static Method ReferenceMethod(MapSpace space) =>
			space.anchors.Count > 0 ? Method.MetaSharedAnchor : Method.AprilTag;

		private static MapSpace ProbeReferenceSet(MapSpace space)
		{
			var copy = space.Clone();
			if (copy.anchors.Count == 0)
				copy.anchors = space.AllAnchors().ToList();
			if (copy.anchors.Count > 0 || copy.HasTags)
				return copy;

			var retainedTags = copy.retainedReferences.FirstOrDefault(reference => reference.tags.Count > 0);
			if (retainedTags != null)
			{
				copy.tags = new(retainedTags.tags);
				copy.tagSizeCm = retainedTags.tagSizeCm;
			}
			return copy;
		}

		[Serializable]
		private sealed class CachedSession
		{
			public MapSpace space;
			public GameMap map;
		}

		private bool CacheSession(MapSpace space, GameMap map) => space == null || map == null ||
			AtomicJsonFile.Write(Path.Combine(documents.DirectoryPath, "session-cache", space.id + ".json"),
				JsonUtility.ToJson(new CachedSession { space = space, map = map }, true));

		public void RestoreAfterSession()
		{
			InvalidateIncoming();
			if (lifecycle.Phase == MapPhase.Stopped)
				return;

			bool preserveFrame = !transientSession;
			int operation = lifecycle.BeginRestore();
			if (transientSession)
			{
				spaceDocument.Load(beforeSessionSpace);
				mapDocument.Load(beforeSessionMap);
				transientSession = false;
			}
			RebuildAfterSession(operation, lifetime, preserveFrame);
		}

		private async void RebuildAfterSession(int operation, CancellationToken token, bool preserveFrame)
		{
			try
			{
				do
				{
					await Awaitable.NextFrameAsync(token);
					if (!lifecycle.IsCurrentRestore(operation))
						return;
				}
				while (NetworkManager.Singleton &&
					(NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ShutdownInProgress));

				lifecycle.EnterLocal();
				remoteIdentity = default;
				Restored.Invoke(preserveFrame);
			}
			catch (OperationCanceledException) { }
		}
	}
}
