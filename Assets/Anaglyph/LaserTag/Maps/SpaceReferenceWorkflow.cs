using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR.SharedSpaces.AprilTags;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Netcode;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>Owns reference authoring and authority commits; the alignment controller owns local handoffs.</summary>
	internal sealed class SpaceReferenceWorkflow : IDisposable
	{
		private readonly MapCatalog documents;
		private MapWorkingCopy mapDocument => documents.MapDocument;
		private MapSpaceWorkingCopy spaceDocument => documents.SpaceDocument;
		private readonly ColocationManager colocationManager;
		private readonly MapSpaceColocationAdapter references;
		private readonly MapSpaceAlignmentController alignment;
		private readonly MapSpaceTransitionSync transitions;
		private readonly MapVisitContext visit;
		private readonly CancellationToken lifetimeToken;
		private readonly Func<bool> trackingReady;
		private readonly Func<Guid> sessionSpaceId;
		private CancellationTokenSource nativePreparation;
		private MapSpace staged;
		private SpaceTransitionCommand transitionCommand;
		private bool applyingReferences;
		private string registrationRequestedFor;
		private readonly List<KeyValuePair<ulong, HeadsetReadiness>> anchorCandidates = new();
		private Guid referenceContext => visit.ReferenceContext;
		private Guid scan
		{
			get => visit.ScanContext;
			set => visit.ScanContext = value;
		}
		private Guid CanonicalFrameId => Guid.TryParse(spaceDocument.Frame.CanonicalId, out var id) ? id : Guid.Empty;
		private Guid SessionSpaceId => sessionSpaceId();
		private MapSpace CurrentSpace => spaceDocument.CurrentSpace;
		private bool Authority => !SyncBus.Active || SyncBus.IsAuthority;
		private bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;
		public bool IsChangingColocation => alignment.Busy;
		public bool WaitingForAlignmentAuthor => !alignment.Busy && spaceDocument.HasPendingSetup;
		public MapSpace Candidate => staged?.Clone();
		public Method TargetMethod => transitionCommand.target;
		public Func<bool> TagRegistrationAllowed { get; set; }
		public Func<Guid> TagSetupOperation { get; set; }
		public event Action SpaceChanged = delegate { };
		public event Action SettingsChanged = delegate { };
		public event Action FrameChanged = delegate { };
		public event Action DocumentsCommitted = delegate { };
		public event Action PrivateReferencesChanged = delegate { };
		public event Action CaptureObjectsRequested = delegate { };
		public event Action SaveRequested = delegate { };
		public event Action TagRegistrationRequested = delegate { };
		public event Action RequestRejected = delegate { };

		public SpaceReferenceWorkflow(MapCatalog documents, ColocationManager colocationManager,
			MapSpaceColocationAdapter references, MapSpaceAlignmentController alignment,
			MapSpaceTransitionSync transitions, MapVisitContext visit, CancellationToken lifetimeToken,
			Func<bool> trackingReady, Func<Guid> sessionSpaceId)
		{
			this.documents = documents;
			this.colocationManager = colocationManager;
			this.references = references;
			this.alignment = alignment;
			this.transitions = transitions;
			this.visit = visit;
			this.lifetimeToken = lifetimeToken;
			this.trackingReady = trackingReady;
			this.sessionSpaceId = sessionSpaceId;
		}

		public void Register()
		{
			ConfigureAnchorMinter();
			alignment.Validated += OnLocallyValidated;
			alignment.Changed += OnAlignmentChanged;
			colocationManager.TwoTagProvider.PairSelected += OnPairSelected;
			if (colocationManager.TagProvider)
				colocationManager.TagProvider.ReferenceEditGate = _ => false;
			transitions.RequestRejected += OnRequestRejected;
			transitions.MethodRequested += OnMethodRequested;
			transitions.ReferenceRequested += OnReferenceRequested;
			transitions.Received += OnTransitionReceived;
			transitions.Validated += OnTargetValidated;
			transitions.Register();
		}

		private void OnAlignmentChanged() => SettingsChanged.Invoke();
		private bool TrackingReady() => trackingReady();
		private bool CheckReferenceFrameAgreement() => spaceDocument.HasSpace && TrackingReady() && colocationManager.ReferenceAligned;
		private bool CanAuthorReferences => TrackingReady() && spaceDocument.HasSpace &&
			MapPolicy.CanAuthorReferences(spaceDocument.HasReferences, colocationManager.ActiveMethod, CheckReferenceFrameAgreement(), colocationManager.AnchorProvider.IsLocalMinter);
		private bool CanAuthorTags => TrackingReady() && spaceDocument.HasSpace &&
			MapPolicy.CanAuthorReferences(spaceDocument.HasReferences, colocationManager.ActiveMethod, CheckReferenceFrameAgreement(),
				MapPolicy.CanInitializeTags(SyncBus.Active, Authority, alignment.Busy,
					transitionCommand.target == Method.AprilTag && transitionCommand.author == SyncBus.LocalClientId));

		public bool CanMintAnchors()
		{
			if (!CanAuthorReferences)
				return false;
			if (WaitingForAlignmentAuthor && spaceDocument.PendingSetupMethod != Method.MetaSharedAnchor)
				return false;
			return !alignment.Busy || alignment.Transition.CreatesReferences &&
				alignment.Transition.Target == Method.MetaSharedAnchor && (staged?.anchors.Count ?? 0) == 0;
		}

		public void Tick()
		{
			ApplyReferenceChanges();
			ResumePendingSetup();
			alignment.Tick();
			RequestTagRegistrationIfNeeded();
		}

		public void ClearPublishedTransition() => transitions.Publish(default, null);

		public void Dispose()
		{
			nativePreparation?.Cancel();
			ClearAnchorMinter();
			alignment.Validated -= OnLocallyValidated;
			alignment.Changed -= OnAlignmentChanged;
			colocationManager.TwoTagProvider.PairSelected -= OnPairSelected;
			transitions.RequestRejected -= OnRequestRejected;
			transitions.MethodRequested -= OnMethodRequested;
			transitions.ReferenceRequested -= OnReferenceRequested;
			transitions.Received -= OnTransitionReceived;
			transitions.Validated -= OnTargetValidated;
			transitions.Unregister();
		}

		// Reference revisions are staged separately; the active observer retains its previous target set.
		private void ApplyReferenceChanges()
		{
			if (applyingReferences || !spaceDocument.HasSpace || !references.HasPendingSnapshots)
				return;

			var current = CurrentSpace;
			var snapshot = references.TakePendingSnapshot((staged ?? current).Clone(), new SpaceReferenceCapture
			{
				Anchors = Authority && colocationManager.AnchorProvider.IsRunning &&
					(staged != null ? transitionCommand.target == Method.MetaSharedAnchor :
					 !alignment.Busy && !current.hasPendingSetup),
				TaggedAnchors = colocationManager.TagProvider.IsRunning
			});
			if (snapshot == null)
				return;

			// Realizations are private and do not revise or replace the host's tag definitions.
			if (staged != null)
				staged.localAnchors = snapshot.localAnchors;
			else if (!current.localAnchors.SequenceEqual(snapshot.localAnchors))
			{
				current.localAnchors = snapshot.localAnchors.Where(current.IsCompatibleLocalAnchor).ToList();
				spaceDocument.ApplySnapshot(current);
				SaveRequested.Invoke();
			}
			if (!Authority || snapshot.anchors.SequenceEqual((staged ?? current).anchors))
				return;
			if (staged != null && transitionCommand.target != Method.MetaSharedAnchor)
				return;
			if (staged == null && !BeginTransition(Method.MetaSharedAnchor, ReferenceTransitionIntent.SetupOnly, true, SelectAuthor(true)))
				return;

			staged.anchors = snapshot.anchors;
			PublishCandidate();
		}

		private ulong? SelectAuthor(bool creates, Method target = Method.MetaSharedAnchor)
		{
			bool canAuthor = target == Method.AprilTag ? CanAuthorTags : CanAuthorReferences;
			if (!SyncBus.Active)
				return TrackingReady() && (!creates || canAuthor) ? SyncBus.LocalClientId : null;
			if (!HeadsetConfiguration.IsOperatorDevice && (!creates || canAuthor) && TrackingReady())
				return SyncBus.LocalClientId;
			foreach (var pair in PlayerAvatar.All.OrderBy(pair => pair.Key))
				if (ReadyForReferenceWork(pair.Key, creates, target))
					return pair.Key;
			return null;
		}

		private bool BeginTransition(Method target, ReferenceTransitionIntent intent, bool creates, ulong? author)
		{
			if (!Authority || !spaceDocument.HasSpace || staged != null || alignment.Busy || !author.HasValue)
				return false;

			var source = CurrentSpace;
			source.hasPendingSetup = true;
			source.pendingSetupMethod = target;
			source.pendingSetupIntent = intent;
			source.MarkChanged();
			if (!documents.SpaceStore.Save(source))
				return false;

			spaceDocument.Load(source);
			staged = source.Clone();
			transitionCommand = new()
			{
				operation = Guid.NewGuid(),
				referenceContext = referenceContext,
				target = target,
				intent = intent,
				createsReferences = creates,
				author = author.Value
			};
			alignment.RequireNewTagObservations = transitionCommand.author == SyncBus.LocalClientId;
			alignment.Begin(transitionCommand.operation, source, target, intent, creates);
			registrationRequestedFor = null;
			PublishCandidate();
			DocumentsCommitted.Invoke();
			return true;
		}

		private void PublishCandidate()
		{
			if (staged == null)
				return;
			transitionCommand.revision = Guid.NewGuid();
			applyingReferences = true;
			references.Inject(staged);
			references.ClearPendingSnapshots();
			applyingReferences = false;
			alignment.SetCandidate(transitionCommand.revision, staged);
			transitions.Publish(transitionCommand, staged);
			SettingsChanged.Invoke();
		}

		private static MapSpace CandidateInStorage(MapSpace canonical, MapSpace local)
		{
			var copy = local.Clone();
			copy.tags = canonical.tags.ConvertAll(tag => new MapTagEntry
			{
				id = tag.id,
				canonPose = local.Frame.ToStorage(tag.canonPose)
			});
			copy.anchors = canonical.anchors.ConvertAll(anchor => new MapAnchorEntry
			{
				guid = anchor.guid,
				tagId = anchor.tagId,
				canonPose = local.Frame.ToStorage(anchor.canonPose)
			});
			copy.tagSizeCm = canonical.tagSizeCm;
			return copy;
		}
		private void RememberCommittedPrivateAnchors(MapSpace candidate)
		{
			var current = CurrentSpace;
			if (current == null || candidate.id != current.id || candidate.storageFrameId != current.storageFrameId ||
				candidate.canonicalFrameId != current.canonicalFrameId || candidate.frameRevision != current.frameRevision)
				return;

			// The transition header can precede the map-identity callback in a combined snapshot.
			// Retain these private records with their tag metadata until that committed definition arrives.
			for (int i = candidate.localAnchors.Count - 1; i >= 0; i--)
			{
				var anchor = candidate.localAnchors[i];
				if (candidate.IsCompatibleLocalAnchor(anchor))
					MapSpaceColocationAdapter.PrioritizePrivateAnchor(current.localAnchors, anchor);
			}
			spaceDocument.ApplySnapshot(current);
			PrivateReferencesChanged.Invoke();
		}
		public void PreserveCurrentPrivateReferences(MapSpace incoming)
		{
			var privateReferences = references.CapturePrivateReferences(staged ?? CurrentSpace);
			if (staged != null)
				MapSpaceColocationAdapter.CopyCompatiblePrivateAnchors(staged, privateReferences);
			MapSpaceColocationAdapter.CopyCompatiblePrivateAnchors(incoming, privateReferences);
		}

		private void OnTransitionReceived(SpaceTransitionCommand command, MapSpace candidate)
		{
			if (command.operation == Guid.Empty)
			{
				if (transitionCommand.operation != Guid.Empty && !transitionCommand.committed)
					CancelTransitionInternal();
				transitionCommand = default;
				return;
			}
			if (command.referenceContext != referenceContext || !spaceDocument.HasSpace || command.space.frameId != CanonicalFrameId)
				return;

			var current = CurrentSpace;
			var privateReferences = references.CapturePrivateReferences(
				transitionCommand.operation == command.operation ? staged ?? current : current);
			transitionCommand = command;
			alignment.RequireNewTagObservations = command.author == SyncBus.LocalClientId;
			alignment.Begin(command.operation, current, command.target, command.intent, command.createsReferences);
			staged = CandidateInStorage(candidate, current);
			MapSpaceColocationAdapter.CopyCompatiblePrivateAnchors(staged, privateReferences);
			applyingReferences = true;
			references.Inject(staged);
			references.ClearPendingSnapshots();
			applyingReferences = false;
			alignment.SetCandidate(command.revision, staged);
			if (command.committed)
			{
				alignment.Commit();
				RememberCommittedPrivateAnchors(staged);
				staged = null;
			}
		}

		private void OnLocallyValidated(Guid operation, Guid revision)
		{
			if (transitionCommand.author != SyncBus.LocalClientId)
				return;

			SpaceTransitionEvidence evidence = new()
			{
				operation = operation,
				revision = revision,
				referenceContext = referenceContext,
				trackingGeneration = colocationManager.TrackingGeneration
			};
			if (Authority)
				OnTargetValidated(SyncBus.LocalClientId, evidence);
			else
				transitions.Report(evidence);
		}

		private void OnTargetValidated(ulong sender, SpaceTransitionEvidence evidence)
		{
			if (!Authority || staged == null || transitionCommand.committed || sender != transitionCommand.author ||
				evidence.trackingGeneration != SenderTrackingGeneration(sender) ||
				evidence.operation != transitionCommand.operation || evidence.revision != transitionCommand.revision ||
				evidence.referenceContext != referenceContext ||
				!ReadyForReferenceWork(sender, transitionCommand.createsReferences, transitionCommand.target))
				return;

			var current = CurrentSpace;
			var saved = current.ApplyReferenceCandidate(staged);
			if (transitionCommand.createsReferences)
				MarkReferencesAuthored(current, saved);
			bool leavesProvisionalFrame = !ColocationManager.UsesSavedReferences(current.preferredColocationMethod) &&
				transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget;
			if (transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget)
				saved.preferredColocationMethod = transitionCommand.target;
			saved.hasPendingSetup = false;
			saved.initializationPending = !saved.HasReferenceBasedData;
			saved.MarkChanged();
			alignment.Transition?.Persisting();
			CaptureObjectsRequested.Invoke();
			if (!mapDocument.Save(SyncBus.Active) || !documents.SpaceStore.Save(saved))
			{
				SaveRequested.Invoke();
				return;
			}

			spaceDocument.Load(saved);
			SpaceChanged.Invoke();
			if (leavesProvisionalFrame)
			{
				scan = Guid.NewGuid();
				FrameChanged.Invoke();
			}
			transitionCommand.committed = true;
			// Publish the configuration before committing selection. Peers may retain their source after this point.
			DocumentsCommitted.Invoke();
			transitions.Publish(transitionCommand, saved);
			if (transitionCommand.intent == ReferenceTransitionIntent.ActivateTarget)
				colocationManager.CommitMethod(transitionCommand.target);
			alignment.Commit(requireLocalHandoff: !HeadsetConfiguration.IsOperatorDevice);
			staged = null;
			colocationManager.AnchorProvider.FinishSessionSharing();
		}

		private static void MarkReferencesAuthored(MapSpace source, MapSpace saved)
		{
			if (source.referenceSourceId != source.id)
			{
				source.RetainActiveReferences();
				saved.retainedReferences = source.retainedReferences;
			}
			saved.referenceSourceId = saved.id;
			saved.referenceVersion = Guid.NewGuid().ToString("N");
			saved.referenceDirty = !SyncBus.Active;
		}
		public void CancelAlignmentTransition()
		{
			if (transitionCommand.committed)
				return;

			if (Authority)
			{
				var space = CurrentSpace;
				if (space != null)
				{
					space.hasPendingSetup = false;
					space.MarkChanged();
					if (!documents.SpaceStore.Save(space))
						return;
					spaceDocument.Load(space);
				}
				CancelTransitionInternal();
				transitions.Publish(default, null);
				SpaceChanged.Invoke();
				DocumentsCommitted.Invoke();
			}
			else
				transitions.RequestMethod(new() { context = referenceContext, cancel = true });
		}
		public void CancelTransitionInternal()
		{
			nativePreparation?.Cancel();
			nativePreparation = null;
			alignment.Cancel();
			staged = null;
			transitionCommand = default;
			colocationManager.AnchorProvider?.FinishSessionSharing();
			var current = CurrentSpace;
			if (current != null)
			{
				references.Inject(current);
				references.ClearPendingSnapshots();
			}
		}
		public string DescribeColocationPreferenceBlocker() => MapPolicy.ColocationPreferenceBlocker(
			spaceDocument.HasSpace, RoundInProgress, HeadsetConfiguration.SessionIsOperatorManaged,
			HeadsetConfiguration.IsOperatorDevice);

		public string DescribeColocationMethodBlocker(Method method) => MapPolicy.ColocationMethodBlocker(
			method, spaceDocument.HasSpace, RoundInProgress, HeadsetConfiguration.SessionIsOperatorManaged,
			HeadsetConfiguration.IsOperatorDevice);

		public bool SetPreferredColocationMethod(Method method)
		{
			if (DescribeColocationMethodBlocker(method) != null)
				return false;

			var request = new SpaceMethodRequest { context = referenceContext, method = method };
			if (Authority)
			{
				bool accepted = RequestMethod(SyncBus.LocalClientId, request);
				if (!accepted)
					RejectRequest(SyncBus.LocalClientId, request.context, Guid.Empty);
				return accepted;
			}
			transitions.RequestMethod(request);
			return true;
		}

		private void OnMethodRequested(ulong sender, SpaceMethodRequest request)
		{
			if (Authority && !RequestMethod(sender, request))
				RejectRequest(sender, request.context, Guid.Empty);
		}

		private void RejectRequest(ulong sender, Guid context, Guid operation)
		{
			var rejection = new SpaceRequestRejection { recipient = sender, context = context, operation = operation };
			if (!SyncBus.Active || sender == SyncBus.LocalClientId)
				OnRequestRejected(SyncBus.LocalClientId, rejection);
			else
				transitions.Reject(rejection);
		}
		private void OnRequestRejected(ulong _, SpaceRequestRejection rejection)
		{
			if (rejection.recipient != SyncBus.LocalClientId || rejection.context != referenceContext ||
				rejection.operation != (alignment.Busy ? transitionCommand.operation : Guid.Empty))
				return;
			RequestRejected.Invoke();
			SettingsChanged.Invoke();
		}
		private bool RequestMethod(ulong sender, SpaceMethodRequest request)
		{
			if (request.context != referenceContext || MapPolicy.ColocationPreferenceBlocker(spaceDocument.HasSpace,
				RoundInProgress, HeadsetConfiguration.SessionIsOperatorManaged, sender == SyncBus.LocalClientId) != null)
				return false;

			if (request.cancel)
			{
				CancelAlignmentTransition();
				return true;
			}
			if (!ColocationManager.IsValidMethod(request.method))
				return false;

			CancelTransitionInternal();
			transitions.Publish(default, null);
			var current = CurrentSpace;
			if (!ColocationManager.UsesSavedReferences(request.method))
			{
				current.hasPendingSetup = false;
				current.preferredColocationMethod = request.method;
				current.MarkChanged();
				if (!documents.SpaceStore.Save(current))
					return false;

				spaceDocument.Load(current);
				SpaceChanged.Invoke();
				scan = Guid.NewGuid();
				FrameChanged.Invoke();
				DocumentsCommitted.Invoke();
				colocationManager.CommitMethod(request.method);
				alignment.Activate(current, request.method);
				return true;
			}
			bool setup = !MapSpaceAlignmentController.HasConfiguration(current, request.method);
			var author = SelectAuthor(setup, request.method);
			if (!author.HasValue)
			{
				current.hasPendingSetup = true;
				current.pendingSetupMethod = request.method;
				current.pendingSetupIntent = ReferenceTransitionIntent.ActivateTarget;
				current.MarkChanged();
				if (!documents.SpaceStore.Save(current))
					return false;

				spaceDocument.Load(current);
				SpaceChanged.Invoke();
				DocumentsCommitted.Invoke();
				return true;
			}
			if (!BeginTransition(request.method, ReferenceTransitionIntent.ActivateTarget, setup, author))
				return false;
			if (request.method == Method.MetaSharedAnchor && setup)
				PrepareAnchors(transitionCommand.operation);
			return true;
		}
		private async void PrepareAnchors(Guid operation)
		{
			if (!SyncBus.Active)
				return;

			var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
			nativePreparation = cancellation;
			cancellation.CancelAfter(TimeSpan.FromSeconds(20));
			try
			{
				var prepared = await colocationManager.AnchorProvider.PrepareSessionSharingAsync(cancellation.Token);
				if (cancellation.IsCancellationRequested || staged == null || transitionCommand.operation != operation)
					return;
				foreach (var anchor in prepared)
					staged.SetAnchorWithTag(anchor.guid.ToString("N"), staged.Frame.ToStorage(anchor.canonPose), anchor.bindingId);
				if (prepared.Count == 0)
				{
					CancelAlignmentTransition();
					return;
				}
				PublishCandidate();
			}
			catch (OperationCanceledException)
			{
				if (transitionCommand.operation == operation)
					CancelAlignmentTransition();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
				if (transitionCommand.operation == operation)
					CancelAlignmentTransition();
			}
			finally
			{
				if (nativePreparation == cancellation)
					nativePreparation = null;
				cancellation.Dispose();
			}
		}

		public bool SessionIsWaitingOnFirstTag => alignment.Busy && alignment.Transition.Target == Method.AprilTag &&
			alignment.Transition.CreatesReferences && !(staged?.HasTags ?? spaceDocument.HasTags);

		public void CheckTransitionAuthor()
		{
			if (!SyncBus.Active || !SyncBus.IsAuthority || !alignment.Busy ||
				transitionCommand.committed || transitionCommand.author == SyncBus.LocalClientId)
				return;

			var network = NetworkManager.Singleton;
			if (network && network.ConnectedClientsIds.Contains(transitionCommand.author))
				return;
			CancelTransitionInternal();
			transitions.Publish(default, null);
		}

		private void ResumePendingSetup()
		{
			if (!Authority || alignment.Busy || staged != null || RoundInProgress || !spaceDocument.HasPendingSetup || documents.HasPending)
				return;

			var space = CurrentSpace;
			bool creates = !MapSpaceAlignmentController.HasConfiguration(space, space.pendingSetupMethod);
			var author = SelectAuthor(creates, space.pendingSetupMethod);
			if (!author.HasValue)
				return;
			if (BeginTransition(space.pendingSetupMethod, space.pendingSetupIntent, creates, author) &&
				creates && space.pendingSetupMethod == Method.MetaSharedAnchor)
				PrepareAnchors(transitionCommand.operation);
		}

		private void RequestTagRegistrationIfNeeded()
		{
			if (!SessionIsWaitingOnFirstTag || !CanAuthorTags ||
				transitionCommand.author != SyncBus.LocalClientId || HeadsetConfiguration.IsOperatorDevice)
				return;

			string operation = transitionCommand.operation.ToString();
			if (registrationRequestedFor == operation)
				return;
			registrationRequestedFor = operation;
			TagRegistrationRequested.Invoke();
		}
		public string DescribeTagSetupBlocker() => DescribeTagRegistrationBlocker();
		public bool CanRegisterTags(ulong sender) => ReadyForReferenceWork(sender, true, Method.AprilTag) &&
			(!alignment.Busy || transitionCommand.target == Method.AprilTag && transitionCommand.author == sender);
		public string DescribeTagRegistrationBlocker() => MapPolicy.TagRegistrationBlocker(CanAuthorTags);
		public string DescribeTagRemovalBlocker() => MapPolicy.TagRemovalBlocker(spaceDocument.HasSpace, IsChangingColocation);
		public string DescribeTagSizeBlocker() =>
			MapPolicy.TagSizeBlocker(spaceDocument.HasSpace, spaceDocument.HasTags || (staged?.HasTags ?? false));
		public float EffectiveTagSizeCm => staged?.tagSizeCm ?? CurrentSpace?.tagSizeCm ?? MapSpace.DefaultTagSizeCm;

		private void SubmitReference(SpaceReferenceRequest request)
		{
			request.context = referenceContext;
			request.setupOperation = TagSetupOperation?.Invoke() ?? Guid.Empty;
			request.trackingGeneration = colocationManager.TrackingGeneration;
			request.operation = alignment.Busy ? transitionCommand.operation : Guid.Empty;
			if (Authority)
				OnReferenceRequested(SyncBus.LocalClientId, request);
			else
				transitions.RequestReference(request);
		}

		public bool RegisterTag(int id, Pose worldPose)
		{
			if (DescribeTagRegistrationBlocker() != null || id < 0 || !MapSpaceFrame.ValidPose(worldPose))
				return false;
			SubmitReference(new() { action = SpaceReferenceAction.Register, tag = id, pose = worldPose });
			return true;
		}

		public bool UnregisterTag(int id)
		{
			if (DescribeTagRemovalBlocker() != null)
				return false;
			SubmitReference(new() { action = SpaceReferenceAction.Remove, tag = id });
			return true;
		}

		public void UnregisterAllTags()
		{
			var space = CurrentSpace;
			if (space == null)
				return;
			foreach (var tag in space.tags)
				UnregisterTag(tag.id);
		}

		public bool SetTagSize(float value)
		{
			if (!float.IsFinite(value) || value <= 0 || DescribeTagSizeBlocker() != null)
				return false;
			SubmitReference(new() { action = SpaceReferenceAction.Size, size = value });
			return true;
		}

		private void OnReferenceRequested(ulong sender, SpaceReferenceRequest request)
		{
			if (Authority && !ApplyReferenceRequest(sender, request))
				RejectRequest(sender, request.context, request.operation);
		}

		private bool ApplyReferenceRequest(ulong sender, SpaceReferenceRequest request)
		{
			if (!Authority || request.context != referenceContext || !spaceDocument.HasSpace ||
				request.setupOperation != (TagSetupOperation?.Invoke() ?? Guid.Empty) ||
				request.operation != (alignment.Busy ? transitionCommand.operation : Guid.Empty))
				return false;

			if (request.action == SpaceReferenceAction.Register)
			{
				if (TagRegistrationAllowed?.Invoke() == false)
					return false;
				if (request.tag < 0 || request.trackingGeneration != SenderTrackingGeneration(sender) ||
					!MapSpaceFrame.ValidPose(request.pose) || !ReadyForReferenceWork(sender, true, Method.AprilTag))
					return false;
				if (staged == null && !BeginTransition(Method.AprilTag, ReferenceTransitionIntent.SetupOnly, true, sender))
					return false;
				if (transitionCommand.target != Method.AprilTag || transitionCommand.author != sender)
					return false;

				var candidate = references.CapturePrivateReferences(staged);
				candidate.SetTag(request.tag, candidate.Frame.ToStorage(request.pose));
				var current = CurrentSpace;
				var saved = current.ApplyReferenceCandidate(candidate);
				MarkReferencesAuthored(current, saved);
				saved.initializationPending = false;
				saved.MarkChanged();
				if (!documents.SpaceStore.Save(saved))
					return false;

				spaceDocument.Load(saved);
				staged = saved.Clone();
				transitionCommand.createsReferences = false;
				alignment.Begin(transitionCommand.operation, saved, transitionCommand.target, transitionCommand.intent, false);
				PublishCandidate();
				SpaceChanged.Invoke();
				DocumentsCommitted.Invoke();
				return true;
			}
			if (alignment.Busy)
			{
				if (request.action == SpaceReferenceAction.Size && staged != null &&
					!spaceDocument.HasTags && !staged.HasTags && float.IsFinite(request.size) && request.size > 0)
				{
					staged.tagSizeCm = request.size;
					PublishCandidate();
					return true;
				}
				return false;
			}
			var space = CurrentSpace;
			switch (request.action)
			{
				case SpaceReferenceAction.Remove:
					space.tags.RemoveAll(tag => tag.id == request.tag);
					foreach (var anchor in space.localAnchors.Where(anchor => anchor.tagId == request.tag))
						documents.QueueAnchorErasure(anchor.guid);
					space.localAnchors.RemoveAll(anchor => anchor.tagId == request.tag);
					foreach (var anchor in space.anchors.Where(anchor => anchor.tagId == request.tag))
						documents.QueueAnchorErasure(anchor.guid);
					space.anchors.RemoveAll(anchor => anchor.tagId == request.tag);
					space.referenceSourceId = space.id;
					space.referenceVersion = Guid.NewGuid().ToString("N");
					space.referenceDirty = !SyncBus.Active;
					break;
				case SpaceReferenceAction.Size:
					if (space.HasTags || !float.IsFinite(request.size) || request.size <= 0)
						return false;
					space.tagSizeCm = request.size;
					break;
				case SpaceReferenceAction.ForgetPair:
					if (DescribeColocationPreferenceBlocker() != null ||
						(HeadsetConfiguration.SessionIsOperatorManaged && sender != SyncBus.LocalClientId))
						return false;
					space.firstTagId = -1;
					space.secondTagId = -1;
					break;
				case SpaceReferenceAction.Pair:
					if (request.trackingGeneration != SenderTrackingGeneration(sender) || request.tag < 0 ||
						request.second <= request.tag || space.firstTagId >= 0)
						return false;
					space.firstTagId = request.tag;
					space.secondTagId = request.second;
					break;
				default:
					return false;
			}
			space.MarkChanged();
			if (!documents.SpaceStore.Save(space))
				return false;

			spaceDocument.Load(space);
			references.Inject(space);
			references.ClearPendingSnapshots();
			SpaceChanged.Invoke();
			alignment.Activate(space, colocationManager.ActiveMethod, true);
			if (colocationManager.ActiveMethod == Method.TwoAprilTags && request.action is SpaceReferenceAction.Size or SpaceReferenceAction.ForgetPair)
			{
				colocationManager.TwoTagProvider.ResetReferences();
				colocationManager.InvalidateAlignment();
				alignment.Activate(space, Method.TwoAprilTags);
				scan = Guid.NewGuid();
				FrameChanged.Invoke();
			}
			DocumentsCommitted.Invoke();
			return true;
		}
		private void OnPairSelected(int first, int second)
		{
			var space = CurrentSpace;
			if (space == null)
				return;
			if (SyncBus.Active && !SyncBus.IsAuthority)
				colocationManager.TwoTagProvider.ConfigurePair(space.firstTagId, space.secondTagId);
			SubmitReference(new() { action = SpaceReferenceAction.Pair, tag = first, second = second });
		}
		public void ChooseAnotherTagPair() => SubmitReference(new() { action = SpaceReferenceAction.ForgetPair });

		// Shared-anchor assignment is scoped to the space/frame, independent of the active map.
		private void ConfigureAnchorMinter()
		{
			var provider = colocationManager.AnchorProvider;
			if (!provider)
				return;
			provider.SelectMinter = SelectAnchorMinter;
			provider.LocalReadinessGate = TrackingReady;
			provider.ValidateRemoteMint = sender => ReadyForReferenceWork(sender, true, Method.MetaSharedAnchor);
			provider.TrackingGeneration = () => colocationManager.TrackingGeneration;
			provider.ValidateTrackingGeneration = (sender, generation) => generation == SenderTrackingGeneration(sender);
			provider.PreparationGate = () => CanAuthorReferences;
			provider.PreparationMintingGate = () => CanAuthorReferences;
			provider.SharingCandidates = LocalSharingCandidates;
			provider.ValidatePreparedAnchor = (sender, _) => ReadyForReferenceWork(sender, true, Method.MetaSharedAnchor);
		}
		private void ClearAnchorMinter()
		{
			var provider = colocationManager.AnchorProvider;
			if (!provider)
				return;
			provider.SelectMinter = null;
			provider.LocalReadinessGate = null;
			provider.ValidateRemoteMint = null;
			provider.TrackingGeneration = null;
			provider.ValidateTrackingGeneration = null;
			provider.PreparationGate = null;
			provider.PreparationMintingGate = null;
			provider.SharingCandidates = null;
			provider.ValidatePreparedAnchor = null;
		}

		private ulong? SelectAnchorMinter()
		{
			var network = NetworkManager.Singleton;
			if (!network || !spaceDocument.HasSpace)
				return null;

			anchorCandidates.Clear();
			foreach (var pair in PlayerAvatar.All)
			{
				var avatar = pair.Value;
				if (avatar && avatar.IsSpawned && avatar.HeadsetStatus && network.ConnectedClientsIds.Contains(pair.Key))
					anchorCandidates.Add(new(pair.Key, avatar.HeadsetStatus.Readiness));
			}
			var assigned = colocationManager.AnchorProvider.Minter;
			return AnchorMinterPolicy.Select(assigned.assigned ? assigned.clientId : null, network.CurrentSessionOwner,
				HeadsetConfiguration.SessionIsOperatorManaged, SessionSpaceId, CanonicalFrameId, referenceContext, anchorCandidates);
		}

		private int SenderTrackingGeneration(ulong sender)
		{
			if (sender == SyncBus.LocalClientId)
				return colocationManager.TrackingGeneration;
			return PlayerAvatar.All.TryGetValue(sender, out var avatar) && avatar && avatar.HeadsetStatus
				? avatar.HeadsetStatus.Readiness.trackingGeneration : -1;
		}

		private bool ReadyForReferenceWork(ulong sender, bool creates, Method target)
		{
			if (!spaceDocument.HasSpace)
				return false;
			if (sender == SyncBus.LocalClientId)
				return TrackingReady() && (!creates || (target == Method.AprilTag ? CanAuthorTags : CanAuthorReferences));
			if (!PlayerAvatar.All.TryGetValue(sender, out var avatar) || !avatar || !avatar.IsSpawned || !avatar.HeadsetStatus)
				return false;

			var readiness = avatar.HeadsetStatus.Readiness;
			bool initializationGrant = target == Method.AprilTag
				? MapPolicy.CanInitializeTags(SyncBus.Active, HeadsetConfiguration.SessionIsOperatorManaged && Authority, alignment.Busy,
					transitionCommand.target == Method.AprilTag && transitionCommand.author == sender)
				: colocationManager.AnchorProvider.Minter.assigned && colocationManager.AnchorProvider.Minter.clientId == sender;
			return readiness.isFocused && readiness.isHeadTracked && !readiness.isOperator &&
				readiness.spaceId == SessionSpaceId && readiness.frameId == CanonicalFrameId &&
				readiness.referenceContext == referenceContext &&
				(!creates || MapPolicy.CanAuthorReferences(spaceDocument.HasReferences, readiness.activeMethod,
					readiness.referenceFrameTrusted, initializationGrant));
		}

		private IReadOnlyList<AnchorConstraintData> LocalSharingCandidates()
		{
			List<AnchorConstraintData> result = new();
			if (colocationManager.ActiveMethod == Method.AprilTag)
			{
				List<TaggedAnchorConstraintData> localAnchors = new();
				colocationManager.TagProvider.GetLocalAnchorConstraints(localAnchors);
				foreach (var anchor in localAnchors)
					result.Add(new(anchor.guid, anchor.canonPose, anchor.tagId));
			}
			else
			{
				foreach (var anchor in colocationManager.AnchorProvider.Constraints)
					result.Add(new(anchor.Key, anchor.Value.canonPose, anchor.Value.bindingId));
			}
			return result;
		}
	}
}
