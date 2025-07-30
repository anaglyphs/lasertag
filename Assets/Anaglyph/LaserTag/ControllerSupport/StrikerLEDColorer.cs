using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Shared.Devices.Types;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class StrikerLEDColorer : MonoBehaviour
	{
		private StrikerDevice strikerDevice;

		private void Awake()
		{
			TryGetComponent(out strikerDevice);
		}

		private void Start()
		{
			MainPlayer.Instance.TeamChanged += OnTeamChanged;
		}

		private void OnTeamChanged(byte team)
		{
			Color teamColor = Teams.Colors[team];

			strikerDevice.PlaySolidLedEffect(teamColor, group: DeviceMavrik.LedGroup.TopLine);
			strikerDevice.PlaySolidLedEffect(teamColor, group: DeviceMavrik.LedGroup.FrontRings);
			strikerDevice.connectedLedColor = teamColor;
		}
	}
}
