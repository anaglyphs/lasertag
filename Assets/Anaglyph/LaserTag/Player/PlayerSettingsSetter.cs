using Anaglyph.LaserTag.Networking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.LaserTag
{
    public class PlayerSettingsSetter : MonoBehaviour
    {
        [SerializeField]
        private Text teamNumberText;

        private void Update()
        {
            teamNumberText.text = $"{PlayerLocal.Instance.currentRole.TeamNumber}";
        }


        public void IncrementTeamNumber()
        {
            PlayerLocal.Instance.currentRole.TeamNumber++;
        }

        public void DecrementTeamNumber()
        {
            PlayerLocal.Instance.currentRole.TeamNumber--;
        }

        public void SetBaseAffinity(bool shouldReturn)
        {
            PlayerLocal.Instance.currentRole.ReturnToBaseOnDie = shouldReturn;
        }
    }
}
