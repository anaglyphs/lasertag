using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.XR.Input;
using StrikerLink.Shared.Devices;
using StrikerLink.Shared.Devices.Types;
using StrikerLink.Shared.Haptics.Types;
using StrikerLink.Unity.Runtime.Core;
using UnityEngine;

namespace Anaglyph.LaserTag.ControllerSupport
{
	public class StrikerLEDColorer : MonoBehaviour
	{
		// matches the device StrikerInputDevice reads input from
		private const ushort StrikerDeviceIndex = 0;

		private void Awake()
		{
			MainPlayer.TeamChanged += OnTeamChanged;
			MountedPeripheral.Changed += OnMountedPeripheralChanged;
		}

		private void OnDestroy()
		{
			MainPlayer.TeamChanged -= OnTeamChanged;
			MountedPeripheral.Changed -= OnMountedPeripheralChanged;
		}

		private void OnTeamChanged(byte team) => ApplyTeamColor(team);

		// the peripheral loses its colors when it disconnects, so paint it again on reconnect
		private void OnMountedPeripheralChanged(HandPeripheral peripheral) =>
			ApplyTeamColor(MainPlayer.Instance.Team);

		private void ApplyTeamColor(byte team)
		{
			if (MountedPeripheral.Current == null)
				return;

			Color teamColor = Teams.Colors[team];

			SetLeds(teamColor, DeviceMavrik.LedGroup.TopLine);
			SetLeds(teamColor, DeviceMavrik.LedGroup.FrontRings);
		}

		private static void SetLeds(Color color, DeviceMavrik.LedGroup group) =>
			StrikerController.Controller.GetClient().SendBasicLedEffect(
				StrikerDeviceIndex, DeviceBase.LedSequence.Solid, group, DeviceMavrik.LedMask.All,
				new LedCommand.LedColor(color.r, color.g, color.b), new LedCommand.LedColor(), 0f, 0);
	}
}
