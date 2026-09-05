using Anaglyph.XR.Input;
using Anaglyph.XR.SharedSpaces;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// What an operator needs to know about a connected headset that the game itself
	/// doesn't care about. Sampled by the owner and replicated on
	/// <see cref="PlayerAvatar"/>, so it arrives and leaves with the headset.
	///
	/// Controllers report tracking rather than battery because nothing on this stack reports
	/// their battery: OpenXR exposes no battery at all, and Meta deprecated the OVRInput reads
	/// that used to.
	/// </summary>
	public struct HeadsetTelemetry : INetworkSerializable
	{
		/// <summary>Stands in for a platform with no battery to read - desktop, editor.</summary>
		public const byte UnknownBatteryPercent = 255;

		public byte batteryPercent;
		public bool isCharging;

		public ColocationAlignmentState alignment;

		/// <summary>How many colocation references the headset can see right now.</summary>
		public byte constraintCount;

		/// <summary>How many of those land where the applied alignment says they should.</summary>
		public byte agreeingConstraintCount;

		/// <summary>
		/// <see cref="InputTrackingState"/> flags per hand, or <see cref="TrackingUnavailable"/>
		/// where the rig has no hand to read - a device with the panel but no XR rig.
		/// </summary>
		public byte leftHandTracking;
		public byte rightHandTracking;

		public const byte TrackingUnavailable = 255;

		public bool BatteryIsKnown => batteryPercent != UnknownBatteryPercent;

		public static HeadsetTelemetry Sample()
		{
			ColocationManager colocation = ColocationManager.Instance;
			FitAgreement agreement = colocation != null ? colocation.Agreement : default;

			return new HeadsetTelemetry
			{
				batteryPercent = ReadBatteryPercent(),
				isCharging = SystemInfo.batteryStatus is BatteryStatus.Charging or BatteryStatus.Full,
				alignment = colocation != null
					? colocation.AlignmentState
					: ColocationAlignmentState.Stopped,
				constraintCount = ClampToByte(agreement.constraintCount),
				agreeingConstraintCount = ClampToByte(agreement.agreeingCount),
				leftHandTracking = ReadHandTracking(Handedness.Left),
				rightHandTracking = ReadHandTracking(Handedness.Right),
			};
		}

		private static byte ReadHandTracking(Handedness handedness)
		{
			HandInput hand = HandInput.Get(handedness);

			// A controller that is off or asleep stays bound and reports None, so an absent
			// HandInput means the rig itself isn't there rather than a missing controller.
			return hand != null ? (byte)hand.TrackingState : TrackingUnavailable;
		}

		private static byte ReadBatteryPercent()
		{
			float level = SystemInfo.batteryLevel;

			return level < 0f
				? UnknownBatteryPercent
				: (byte)Mathf.Clamp(Mathf.RoundToInt(level * 100f), 0, 100);
		}

		private static byte ClampToByte(int count) => (byte)Mathf.Clamp(count, 0, 254);

		public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
		{
			serializer.SerializeValue(ref batteryPercent);
			serializer.SerializeValue(ref isCharging);
			serializer.SerializeValue(ref alignment);
			serializer.SerializeValue(ref constraintCount);
			serializer.SerializeValue(ref agreeingConstraintCount);
			serializer.SerializeValue(ref leftHandTracking);
			serializer.SerializeValue(ref rightHandTracking);
		}
	}
}
