using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class GameMenu : MonoBehaviour
    {
        public void BecomeGameMaster()
        {
            RoundManager.Instance.NetworkObject.RequestOwnership();
        }

        public void SetTime(float time)
        {
			RoundManager.Instance.

		}
    }
}
