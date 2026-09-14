using System;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Netcode.SyncVariables;
using Anaglyph.XR;
using UnityEngine;
using Method = Anaglyph.LaserTag.ColocationManager.ColocationMethod;

namespace Anaglyph.LaserTag.Maps
{
	public struct AprilTagSetupState
	{
		public Guid operation, space, context;
		public bool sizeConfirmed;
		public float tagSizeCm;
		public bool Matches(Guid spaceId, Guid referenceContext) => operation != Guid.Empty &&
			space == spaceId && context == referenceContext;
	}

	public sealed class AprilTagSetupSession : IDisposable
	{
		private readonly LaserTagMapCoordinator coordinator;
		private readonly Func<ulong, bool> canAuthor;
		private readonly SyncVariable<AprilTagSetupState> state = new("space.apriltag-setup");
		private readonly SyncEvent<AprilTagSetupState> measurement = new("space.apriltag-setup.measurement", EventRoute.ToAuthority);
		private Guid guidingOperation;
		private float measurementSentAt = float.NegativeInfinity;
		public AprilTagSetupState State => state.Value;
		public bool IsActive => SyncBus.Active && State.Matches(coordinator.SessionSpaceId, coordinator.ReferenceContext);
		public bool IsGuidingHeadset => IsActive && !HeadsetConfiguration.IsOperatorDevice;
		public bool SizeConfirmed => State.sizeConfirmed && Mathf.Abs(State.tagSizeCm - coordinator.EffectiveTagSizeCm) < .001f;
		public bool CanMeasure => IsGuidingHeadset && !SizeConfirmed && canAuthor(SyncBus.LocalClientId) &&
			coordinator.DescribeTagSizeBlocker() == null;
		public bool CanFinish => IsActive && HeadsetConfiguration.IsOperatorDevice && SyncBus.IsAuthority &&
			coordinator.CurrentSpace is { HasTags: true, hasPendingSetup: false, preferredColocationMethod: Method.AprilTag } &&
			!coordinator.IsChangingColocation && !coordinator.IsChangingMap && !coordinator.HasPendingCatalogOperation;

		internal AprilTagSetupSession(LaserTagMapCoordinator coordinator, Func<ulong, bool> canAuthor)
		{
			this.coordinator = coordinator;
			this.canAuthor = canAuthor;
		}

		public void Register()
		{
			state.Validate = (_, _) => false;
			state.Changed += OnStateChanged;
			state.Register();
			measurement.Received += OnMeasurement;
			measurement.Register();
			MapEditorTool.TagSizeMeasured += ConfirmMeasurement;
		}

		public string StartBlocker => !HeadsetConfiguration.IsOperatorDevice || !SyncBus.Active || !SyncBus.IsAuthority
			? MenuCopy.Get("Game", "tag-setup.host-first") : coordinator.CurrentSpace == null
			? MenuCopy.Get("Game", "tag-setup.space-first") : coordinator.IsChangingMap || coordinator.HasPendingCatalogOperation
			? MenuCopy.Get("Game", "tag-setup.busy") : coordinator.DescribeColocationMethodBlocker(Method.AprilTag);

		public bool Begin()
		{
			if (IsActive) return true;
			if (StartBlocker != null || !coordinator.SetPreferredColocationMethod(Method.AprilTag)) return false;
			state.Value = new()
			{
				operation = Guid.NewGuid(), space = coordinator.SessionSpaceId, context = coordinator.ReferenceContext,
				sizeConfirmed = coordinator.CurrentSpace.HasTags, tagSizeCm = coordinator.EffectiveTagSizeCm
			};
			return true;
		}

		public bool Finish()
		{
			if (!CanFinish || !coordinator.SaveCurrentMap()) return false;
			state.Value = default;
			return true;
		}

		public void Cancel()
		{
			if (!SyncBus.IsAuthority || !HeadsetConfiguration.IsOperatorDevice) return;
			bool active = IsActive;
			state.Value = default;
			if (active && (coordinator.IsChangingColocation || coordinator.WaitingForAlignmentAuthor))
				coordinator.CancelAlignmentTransition();
		}

		private void ConfirmMeasurement(float centimeters)
		{
			if (!CanMeasure) return;
			measurementSentAt = Time.unscaledTime;
			var request = State;
			request.tagSizeCm = centimeters;
			measurement.Raise(request);
		}

		private void OnMeasurement(ulong sender, AprilTagSetupState request)
		{
			if (!SyncBus.IsAuthority || !IsActive || !request.Matches(State.space, State.context) ||
				request.operation != State.operation || !canAuthor(sender) || !float.IsFinite(request.tagSizeCm) ||
				request.tagSizeCm < 1 || Mathf.Abs(request.tagSizeCm - coordinator.EffectiveTagSizeCm) >= .001f)
				return;
			var confirmed = State;
			confirmed.sizeConfirmed = true;
			confirmed.tagSizeCm = request.tagSizeCm;
			state.Value = confirmed;
		}

		private void OnStateChanged(AprilTagSetupState previous, AprilTagSetupState current)
		{
			if (current.operation != previous.operation) EndHeadsetGuide();
		}

		public void Tick()
		{
			if (SyncBus.Active && SyncBus.IsAuthority && State.operation != Guid.Empty &&
				(!IsActive || !HeadsetConfiguration.IsOperatorDevice ||
				 MatchReferee.State is MatchState.Playing or MatchState.Countdown ||
				 coordinator.CurrentSpace is { hasPendingSetup: true, pendingSetupMethod: not Method.AprilTag } ||
				 coordinator.CurrentSpace is { hasPendingSetup: false, preferredColocationMethod: not Method.AprilTag }))
			{
				state.Value = default;
			}
			if (!IsGuidingHeadset || MainXRRig.Instance == null)
			{
				EndHeadsetGuide();
				return;
			}
			if (guidingOperation != State.operation)
			{
				guidingOperation = State.operation;
				MapEditor.MapEditor.RequestTagRegistration();
			}
			if (!MapEditor.MapEditor.IsActive) MapEditor.MapEditor.SetActive(true);
			var mode = MapEditorTool.CurrentMode;
			if (CanMeasure && Time.unscaledTime - measurementSentAt > 2f)
			{
				if (mode != MapEditorTool.Mode.MeasureTagSize) MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
			}
			else if (mode is not MapEditorTool.Mode.Tags && (SizeConfirmed || mode != MapEditorTool.Mode.MeasureTagSize))
				MapEditorTool.SetMode(MapEditorTool.Mode.Tags);
		}

		private void EndHeadsetGuide()
		{
			if (guidingOperation == Guid.Empty) return;
			guidingOperation = Guid.Empty;
			measurementSentAt = float.NegativeInfinity;
			MapEditor.MapEditor.SetActive(false);
		}

		public void Dispose()
		{
			EndHeadsetGuide();
			MapEditorTool.TagSizeMeasured -= ConfirmMeasurement;
			state.Changed -= OnStateChanged;
			state.Unregister();
			measurement.Received -= OnMeasurement;
			measurement.Unregister();
		}
	}
}
