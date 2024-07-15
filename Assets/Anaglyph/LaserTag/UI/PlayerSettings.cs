using Anaglyph.LaserTag.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.LaserTag
{
    public class PlayerSettings : MonoBehaviour
    {
        [SerializeField]
        private Text teamNumberText;

        private void Update()
        {
            teamNumberText.text = $"{MainPlayer.Instance.currentRole.TeamNumber}";
        }

        public void SetTeam(byte team)
        {
            MainPlayer.Instance.team = team;
        }

        public void IncrementTeamNumber()
        {
            MainPlayer.Instance.currentRole.TeamNumber++;
        }

        public void DecrementTeamNumber()
        {
            MainPlayer.Instance.currentRole.TeamNumber--;
        }

        public void SetBaseAffinity(bool shouldReturn)
        {
            MainPlayer.Instance.currentRole.ReturnToBaseOnDie = shouldReturn;
        }
    }
}
