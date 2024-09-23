using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class TeamIndicator : MonoBehaviour
    {
        [SerializeField] private Image image;

		private void OnValidate()
		{
			this.SetComponent(ref image);
		}

		private void Update()
		{
			byte team = 0;

			if(MainPlayer.Instance.networkPlayer != null)
			{
				team = MainPlayer.Instance.networkPlayer.Team;
			}

			image.color = TeamManagement.TeamColors[team];
		}
	}
}
