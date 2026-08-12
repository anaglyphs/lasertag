using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using StrikerLink.Shared.Devices.Types;
using StrikerLink.Unity.Runtime.Core;
using UnityEngine;

namespace Anaglyph.LaserTag.ControllerSupport
{
	public class StrikerLEDColorer : MonoBehaviour
	{
		private StrikerDevice strikerDevice;

		private void Awake()
		{
			TryGetComponent(out strikerDevice);
			MainPlayer.TeamChanged += OnTeamChanged;
		}

		private void OnDestroy()
		{
			MainPlayer.TeamChanged -= OnTeamChanged;
		}

		private void OnTeamChanged(byte team)
		{
			var teamColor = Teams.Colors[team];

			if (strikerDevice.isConnected)
			{
				strikerDevice.PlaySolidLedEffect(teamColor, group: DeviceMavrik.LedGroup.TopLine);
				strikerDevice.PlaySolidLedEffect(teamColor, group: DeviceMavrik.LedGroup.FrontRings);
			}

			strikerDevice.connectedLedColor = teamColor;
		}
	}
}