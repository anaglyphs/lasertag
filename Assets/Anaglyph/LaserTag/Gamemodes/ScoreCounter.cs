using Unity.Netcode;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class ScoreCounter : NetworkBehaviour
	{
		private NetworkVariable<int>[] scores = new NetworkVariable<int>[TeamManagement.NumTeams];

		public int scoreTarget = 0;
		public byte winningTeam { get; private set; } = 0;
		public UnityEvent<byte> OnTeamWin;

		private void Awake()
		{
			for (byte i = 0; i < scores.Length; i++)
			{
				scores[i] = new NetworkVariable<int>(0);

				int iCap = i;
				scores[i].OnValueChanged += delegate (int prevSore, int newScore)
				{
					if (scoreTarget > 0 && winningTeam == 0 && prevSore < scoreTarget && newScore >= scoreTarget)
					{
						winningTeam = i;
						OnTeamWin.Invoke(winningTeam);
					}
				};
			}
		}

		public void Score(byte team, int points)
		{
			scores[team].Value += points;
		}

		public void ResetScores()
		{
			for(byte i = 0; i < scores.Length; i++)
			{
				scores[i].Value = 0;
			}
		}
	}
}
