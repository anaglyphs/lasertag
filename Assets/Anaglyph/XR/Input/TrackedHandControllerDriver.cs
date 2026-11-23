using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using XRHandedness = UnityEngine.XR.Hands.Handedness;
using InputCommonUsages = UnityEngine.InputSystem.CommonUsages;

namespace Anaglyph.XR.Input
{
	/// <summary>
	/// Feeds XR Hands joints into one <see cref="TrackedHandController"/> per hand.
	/// The pointer (UI ray) runs from an estimated shoulder through the knuckles, so finger and
	/// wrist motion barely moves it. The grip pose is glued to the palm (barrel from the wrist
	/// through the knuckles, topped by the index side, from rigid bones only), then swung by the
	/// smallest rotation that puts the held sight on a line from the dominant eye, looking along
	/// the barrel. The thumb touching anywhere on the index finger is the trigger. The same tap with the left palm facing the head is the menu.
	/// </summary>
	public class TrackedHandControllerDriver : MonoBehaviour
	{
		[Header("Body estimate (meters, head space)")]
		[SerializeField] private Vector3 neckFromEyes = new(0, -0.1f, -0.08f);
		[SerializeField] private float shoulderBelowNeck = 0.13f;
		[SerializeField] private float shoulderHalfWidth = 0.17f;
		[Tooltip("Head yaw beyond this turns the estimated torso.")]
		[SerializeField] private float bodyYawDeadzoneDegrees = 30f;

		[Header("Grip pose")]
		[Tooltip("Angle from the held barrel up to the grip's fist tube (pinky to thumb), as a controller's grip sits relative to where it points.")]
		[SerializeField] private float fistTubeToBarrelDegrees = 80f;
		[Tooltip("Handle center in front of the palm.")]
		[SerializeField] private float handleFromPalm = 0.02f;
		[Tooltip("Where the held sight sits relative to the handle, in the barrel's frame (x right, y up, z forward). The eye looks through it along the barrel.")]
		[SerializeField] private Vector3 sightFromHandle = new(0, 0.104f, 0.079f);
		[Tooltip("0: the held barrel follows the palm alone. 1: it follows the sight line, keeping the palm-relative pose it has when the palm lines up with that line.")]
		[SerializeField, Range(0, 1)] private float barrelFollowsSightLine = 1f;
		[Tooltip("Closer than this to the eye, the sight line is too unstable and the palm aims alone.")]
		[SerializeField] private float minimumSightDistance = 0.12f;

		[Header("Tap (thumb tip to nearest point on the index finger, meters)")]
		[SerializeField] private float tapPressGap = 0.022f;
		[SerializeField] private float tapReleaseGap = 0.035f;
		[SerializeField] private float tapTriggerZeroGap = 0.06f;
		[Tooltip("Palm-toward-head cosine at which a tap becomes a menu press instead of a shot.")]
		[SerializeField] private float palmFacingHeadCosine = 0.6f;

		[Header("Walking compensation")]
		[Tooltip("How far hand tracking trails head tracking. Hands are carried along by the head's travel over this time, so walking doesn't leave held items behind. Head rotation isn't carried, so looking around doesn't swing them.")]
		[SerializeField, Range(0, 0.15f)] private float handTrackingLagSeconds = 0.05f;
		[Tooltip("Head travel beyond this over the lag window is a recenter or teleport, not a walk.")]
		[SerializeField] private float maxHeadTravel = 0.3f;

		[Header("Pointer and grip smoothing")]
		[SerializeField] private bool smoothDirections;
		[SerializeField] private float aimMinCutoffHz = 1.2f;
		[SerializeField] private float aimCutoffPerDegreePerSecond = 0.05f;
		[Tooltip("How much pointer and grip freeze while the fingers are closing or opening.")]
		[SerializeField, Range(0, 1)] private float aimFreezeDuringTap = 0.85f;
		[SerializeField] private float tapFingerSpeedForFullFreeze = 0.2f;

		private readonly List<XRHandSubsystem> subsystemsBuffer = new();
		private XRHandSubsystem subsystem;
		private TrackedHand left, right;
		private float bodyYaw;
		private bool hasBodyYaw;

		private readonly double[] headSampleTimes = new double[64];
		private readonly Vector3[] headSamplePositions = new Vector3[64];
		private int headSampleCount;

		private void OnEnable()
		{
			left = new TrackedHand(this, XRHandedness.Left);
			right = new TrackedHand(this, XRHandedness.Right);
		}

		private void OnDisable()
		{
			if (subsystem != null)
				subsystem.updatedHands -= OnUpdatedHands;
			subsystem = null;

			left.RemoveDevice();
			right.RemoveDevice();
		}

		private void Update()
		{
			if (subsystem != null && subsystem.running)
				return;

			if (subsystem != null)
				subsystem.updatedHands -= OnUpdatedHands;
			subsystem = null;

			SubsystemManager.GetSubsystems(subsystemsBuffer);
			foreach (XRHandSubsystem candidate in subsystemsBuffer)
			{
				if (!candidate.running) continue;
				subsystem = candidate;
				subsystem.updatedHands += OnUpdatedHands;
				break;
			}
		}

		private void OnUpdatedHands(XRHandSubsystem hands, XRHandSubsystem.UpdateSuccessFlags _,
			XRHandSubsystem.UpdateType updateType)
		{
			if (updateType != XRHandSubsystem.UpdateType.Dynamic)
				return;

			XRHMD hmd = InputSystem.GetDevice<XRHMD>();
			if (hmd == null || !hmd.isTracked.isPressed)
			{
				left.MarkUntracked();
				right.MarkUntracked();
				return;
			}

			Pose head = new(hmd.centerEyePosition.ReadValue(), hmd.centerEyeRotation.ReadValue());
			EstimateShoulders(head, out Vector3 leftShoulder, out Vector3 rightShoulder);

			Vector3 headTravel = RecordHeadAndGetTravelDuringHandLag(head.position);

			float deltaTime = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
			Vector3 dominantEye = Dominance.Eye == Handedness.Left
				? EyePosition(hmd.leftEyePosition.ReadValue(), head, -1)
				: EyePosition(hmd.rightEyePosition.ReadValue(), head, 1);
			left.Update(hands.leftHand, leftShoulder, head, dominantEye, headTravel, deltaTime);
			right.Update(hands.rightHand, rightShoulder, head, dominantEye, headTravel, deltaTime);
		}

		// runtimes that don't report eyes get a typical half IPD either side of the center eye
		private static Vector3 EyePosition(Vector3 reportedEye, Pose head, float side) =>
			reportedEye != Vector3.zero ? reportedEye : head.position + head.rotation * new Vector3(side * 0.032f, 0, 0);

		private Vector3 RecordHeadAndGetTravelDuringHandLag(Vector3 headPosition)
		{
			double now = Time.realtimeSinceStartupAsDouble;
			int length = headSampleTimes.Length;
			headSampleTimes[headSampleCount % length] = now;
			headSamplePositions[headSampleCount % length] = headPosition;
			headSampleCount++;

			double lagTime = now - handTrackingLagSeconds;
			int oldest = Mathf.Max(0, headSampleCount - length);
			for (int i = headSampleCount - 2; i >= oldest; i--)
			{
				double sampleTime = headSampleTimes[i % length];
				if (sampleTime > lagTime)
					continue;

				double newerTime = headSampleTimes[(i + 1) % length];
				float between = newerTime > sampleTime ? (float)((lagTime - sampleTime) / (newerTime - sampleTime)) : 0f;
				Vector3 headThen = Vector3.Lerp(headSamplePositions[i % length],
					headSamplePositions[(i + 1) % length], between);

				Vector3 travel = headPosition - headThen;
				return travel.magnitude < maxHeadTravel ? travel : Vector3.zero;
			}

			return Vector3.zero;
		}

		private void EstimateShoulders(Pose head, out Vector3 leftShoulder, out Vector3 rightShoulder)
		{
			Vector3 headForwardFlat = head.rotation * Vector3.forward;
			headForwardFlat.y = 0;
			if (headForwardFlat.sqrMagnitude > 0.01f)
			{
				float headYaw = Mathf.Atan2(headForwardFlat.x, headForwardFlat.z) * Mathf.Rad2Deg;
				if (!hasBodyYaw)
				{
					bodyYaw = headYaw;
					hasBodyYaw = true;
				}

				float yawOutsideDeadzone = Mathf.DeltaAngle(bodyYaw, headYaw);
				if (Mathf.Abs(yawOutsideDeadzone) > bodyYawDeadzoneDegrees)
					bodyYaw += yawOutsideDeadzone - Mathf.Sign(yawOutsideDeadzone) * bodyYawDeadzoneDegrees;
			}

			Vector3 neck = head.position + head.rotation * neckFromEyes;
			Vector3 shoulderCenter = neck + Vector3.down * shoulderBelowNeck;
			Vector3 bodyRight = Quaternion.Euler(0, bodyYaw, 0) * Vector3.right;
			leftShoulder = shoulderCenter - bodyRight * shoulderHalfWidth;
			rightShoulder = shoulderCenter + bodyRight * shoulderHalfWidth;
		}

		private static float SmoothingFactor(float cutoffHz, float deltaTime) =>
			1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz * deltaTime);

		// one-euro style: steady when still, quick when moving, held while the fingers tap
		private class SmoothedDirection
		{
			private readonly TrackedHandControllerDriver settings;
			private Vector3 direction;
			private Vector3 lastRawDirection;
			private float smoothedSpeed;
			private bool hasDirection;

			public SmoothedDirection(TrackedHandControllerDriver settings) => this.settings = settings;

			public void Reset() => hasDirection = false;

			public Vector3 Update(Vector3 rawDirection, float tapFingerSpeed, float deltaTime)
			{
				if (!hasDirection || !settings.smoothDirections)
				{
					direction = lastRawDirection = rawDirection;
					smoothedSpeed = 0;
					hasDirection = true;
					return direction;
				}

				float rawSpeed = Vector3.Angle(lastRawDirection, rawDirection) / deltaTime;
				lastRawDirection = rawDirection;
				smoothedSpeed = Mathf.Lerp(smoothedSpeed, rawSpeed, SmoothingFactor(1f, deltaTime));

				float cutoff = settings.aimMinCutoffHz + settings.aimCutoffPerDegreePerSecond * smoothedSpeed;
				float freeze = settings.aimFreezeDuringTap *
				               Mathf.Clamp01(tapFingerSpeed / settings.tapFingerSpeedForFullFreeze);
				float follow = SmoothingFactor(cutoff, deltaTime) * (1f - freeze);

				direction = Vector3.Slerp(direction, rawDirection, follow).normalized;
				return direction;
			}
		}

		private class TrackedHand
		{
			private readonly TrackedHandControllerDriver settings;
			private readonly XRHandedness side;
			private readonly InternedString handUsage;

			private TrackedHandController device;
			private TrackedHandControllerState state;
			private bool reportedTracked;

			private bool tapHeld;
			private bool tapIsMenu;
			private float lastTapGap;
			private bool hasLastTapGap;

			private readonly SmoothedDirection pointerDirection;
			private readonly SmoothedDirection barrelDirection;
			private readonly SmoothedDirection barrelTop;
			private readonly SmoothedDirection sightLine;

			public TrackedHand(TrackedHandControllerDriver settings, XRHandedness side)
			{
				this.settings = settings;
				this.side = side;
				pointerDirection = new SmoothedDirection(settings);
				barrelDirection = new SmoothedDirection(settings);
				barrelTop = new SmoothedDirection(settings);
				sightLine = new SmoothedDirection(settings);
				handUsage = side == XRHandedness.Left ? InputCommonUsages.LeftHand : InputCommonUsages.RightHand;
				state.deviceRotation = Quaternion.identity;
				state.pointerRotation = Quaternion.identity;
			}

			public void Update(XRHand hand, Vector3 shoulder, Pose head, Vector3 sightingEye, Vector3 headTravelDuringHandLag,
				float deltaTime)
			{
				if (!hand.isTracked ||
				    !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist) ||
				    !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose palm) ||
				    !hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexKnuckle) ||
				    !hand.GetJoint(XRHandJointID.IndexIntermediate).TryGetPose(out Pose indexMiddle) ||
				    !hand.GetJoint(XRHandJointID.IndexDistal).TryGetPose(out Pose indexDistal) ||
				    !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose indexTip) ||
				    !hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out Pose littleKnuckle) ||
				    !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbTip))
				{
					MarkUntracked();
					return;
				}

				EnsureDevice();

				float tapGap = DistanceToIndexFinger(thumbTip.position,
					indexKnuckle.position, indexMiddle.position, indexDistal.position, indexTip.position);
				float tapFingerSpeed = hasLastTapGap ? Mathf.Abs(tapGap - lastTapGap) / deltaTime : 0f;
				lastTapGap = tapGap;
				hasLastTapGap = true;

				bool wasTapHeld = tapHeld;
				tapHeld = tapHeld ? tapGap < settings.tapReleaseGap : tapGap < settings.tapPressGap;
				if (tapHeld && !wasTapHeld)
					tapIsMenu = IsPalmFacingHead(palm, head);

				Vector3 pointerOrigin = (palm.position + indexKnuckle.position) * 0.5f + headTravelDuringHandLag;
				Vector3 pointer = pointerDirection.Update(
					(pointerOrigin - shoulder).normalized, tapFingerSpeed, deltaTime);

				Vector3 knuckles = (indexKnuckle.position + littleKnuckle.position) * 0.5f;
				Vector3 barrel = barrelDirection.Update(
					(knuckles - wrist.position).normalized, tapFingerSpeed, deltaTime);
				Vector3 top = barrelTop.Update(
					(indexKnuckle.position - littleKnuckle.position).normalized, tapFingerSpeed, deltaTime);
				Quaternion palmBarrelRotation = Quaternion.LookRotation(barrel, top);

				Vector3 handle = palm.position + PalmNormal(palm) * settings.handleFromPalm + headTravelDuringHandLag;
				Quaternion barrelRotation = AlignSightWithEye(palmBarrelRotation, barrel, handle, sightingEye,
					tapFingerSpeed, deltaTime);

				state.isTracked = true;
				state.trackingState = (int)(InputTrackingState.Position | InputTrackingState.Rotation);
				state.triggerPressed = tapHeld && !tapIsMenu;
				state.trigger = tapHeld && tapIsMenu
					? 0f
					: Mathf.InverseLerp(settings.tapTriggerZeroGap, settings.tapPressGap, tapGap);
				state.menu = tapHeld && tapIsMenu && side == XRHandedness.Left;
				state.devicePosition = handle;
				state.deviceRotation = barrelRotation * Quaternion.Euler(-settings.fistTubeToBarrelDegrees, 0, 0);
				state.pointerPosition = pointerOrigin;
				state.pointerRotation = Quaternion.LookRotation(pointer, Vector3.up);

				reportedTracked = true;
				Publish();
			}

			// The sight moves with the gun, so where it sits depends on the rotation being solved.
			// Its offset is small next to the eye distance, so a few passes settle it.
			private Quaternion AlignSightWithEye(Quaternion palmBarrelRotation, Vector3 palmBarrel,
				Vector3 handle, Vector3 eye, float tapFingerSpeed, float deltaTime)
			{
				Vector3 sightDirection = palmBarrel;
				Quaternion barrelRotation = palmBarrelRotation;

				for (int pass = 0; pass < 3; pass++)
				{
					Vector3 eyeToSight = handle + barrelRotation * settings.sightFromHandle - eye;
					if (eyeToSight.magnitude < settings.minimumSightDistance)
						return palmBarrelRotation;

					sightDirection = eyeToSight.normalized;
					barrelRotation = Quaternion.FromToRotation(palmBarrel, sightDirection) * palmBarrelRotation;
				}

				Vector3 smoothedSightDirection = sightLine.Update(sightDirection, tapFingerSpeed, deltaTime);
				Quaternion palmToSightLine = Quaternion.Slerp(Quaternion.identity,
					Quaternion.FromToRotation(palmBarrel, smoothedSightDirection), settings.barrelFollowsSightLine);
				return palmToSightLine * palmBarrelRotation;
			}

			private static float DistanceToIndexFinger(Vector3 point,
				Vector3 knuckle, Vector3 middle, Vector3 distal, Vector3 tip) =>
				Mathf.Min(DistanceToSegment(point, knuckle, middle),
					DistanceToSegment(point, middle, distal),
					DistanceToSegment(point, distal, tip));

			private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
			{
				Vector3 segment = end - start;
				float along = Mathf.Clamp01(Vector3.Dot(point - start, segment) / Mathf.Max(segment.sqrMagnitude, 1e-8f));
				return Vector3.Distance(point, start + segment * along);
			}

			// palm joint up is the back of the hand
			private static Vector3 PalmNormal(Pose palm) => palm.rotation * Vector3.down;

			private bool IsPalmFacingHead(Pose palm, Pose head)
			{
				Vector3 toHead = (head.position - palm.position).normalized;
				return Vector3.Dot(PalmNormal(palm), toHead) > settings.palmFacingHeadCosine;
			}

			public void MarkUntracked()
			{
				tapHeld = false;
				hasLastTapGap = false;
				pointerDirection.Reset();
				barrelDirection.Reset();
				barrelTop.Reset();
				sightLine.Reset();

				if (device == null || !reportedTracked)
					return;

				reportedTracked = false;
				state.isTracked = false;
				state.trackingState = (int)InputTrackingState.None;
				state.triggerPressed = false;
				state.trigger = 0;
				state.menu = false;
				Publish();
			}

			private void EnsureDevice()
			{
				if (device != null && device.added)
					return;

				device = InputSystem.AddDevice<TrackedHandController>(
					side == XRHandedness.Left ? "Left Tracked Hand" : "Right Tracked Hand");
			}

			public void RemoveDevice()
			{
				if (device != null && device.added)
					InputSystem.RemoveDevice(device);
				device = null;
				reportedTracked = false;
			}

			// An untracked hand or a held controller gives up the hand usage, so a stale hand pose
			// never competes with the controller for the same bindings.
			private void Publish()
			{
				bool shouldOwnUsage = state.isTracked && !IsRealControllerTracked();
				bool ownsUsage = device.usages.Contains(handUsage);

				if (shouldOwnUsage && !ownsUsage)
					InputSystem.SetDeviceUsage(device, handUsage);

				InputSystem.QueueStateEvent(device, state);

				if (!shouldOwnUsage && ownsUsage)
					InputSystem.RemoveDeviceUsage(device, handUsage);
			}

			private bool IsRealControllerTracked()
			{
				var devices = InputSystem.devices;
				for (int i = 0; i < devices.Count; i++)
				{
					if (devices[i] is not XRController controller || controller is TrackedHandController)
						continue;

					if (controller.usages.Contains(handUsage) &&
					    (controller.isTracked.isPressed || controller.trackingState.value != 0))
						return true;
				}

				return false;
			}
		}
	}
}
