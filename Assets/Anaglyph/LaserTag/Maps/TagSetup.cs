using System;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	/// <summary>
	/// The guided tag setup in progress, if any: which space and frame it is for, who started
	/// it, which headset measures and registers for the host, and whether the printed size has
	/// been confirmed. The host writes it; every device reads it.
	/// </summary>
	public struct TagSetupState
	{
		/// <summary>Empty when no setup is running.</summary>
		public Guid operation;
		public Guid spaceId, frameId;
		/// <summary>An operator started it, so only the operator finishes or cancels it.</summary>
		public bool operatorManaged;
		public bool sizeConfirmed;
		public float tagSizeCm;
		public bool hasAuthor;
		/// <summary>The headset that measures and registers on the host's behalf.</summary>
		public ulong author;

		public static bool Same(in TagSetupState a, in TagSetupState b) =>
			a.operation == b.operation && a.spaceId == b.spaceId && a.frameId == b.frameId &&
			a.operatorManaged == b.operatorManaged && a.sizeConfirmed == b.sizeConfirmed &&
			a.tagSizeCm.Equals(b.tagSizeCm) && a.hasAuthor == b.hasAuthor && a.author == b.author;
	}

	/// <summary>
	/// The three-step AprilTag setup: print, measure the printed size, register the tags.
	/// Offline or on a headset host the device does it all itself. In an operator-managed
	/// session the operator starts and finishes it on the PC while one headset, the author
	/// the host selects, measures and registers for it. The state travels in
	/// <see cref="SpaceSync"/>; a headset's steps travel as space requests the host checks
	/// against the sender's readiness.
	/// </summary>
	public sealed class TagSetup
	{
		private const float MeasurementRetrySeconds = 2f;

		private readonly SpaceManager manager;
		private readonly SpaceAlignment alignment;
		private readonly SpaceSync sync;
		// the authority's copy; a client reads the synced one
		private TagSetupState local;
		private Guid guidingOperation;
		private float? requestedSizeCm;
		private float measurementSentAt = float.NegativeInfinity;

		public TagSetup(SpaceManager manager, SpaceAlignment alignment, SpaceSync sync)
		{
			this.manager = manager;
			this.alignment = alignment;
			this.sync = sync;
		}

		public void Register()
		{
			SyncBus.Activated += OnBusActivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		public void Unregister()
		{
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Activated -= OnBusActivated;
			EndHeadsetGuide();
		}

		private void OnBusActivated() { if (SyncBus.IsAuthority) sync.PublishSetup(local); }
		private void OnAuthorityChanged(bool authority) { if (authority) local = sync.Setup; }

		private static string L(string key) => MenuCopy.Get("Map", key);
		private static bool RoundInProgress => MatchReferee.State is MatchState.Playing or MatchState.Countdown;

		// ------- state -----------------------------------------------

		public TagSetupState State => SyncBus.Active && !SyncBus.IsAuthority ? sync.Setup : local;

		/// <summary>A setup is running for the shown space and frame.</summary>
		public bool IsActive => State.operation != Guid.Empty && manager.Space != null &&
			State.spaceId == manager.Space.Id && State.frameId == manager.Space.FrameId &&
			(!State.operatorManaged || SyncBus.Active);

		/// <summary>This headset measures and registers for the setup.</summary>
		public bool IsGuidingHeadset => IsActive && !HeadsetConfiguration.IsOperatorDevice &&
			State.hasAuthor && State.author == SyncBus.LocalClientId;

		/// <summary>Whoever started it finishes or cancels it: the operator, or any headset of a headset-run setup.</summary>
		public bool CanEndFromHere => IsActive &&
			(!State.operatorManaged || HeadsetConfiguration.IsOperatorDevice && SyncBus.IsAuthority);

		/// <summary>The host accepted a measurement of the size the space now uses.</summary>
		public bool SizeConfirmed => State.sizeConfirmed && Mathf.Abs(State.tagSizeCm - alignment.TagSizeCm) < .001f;

		/// <summary>The size can still change: no tag has been registered at it yet.</summary>
		public bool CanMeasure => IsGuidingHeadset && alignment.TagSizeBlocker() == null;

		public bool CanFinish => CanEndFromHere && manager.Space.HasTags &&
			manager.Space.alignment.method == Method.AprilTag && !manager.IsChangingMap;

		public string StartBlocker =>
			IsActive ? null :
			HeadsetConfiguration.IsOperatorDevice && (!SyncBus.Active || !SyncBus.IsAuthority) ? L("tag-setup.host-first") :
			manager.Space == null ? L("tag-setup.space-first") :
			manager.IsChangingMap || RoundInProgress ? L("tag-setup.busy") :
			manager.Role == SpaceRole.Client ? null :
			alignment.MethodBlocker(Method.AprilTag);

		// ------- commands --------------------------------------------

		/// <summary>Starts a setup for the shown space, switching it to AprilTags. A client asks the host.</summary>
		public bool Begin()
		{
			if (IsActive) return true;
			if (StartBlocker != null) return false;
			if (manager.Role == SpaceRole.Client)
			{
				Send(SpaceRequestKind.TagSetupBegin);
				return true;
			}
			return ApplyBegin(HeadsetConfiguration.IsOperatorDevice ? null : SyncBus.LocalClientId);
		}

		// the requester authors when it can; an operator takes any ready headset
		private bool ApplyBegin(ulong? requester)
		{
			Space space = manager.Space;
			if (space.alignment.method != Method.AprilTag && !alignment.SetMethod(Method.AprilTag)) return false;
			// after the switch: entering two-tag mode gives the space a new frame
			ulong? author = requester.HasValue && alignment.ReadyFor(requester.Value) ? requester : alignment.SelectAuthor(null);
			Set(new TagSetupState
			{
				operation = Guid.NewGuid(), spaceId = space.Id, frameId = space.FrameId,
				operatorManaged = HeadsetConfiguration.IsOperatorDevice,
				sizeConfirmed = space.HasTags, tagSizeCm = space.alignment.tagSizeCm,
				hasAuthor = author.HasValue, author = author ?? 0
			});
			return true;
		}

		/// <summary>The guiding headset settles on the printed size and moves on to registering once the host confirms it.</summary>
		public bool ContinueToRegistration(float centimeters)
		{
			if (!IsGuidingHeadset || !float.IsFinite(centimeters) || centimeters < 1) return false;
			if (!CanMeasure && !(SizeConfirmed && Mathf.Abs(State.tagSizeCm - centimeters) < .001f)) return false;
			requestedSizeCm = centimeters;
			ConfirmMeasurement();
			RefreshHeadsetMode();
			return true;
		}

		public void ReturnToMeasurement()
		{
			if (!IsGuidingHeadset) return;
			requestedSizeCm = null;
			MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
		}

		private void ConfirmMeasurement()
		{
			if (!CanMeasure || !requestedSizeCm.HasValue) return;
			if (SizeConfirmed && Mathf.Abs(State.tagSizeCm - requestedSizeCm.Value) < .001f) return;
			measurementSentAt = Time.unscaledTime;
			if (manager.Role == SpaceRole.Client) Send(SpaceRequestKind.TagSetupMeasure, requestedSizeCm.Value);
			else ApplyMeasure(requestedSizeCm.Value);
		}

		// the measured size becomes the space's while no tag is registered at another
		private bool ApplyMeasure(float centimeters)
		{
			if (!IsActive || !float.IsFinite(centimeters) || centimeters < 1) return false;
			Space space = manager.Space;
			if (!space.HasTags && !alignment.SetTagSize(centimeters)) return false;
			if (Mathf.Abs(space.alignment.tagSizeCm - centimeters) >= .001f) return false;
			TagSetupState confirmed = State;
			confirmed.sizeConfirmed = true;
			confirmed.tagSizeCm = centimeters;
			Set(confirmed);
			return true;
		}

		/// <summary>Saves the registered tags and ends the setup.</summary>
		public bool Finish()
		{
			if (!CanFinish) return false;
			if (manager.Role == SpaceRole.Client)
			{
				Send(SpaceRequestKind.TagSetupFinish);
				return true;
			}
			return ApplyFinish();
		}

		private bool ApplyFinish()
		{
			if (!CanFinish || !manager.Save()) return false;
			Set(default);
			return true;
		}

		/// <summary>Ends the setup; the tags registered so far stay with the space.</summary>
		public void Cancel()
		{
			if (!CanEndFromHere) return;
			if (manager.Role == SpaceRole.Client)
			{
				Send(SpaceRequestKind.TagSetupCancel);
				return;
			}
			Set(default);
		}

		private void Send(SpaceRequestKind kind, float size = 0) => alignment.Send(new SpaceRequest { kind = kind, size = size });

		private void Set(TagSetupState value)
		{
			local = value;
			sync.PublishSetup(value);
		}

		/// <summary>
		/// The host's side of a headset's setup request. The space and frame were already
		/// checked. A ready headset may start one; only the author measures; a headset-run
		/// setup may be finished or cancelled by any ready headset, an operator's by none.
		/// </summary>
		internal bool HandleRequest(ulong sender, SpaceRequest value)
		{
			switch (value.kind)
			{
				case SpaceRequestKind.TagSetupBegin:
					if (IsActive) return true;
					return !RoundInProgress && !manager.IsChangingMap && alignment.ReadyFor(sender) &&
						alignment.MethodBlocker(Method.AprilTag) == null && ApplyBegin(sender);
				case SpaceRequestKind.TagSetupMeasure:
					return IsActive && State.hasAuthor && sender == State.author && ApplyMeasure(value.size);
				case SpaceRequestKind.TagSetupFinish:
					return IsActive && !State.operatorManaged && alignment.ReadyFor(sender) && ApplyFinish();
				case SpaceRequestKind.TagSetupCancel:
					if (!IsActive || State.operatorManaged || !alignment.ReadyFor(sender)) return false;
					Set(default);
					return true;
				default:
					return false;
			}
		}

		// ------- per frame -------------------------------------------

		public void Tick()
		{
			if (manager.Role != SpaceRole.Client) TickAuthority();
			TickHeadset();
		}

		// the setup ends with its space, its method or a round starting; it follows the space's frame
		// through a rebase of its own (the last tag removed to measure again) and keeps a ready author
		private void TickAuthority()
		{
			TagSetupState state = local;
			if (state.operation == Guid.Empty) return;
			Space space = manager.Space;
			if (space == null || state.spaceId != space.Id || RoundInProgress ||
				space.alignment.method != Method.AprilTag || state.operatorManaged && !SyncBus.Active)
			{
				Set(default);
				return;
			}
			state.frameId = space.FrameId;
			ulong? author = alignment.SelectAuthor(state.hasAuthor ? state.author : null);
			state.hasAuthor = author.HasValue;
			state.author = author ?? 0;
			if (!TagSetupState.Same(state, local)) Set(state);
		}

		private void TickHeadset()
		{
			if (!IsGuidingHeadset || MainXRRig.Instance == null)
			{
				EndHeadsetGuide();
				return;
			}
			if (guidingOperation != State.operation)
			{
				guidingOperation = State.operation;
				requestedSizeCm = null;
				MapEditor.MapEditor.RequestTagRegistration();
			}
			if (MapEditor.MapEditor.IsActive) RefreshHeadsetMode();
		}

		// measuring until the host has the size the headset settled on, then registering
		private void RefreshHeadsetMode()
		{
			bool ready = requestedSizeCm.HasValue && SizeConfirmed && Mathf.Abs(requestedSizeCm.Value - State.tagSizeCm) < .001f;
			if (!ready && Time.unscaledTime - measurementSentAt > MeasurementRetrySeconds) ConfirmMeasurement();
			MapEditorTool.Mode mode = ready ? MapEditorTool.Mode.Tags : MapEditorTool.Mode.MeasureTagSize;
			if (MapEditorTool.CurrentMode != mode) MapEditorTool.SetMode(mode);
		}

		private void EndHeadsetGuide()
		{
			if (guidingOperation == Guid.Empty) return;
			guidingOperation = Guid.Empty;
			requestedSizeCm = null;
			measurementSentAt = float.NegativeInfinity;
			MapEditor.MapEditor.SetActive(false);
		}
	}
}
