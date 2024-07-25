using Anaglyph.LaserTag;
using UnityEngine;

namespace Anaglyph.Lasertag.Menu
{
	public class PlayerSettingsMenu : MonoBehaviour
	{
		public void SetPlayerNickname(string playerName)
		{
			MainPlayer.Instance.networkPlayer.nicknameSync.Value = playerName;
		}

		public void SetPlayerTeam(int team)
		{
			MainPlayer.Instance.networkPlayer.TeamOwner.teamSync.Value = (byte)team;
		}
	}
}
