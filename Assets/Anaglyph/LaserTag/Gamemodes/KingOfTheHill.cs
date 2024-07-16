using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class KingOfTheHill : NetworkBehaviour
    {
        [SerializeField] private TeamScores teamScores;

		private void OnValidate()
		{
			this.SetComponent(ref teamScores);

			teamScores.OnTeamWin.AddListener(OnTeamWin);
		}

		public void StartGame()
		{
			teamScores.ResetScores();
		}

		private void OnTeamWin(byte team)
		{

		}

		
	}
}
