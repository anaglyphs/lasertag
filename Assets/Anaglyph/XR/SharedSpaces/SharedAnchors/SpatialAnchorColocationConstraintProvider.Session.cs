using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.Netcode.SyncVariables;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.SharedSpaces.SharedAnchors
{
	public struct AnchorMinterAssignment
	{
		public bool assigned;
		public ulong clientId;
		public Guid generation;
	}

	public partial class SpatialAnchorColocationConstraintProvider
	{
		private struct AnchorOperation
		{
			public Guid id;
			public Guid assignment;
			public Guid context;
		}

		private struct AnchorProposal
		{
			public AnchorOperation operation;
			public Guid guid;
			public Pose canon;
			public int bindingId;
			public int trackingGeneration;
		}

		private struct AnchorReply
		{
			public Guid id;
			public bool accepted;
		}

		private sealed class PendingMint
		{
			public AnchorLease lease;
			public AnchorOperation operation;
			public double deadline;
		}

		private readonly SyncVariable<AnchorMinterAssignment> minter = new("colocation.anchors.minter");
		private readonly SyncVariable<Guid> referenceContext = new("colocation.anchors.context");
		private readonly SyncVariable<AnchorOperation> preparation = new("colocation.anchors.prepare");
		private readonly SyncEvent<AnchorProposal> mintProposal = new("colocation.anchors.mint", EventRoute.ToAuthority);
		private readonly SyncEvent<AnchorReply> mintReply = new("colocation.anchors.mint-result", EventRoute.ViaAuthority);
		private readonly SyncEvent<AnchorProposal> preparedAnchor = new("colocation.anchors.prepared-anchor", EventRoute.ToAuthority);
		private readonly SyncEvent<AnchorReply> preparationDone = new("colocation.anchors.prepared", EventRoute.ToAuthority);
		private readonly Dictionary<Guid, PendingMint> pendingMints = new();
		private readonly List<Guid> expiredMints = new();
		private readonly List<AnchorLease> preparationLeases = new();
		private readonly HashSet<Guid> preparationSaves = new();
		private readonly HashSet<Guid> publishedPreparationSaves = new();
		private readonly List<AnchorConstraintData> preparedConstraints = new();
		private CancellationTokenSource preparationLifetime;
		private bool preparationComplete;
		private int localFrameGeneration;
		private bool wasLocallyReady;

		/// <summary>Embedding-layer selection. Without it the sync authority is the minter.</summary>
		public Func<ulong?> SelectMinter { get; set; }
		public Func<bool> LocalReadinessGate { get; set; }
		public Func<ulong, bool> ValidateRemoteMint { get; set; }
		public Func<int> TrackingGeneration { get; set; }
		public Func<ulong, int, bool> ValidateTrackingGeneration { get; set; }
		public Func<bool> PreparationGate { get; set; }
		public Func<bool> PreparationMintingGate { get; set; }
		public Func<IReadOnlyList<AnchorConstraintData>> SharingCandidates { get; set; }
		public Func<ulong, AnchorConstraintData, bool> ValidatePreparedAnchor { get; set; }
		public AnchorMinterAssignment Minter => minter.Value;
		public bool HasMinter => SyncBus.Active && Minter.assigned;
		public bool IsLocalMinter => !SyncBus.Active || (Minter.assigned && Minter.clientId == SyncBus.LocalClientId);
		private bool LocallyReady => IsAvailable && (LocalReadinessGate?.Invoke() ?? true);
		public event Action MinterChanged = delegate { };

		private void RegisterSession()
		{
			minter.Validate = (_, _) => false;
			referenceContext.Validate = (_, _) => false;
			preparation.Validate = (_, _) => false;
			mintReply.Validate = (sender, _) => SyncBus.IsAuthority && sender == SyncBus.LocalClientId;
			minter.Register();
			referenceContext.Register();
			preparation.Register();
			mintProposal.Register();
			mintReply.Register();
			preparedAnchor.Register();
			preparationDone.Register();
			minter.Changed += OnMinterChanged;
			preparation.Changed += OnPreparationChanged;
			preparation.Synced += ResumePreparation;
			mintProposal.Received += OnMintProposed;
			mintReply.Received += OnMintReply;
			preparedAnchor.Received += OnAnchorPrepared;
			preparationDone.Received += OnPreparationDone;
			MainXRRig.Recentered += InvalidateLocalFrame;
		}

		private void UnregisterSession()
		{
			MainXRRig.Recentered -= InvalidateLocalFrame;
			minter.Changed -= OnMinterChanged;
			preparation.Changed -= OnPreparationChanged;
			preparation.Synced -= ResumePreparation;
			mintProposal.Received -= OnMintProposed;
			mintReply.Received -= OnMintReply;
			preparedAnchor.Received -= OnAnchorPrepared;
			preparationDone.Received -= OnPreparationDone;
			minter.Unregister();
			referenceContext.Unregister();
			preparation.Unregister();
			mintProposal.Unregister();
			mintReply.Unregister();
			preparedAnchor.Unregister();
			preparationDone.Unregister();
			ReleasePreparation();
			foreach (PendingMint pending in pendingMints.Values) pending.lease.Dispose();
			pendingMints.Clear();
		}

		private void Update()
		{
			if (SyncBus.Active && SyncBus.IsAuthority)
				AssignMinter(SelectMinter != null ? SelectMinter() : SyncBus.LocalClientId);
			bool ready = LocallyReady;
			if (wasLocallyReady && !ready) InvalidateLocalFrame();
			wasLocallyReady = ready;
			expiredMints.Clear();
			foreach (var pair in pendingMints)
				if (!OperationIsCurrent(pair.Value.operation) || Time.realtimeSinceStartupAsDouble >= pair.Value.deadline)
					expiredMints.Add(pair.Key);
			// A sent request may have committed before a disconnect. Keep its local save when
			// acknowledgement is uncertain; erasing it could destroy a published reference.
			foreach (Guid guid in expiredMints) ReleasePendingMint(guid, erase: false);
		}

		public void AssignMinter(ulong? clientId)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			if (Minter.assigned == clientId.HasValue && (!clientId.HasValue || Minter.clientId == clientId.Value)) return;
			minter.Value = new AnchorMinterAssignment
			{
				assigned = clientId.HasValue, clientId = clientId.GetValueOrDefault(), generation = Guid.NewGuid()
			};
		}

		private void OnMinterChanged(AnchorMinterAssignment _, AnchorMinterAssignment __)
		{
			InvalidateLocalFrame();
			if (IsRunning && IsLocalMinter) ShareAllAfterActivation();
			MinterChanged.Invoke();
		}

		public void InvalidateLocalFrame() => localFrameGeneration++;
		private void OnApplicationFocus(bool focused) { if (!focused) InvalidateLocalFrame(); }
		private void OnApplicationPause(bool paused) { if (paused) InvalidateLocalFrame(); }

		public void InvalidateReferenceContext()
		{
			InvalidateLocalFrame();
			if (SyncBus.IsAuthority) referenceContext.Value = Guid.NewGuid();
		}

		private AnchorOperation NewOperation() => new()
		{
			id = Guid.NewGuid(), assignment = Minter.generation, context = referenceContext.Value
		};

		private bool OperationIsCurrent(AnchorOperation operation) => SyncBus.Active && Minter.assigned &&
			operation.id != Guid.Empty && operation.assignment == Minter.generation && operation.context == referenceContext.Value;

		private bool SenderIsCurrent(ulong sender, AnchorOperation operation) =>
			SyncBus.IsAuthority && OperationIsCurrent(operation) && sender == Minter.clientId;

		private static bool ValidConstraint(Guid guid, Pose pose) => guid != Guid.Empty &&
			float.IsFinite(pose.position.x) && float.IsFinite(pose.position.y) && float.IsFinite(pose.position.z) &&
			float.IsFinite(pose.rotation.x) && float.IsFinite(pose.rotation.y) &&
			float.IsFinite(pose.rotation.z) && float.IsFinite(pose.rotation.w) &&
			Mathf.Abs(Quaternion.Dot(pose.rotation, pose.rotation) - 1f) < 0.01f;

		private bool CanAcceptMint(ulong sender, AnchorProposal proposal) =>
			SenderIsCurrent(sender, proposal.operation) && IsRunning && RoamingMintEnabled &&
			proposal.bindingId == -1 && ValidConstraint(proposal.guid, proposal.canon) &&
			!constraints.ContainsKey(proposal.guid) && (ValidateTrackingGeneration?.Invoke(sender, proposal.trackingGeneration) ?? true) && (ValidateRemoteMint?.Invoke(sender) ?? true);

		private void OnMintProposed(ulong sender, AnchorProposal proposal)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority) return;
			bool accepted = CanAcceptMint(sender, proposal);
			if (accepted) constraints.Set(proposal.guid, new AnchorConstraintState { canonPose = proposal.canon, bindingId = -1 });
			mintReply.Raise(new AnchorReply { id = proposal.guid, accepted = accepted });
		}

		private void OnMintReply(ulong _, AnchorReply reply) => ReleasePendingMint(reply.id, erase: !reply.accepted);

		private async void ReleasePendingMint(Guid guid, bool erase)
		{
			if (!pendingMints.Remove(guid, out PendingMint pending)) return;
			pending.lease.Dispose();
			if (!erase || constraints.ContainsKey(guid)) return;
			try { await EraseAsync(guid); }
			catch (Exception e) { Debug.LogException(e); }
		}

		/// <summary>Runs preparation on the minter. FinishSessionSharing must follow commit or failure.</summary>
		public async Awaitable<List<AnchorConstraintData>> PrepareSessionSharingAsync(CancellationToken token)
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !HasMinter) return new();
			preparedConstraints.Clear();
			preparationComplete = false;
			AnchorOperation operation = NewOperation();
			preparation.Value = operation;
			while (!preparationComplete)
			{
				token.ThrowIfCancellationRequested();
				if (!OperationIsCurrent(operation) || preparation.Value.id != operation.id) return new();
				await Awaitable.NextFrameAsync(token);
			}
			token.ThrowIfCancellationRequested();
			if (!OperationIsCurrent(operation) || preparation.Value.id != operation.id) return new();
			return new List<AnchorConstraintData>(preparedConstraints);
		}

		public void FinishSessionSharing()
		{
			if (SyncBus.IsAuthority) preparation.Value = default;
		}

		private void OnPreparationChanged(AnchorOperation _, AnchorOperation __)
		{
			ReleasePreparation();
			ResumePreparation();
		}

		private void ResumePreparation()
		{
			if (preparationLifetime != null || !IsLocalMinter || !OperationIsCurrent(preparation.Value)) return;
			preparationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCtknSrc.Token);
			PrepareOnMinter(preparation.Value, preparationLifetime.Token);
		}

		private void ReleasePreparation()
		{
			preparationLifetime?.Cancel();
			preparationLifetime?.Dispose();
			preparationLifetime = null;
			foreach (AnchorLease lease in preparationLeases) lease.Dispose();
			preparationLeases.Clear();
			foreach (Guid guid in preparationSaves)
				if (SyncBus.Active && !publishedPreparationSaves.Contains(guid) && !constraints.ContainsKey(guid))
					EraseRejectedPreparation(guid);
			preparationSaves.Clear();
			publishedPreparationSaves.Clear();
		}

		private async void EraseRejectedPreparation(Guid guid)
		{
			try { await EraseAsync(guid); }
			catch (Exception e) { Debug.LogException(e); }
		}

		private bool CanPrepare(AnchorOperation operation, int frame) => IsLocalMinter && LocallyReady &&
			CanShareAnchors && frame == localFrameGeneration && OperationIsCurrent(operation) &&
			preparation.Value.id == operation.id && (PreparationGate?.Invoke() ?? true);

		private IReadOnlyList<AnchorConstraintData> GetSharingCandidates()
		{
			if (SharingCandidates != null) return SharingCandidates();
			List<AnchorConstraintData> result = new();
			foreach (var pair in constraints) result.Add(new(pair.Key, pair.Value.canonPose, pair.Value.bindingId));
			return result;
		}

		private async void PrepareOnMinter(AnchorOperation operation, CancellationToken token)
		{
			int frame = localFrameGeneration;
			try
			{
				// Start after the request has been broadcast, including on a headset host.
				await Awaitable.NextFrameAsync(token);
				if (!CanPrepare(operation, frame)) return;
				HashSet<Guid> uploaded = new();
				foreach (AnchorConstraintData candidate in GetSharingCandidates())
				{
					if (!CanPrepare(operation, frame)) return;
					AnchorLease lease = registry.Acquire(ToSerializable(candidate.guid), AnchorSource.Any);
					preparationLeases.Add(lease);
					if (lease.Handle.anchor == null || lease.Handle.anchor.trackingState != TrackingState.Tracking) continue;
					XRResultStatus result = await registry.TryShareAsync(lease, token);
					if (!CanPrepare(operation, frame)) return;
					if (!result.IsError()) uploaded.Add(candidate.guid);
				}
				// Tag corrections can change canon poses while uploads run. Read them again.
				foreach (AnchorConstraintData candidate in GetSharingCandidates())
					if (uploaded.Contains(candidate.guid)) SendPrepared(operation, candidate);
				if (uploaded.Count == 0 && (PreparationMintingGate?.Invoke() ?? false))
				{
					Pose pose = PoseUnderPlayer();
					await AnchorMinting.TryMintWithAsyncCommit(registry, pose, async minted =>
					{
						if (!CanPrepare(operation, frame)) return false;
						if (!await ShareBeforePublishing(minted.lease, () => CanPrepare(operation, frame), token)) return false;
						preparationLeases.Add(minted.lease);
						if (minted.saved) preparationSaves.Add(minted.guid);
						SendPrepared(operation, new(minted.guid, pose));
						return true;
					}, commitTakesLease: true, token);
				}
			}
			catch (OperationCanceledException) { }
			catch (Exception e) { Debug.LogException(e); }
			finally
			{
				if (OperationIsCurrent(operation) && preparation.Value.id == operation.id && IsLocalMinter)
					preparationDone.Raise(new AnchorReply { id = operation.id, accepted = CanPrepare(operation, frame) });
			}
		}

		private void SendPrepared(AnchorOperation operation, AnchorConstraintData candidate) => preparedAnchor.Raise(new()
		{
			operation = operation, guid = candidate.guid, canon = candidate.canonPose, bindingId = candidate.bindingId, trackingGeneration = TrackingGeneration?.Invoke() ?? 0
		});

		private void OnAnchorPrepared(ulong sender, AnchorProposal proposal)
		{
			AnchorConstraintData data = new(proposal.guid, proposal.canon, proposal.bindingId);
			if (!SenderIsCurrent(sender, proposal.operation) || preparationComplete ||
				preparation.Value.id != proposal.operation.id || !ValidConstraint(proposal.guid, proposal.canon) ||
				!(ValidateTrackingGeneration?.Invoke(sender, proposal.trackingGeneration) ?? true) || !(ValidatePreparedAnchor?.Invoke(sender, data) ?? true)) return;
			if (preparedConstraints.Exists(entry => entry.guid == proposal.guid)) return;
			preparedConstraints.Add(data);
		}

		private void OnPreparationDone(ulong sender, AnchorReply reply)
		{
			if (!SenderIsCurrent(sender, preparation.Value) || reply.id != preparation.Value.id) return;
			if (!reply.accepted) preparedConstraints.Clear();
			preparationComplete = true;
		}
	}
}
