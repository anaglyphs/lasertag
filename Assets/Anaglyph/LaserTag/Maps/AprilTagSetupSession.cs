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
		public bool sizeConfirmed, operatorManaged;
		public float tagSizeCm;
		public bool Matches(Guid spaceId, Guid referenceContext) => operation != Guid.Empty &&
			space == spaceId && context == referenceContext;
	}

	public sealed class AprilTagSetupSession : IDisposable
	{
		internal enum SetupAction : byte { Begin, Finish, Cancel }
		internal struct SetupRequest
		{
			public Guid space, context, operation;
			public SetupAction action;
		}

		private readonly LaserTagMapCoordinator coordinator;
		private readonly Func<ulong, bool> canAuthor;
		private readonly SyncVariable<AprilTagSetupState> state = new("space.apriltag-setup");
		private readonly SyncEvent<AprilTagSetupState> measurement = new("space.apriltag-setup.measurement", EventRoute.ToAuthority);
		private readonly SyncEvent<SetupRequest> requests = new("space.apriltag-setup.request", EventRoute.ToAuthority);
		private Guid guidingOperation;
		private float? registrationSizeCm;
		private float measurementSentAt = float.NegativeInfinity;
		public AprilTagSetupState State => state.Value;
		public bool IsActive => (!State.operatorManaged || SyncBus.Active) && State.Matches(coordinator.SessionSpaceId, coordinator.ReferenceContext);
		public bool IsGuidingHeadset => IsActive && !HeadsetConfiguration.IsOperatorDevice;
		public bool CanEndFromHere => IsActive && (!State.operatorManaged || HeadsetConfiguration.IsOperatorDevice && SyncBus.IsAuthority);
		public bool SizeConfirmed => State.sizeConfirmed && Mathf.Abs(State.tagSizeCm - coordinator.EffectiveTagSizeCm) < .001f;
		public bool CanMeasure => IsGuidingHeadset && canAuthor(SyncBus.LocalClientId) &&
			coordinator.DescribeTagSizeBlocker() == null;
		public bool CanFinish => CanEndFromHere &&
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
			requests.Received += OnRequest;
			requests.Register();
		}

		public string StartBlocker => IsActive ? null : HeadsetConfiguration.IsOperatorDevice && (!SyncBus.Active || !SyncBus.IsAuthority)
			? MenuCopy.Get("Map", "tag-setup.host-first") : coordinator.CurrentSpace == null
			? MenuCopy.Get("Map", "tag-setup.space-first") : coordinator.IsChangingMap || coordinator.HasPendingCatalogOperation
			|| MatchReferee.State is MatchState.Playing or MatchState.Countdown
			? MenuCopy.Get("Map", "tag-setup.busy") : !SyncBus.IsAuthority ? null
			: coordinator.DescribeColocationMethodBlocker(Method.AprilTag);

		public bool Begin()
		{
			if (IsActive) return true;
			if (StartBlocker != null) return false;
			if (!SyncBus.IsAuthority) { Request(SetupAction.Begin); return true; }
			if (!coordinator.SetPreferredColocationMethod(Method.AprilTag)) return false;
			state.Value = new()
			{
				operation = Guid.NewGuid(), space = coordinator.SessionSpaceId, context = coordinator.ReferenceContext,
				sizeConfirmed = coordinator.CurrentSpace.HasTags, tagSizeCm = coordinator.EffectiveTagSizeCm,
				operatorManaged = HeadsetConfiguration.IsOperatorDevice
			};
			return true;
		}

		public bool Finish()
		{
			if (!CanFinish) return false;
			if (!SyncBus.IsAuthority) { Request(SetupAction.Finish); return true; }
			if (!coordinator.SaveCurrentMap()) return false;
			state.Value = default;
			return true;
		}

		public void Cancel()
		{
			if (!CanEndFromHere) return;
			if (!SyncBus.IsAuthority) { Request(SetupAction.Cancel); return; }
			bool active = IsActive;
			state.Value = default;
			if (active && (coordinator.IsChangingColocation || coordinator.WaitingForAlignmentAuthor))
				coordinator.CancelAlignmentTransition();
		}

		private void Request(SetupAction action) => requests.Raise(new()
		{
			action = action, space = coordinator.SessionSpaceId, context = coordinator.ReferenceContext, operation = State.operation
		});

		private void OnRequest(ulong sender, SetupRequest request)
		{
			if (!SyncBus.IsAuthority || request.space != coordinator.SessionSpaceId || request.context != coordinator.ReferenceContext ||
				request.operation != State.operation || !canAuthor(sender)) return;
			if (request.action == SetupAction.Begin) Begin();
			else if (!State.operatorManaged && request.action == SetupAction.Finish) Finish();
			else if (!State.operatorManaged && request.action == SetupAction.Cancel) Cancel();
		}

		public bool ContinueToRegistration(float centimeters)
		{
			if (!IsGuidingHeadset || !float.IsFinite(centimeters) || centimeters < 1 ||
				!CanMeasure && !(SizeConfirmed && Mathf.Abs(State.tagSizeCm - centimeters) < .001f)) return false;
			registrationSizeCm = centimeters;
			ConfirmMeasurement();
			RefreshHeadsetMode();
			return true;
		}

		public void ReturnToMeasurement()
		{
			if (!IsGuidingHeadset) return;
			registrationSizeCm = null;
			MapEditorTool.SetMode(MapEditorTool.Mode.MeasureTagSize);
		}

		private void ConfirmMeasurement()
		{
			if (!CanMeasure || !registrationSizeCm.HasValue) return;
			if (SizeConfirmed && Mathf.Abs(State.tagSizeCm - registrationSizeCm.Value) < .001f) return;
			measurementSentAt = Time.unscaledTime;
			var request = State;
			request.tagSizeCm = registrationSizeCm.Value;
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
			if (SyncBus.IsAuthority && State.operation != Guid.Empty &&
				(!IsActive ||
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
			if (MapEditor.MapEditor.IsActive) RefreshHeadsetMode();
		}

		private void RefreshHeadsetMode()
		{
			bool ready = registrationSizeCm.HasValue && SizeConfirmed &&
				Mathf.Abs(registrationSizeCm.Value - State.tagSizeCm) < .001f;
			if (!ready && Time.unscaledTime - measurementSentAt > 2f) ConfirmMeasurement();
			var mode = ready ? MapEditorTool.Mode.Tags : MapEditorTool.Mode.MeasureTagSize;
			if (MapEditorTool.CurrentMode != mode) MapEditorTool.SetMode(mode);
		}

		private void EndHeadsetGuide()
		{
			if (guidingOperation == Guid.Empty) return;
			guidingOperation = Guid.Empty;
			registrationSizeCm = null;
			measurementSentAt = float.NegativeInfinity;
			MapEditor.MapEditor.SetActive(false);
		}

		public void Dispose()
		{
			EndHeadsetGuide();
			state.Changed -= OnStateChanged;
			state.Unregister();
			measurement.Received -= OnMeasurement;
			measurement.Unregister();
			requests.Received -= OnRequest;
			requests.Unregister();
		}
	}
}
