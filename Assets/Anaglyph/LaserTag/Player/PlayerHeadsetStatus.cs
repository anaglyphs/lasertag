using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.XR;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>Device capabilities and current readiness, independent of playing or alignment.</summary>
	public struct HeadsetReadiness : INetworkSerializeByMemcpy
	{
		public bool isOperator;
		public bool hasAnchorRuntime;
		public bool supportsSharedAnchors;
		public bool isHeadTracked;
		public bool isFocused;
		public bool aligned;
		public bool referenceFrameTrusted;
		public Guid mapId;
		public Guid spaceId, frameId, referenceContext;
		public int trackingGeneration;
		public ColocationManager.ColocationMethod activeMethod;
		public ReferenceTransitionPhase transition;
		public ColocationManager.ColocationMethod method;

		// Alignment is deliberately separate: a blank map needs its first anchor before aligning.
		public readonly bool CanMintSharedAnchors => !isOperator && hasAnchorRuntime &&
			supportsSharedAnchors && isHeadTracked && isFocused;

		public readonly bool IsAlignedTo(Guid currentMap, Guid currentFrame, Guid context) =>
			!isOperator && isFocused && isHeadTracked && aligned && currentMap != Guid.Empty && currentFrame != Guid.Empty &&
			mapId == currentMap && frameId == currentFrame && referenceContext == context;

	}

	/// <summary>
	/// Owner-sampled headset attributes on the player's NetworkObject. Readiness is updated
	/// on change each frame; slower operator diagnostics are sampled once per second.
	/// This component stays active while the avatar's spatial presentation is hidden.
	/// </summary>
	[DefaultExecutionOrder(-600)]
	public class PlayerHeadsetStatus : NetworkBehaviour
	{
		private readonly NetworkVariable<HeadsetReadiness> readinessSync = new();
		private readonly NetworkVariable<HeadsetTelemetry> telemetrySync = new(HeadsetTelemetry.Unknown);
		private Guid currentMapId;
		private bool focused;
		private bool paused;
		private float nextTelemetrySampleTime;

		public HeadsetReadiness Readiness => IsOwner ? SampleReadiness() : readinessSync.Value;
		public HeadsetTelemetry Telemetry => telemetrySync.Value;

		public bool IsAligned => IsSpawned && SessionFrameReady && Readiness.IsAlignedTo(currentMapId,
			LaserTagMapCoordinator.Instance.CanonicalFrameId, LaserTagMapCoordinator.Instance.ReferenceContext);

		private static bool SessionFrameReady => LaserTagMapCoordinator.Instance != null &&
			!LaserTagMapCoordinator.Instance.IsChangingMap &&
			LaserTagMapCoordinator.Instance.Phase is MapPhase.Hosting or MapPhase.FollowingSession;

		public override void OnNetworkSpawn()
		{
			focused = Application.isFocused;
			OnMapChanged(LaserTagMapCoordinator.Instance?.CurrentMap);
			LaserTagMapCoordinator.CurrentMapChanged += OnMapChanged;
			Publish();
		}

		public override void OnNetworkDespawn()
		{
			LaserTagMapCoordinator.CurrentMapChanged -= OnMapChanged;
			currentMapId = Guid.Empty;
		}

		private void OnMapChanged(GameMap map) => Guid.TryParse(map?.id, out currentMapId);
		private void Update() => Publish();
		private void OnApplicationFocus(bool value) { focused = value; Publish(); }
		private void OnApplicationPause(bool value) { paused = value; Publish(); }

		// Use the same tracking signal for local authoring and the operator's remote selection.
		public static bool HeadTrackingReady
		{
			get
			{
				if (HeadsetConfiguration.IsOperatorDevice || !MainXRRig.Instance) return false;
#if UNITY_EDITOR
				if (!XRSettings.enabled) return true;
#endif
				var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
				return head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
			}
		}

		private HeadsetReadiness SampleReadiness()
		{
			AnchorRegistry registry = AnchorRegistry.Instance;
			return new HeadsetReadiness
			{
				isOperator = HeadsetConfiguration.IsOperatorDevice,
				hasAnchorRuntime = registry != null && registry.IsAvailable,
				supportsSharedAnchors = registry != null && registry.canShareAnchors,
				isHeadTracked = HeadTrackingReady,
				isFocused = focused && !paused,
				aligned = SessionFrameReady && LaserTagMapCoordinator.Instance.CheckWorldFrameIsTrusted(),
				referenceFrameTrusted = LaserTagMapCoordinator.Instance != null &&
					LaserTagMapCoordinator.Instance.CheckReferenceFrameAgreement(),
				trackingGeneration = ColocationManager.Instance != null ? ColocationManager.Instance.TrackingGeneration : 0,
				mapId = currentMapId,
				spaceId = LaserTagMapCoordinator.Instance != null ? LaserTagMapCoordinator.Instance.SessionSpaceId : Guid.Empty,
				frameId = LaserTagMapCoordinator.Instance != null ? LaserTagMapCoordinator.Instance.CanonicalFrameId : Guid.Empty,
				referenceContext = LaserTagMapCoordinator.Instance != null ? LaserTagMapCoordinator.Instance.ReferenceContext : Guid.Empty,
				activeMethod = ColocationManager.Instance != null ? ColocationManager.Instance.ActiveMethod : default,
				transition = LaserTagMapCoordinator.Instance?.AlignmentTransition?.Phase ?? ReferenceTransitionPhase.None,
				method = ColocationManager.Instance != null ? ColocationManager.Instance.SelectedMethod : default
			};
		}

		private void Publish()
		{
			if (!IsSpawned || !IsOwner) return;
			readinessSync.Value = SampleReadiness();
			if (Time.unscaledTime < nextTelemetrySampleTime) return;
			nextTelemetrySampleTime = Time.unscaledTime + 1f;
			telemetrySync.Value = HeadsetTelemetry.Sample();
		}
	}
}
