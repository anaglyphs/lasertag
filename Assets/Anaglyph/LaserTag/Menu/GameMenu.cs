using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class GameMenu : MonoBehaviour
	{
		public static readonly GameSettings TeamDeathmatchPreset = new GameSettings()
		{
			teams = true,
			respawnInBases = true,

			pointsPerKill = 1,
			pointsPerSecondHoldingPoint = 0,

			winCondition = WinCondition.ReachScore,
			timerSeconds = 0,
			scoreTarget = 15,
		};

		public static readonly GameSettings KingOfTheHillPreset = new GameSettings()
		{
			teams = true,
			respawnInBases = true,

			pointsPerKill = 0,
			pointsPerSecondHoldingPoint = 1,

			winCondition = WinCondition.Timer,
			timerSeconds = 300,
			scoreTarget = 0,
		};

		public GameSettings settings = new();

		[SerializeField] private GameObject startGamePage;
		[SerializeField] private GameObject becomeGameMaster;
		[SerializeField] private GameObject gameRunningPage;

		private void Start()
		{
			RoundManager.OnBecomeGameMasterLocal += UpdateActivePage;
			RoundManager.OnLoseGameMasterLocal += UpdateActivePage;
			RoundManager.OnGameStartEveryone += UpdateActivePage;
			RoundManager.OnGameEndEveryone += UpdateActivePage;

			UpdateActivePage();
		}

		private void UpdateActivePage()
		{
			if (RoundManager.Instance.GameIsOn)
			{
				gameRunningPage.SetActive(true);
			}
			else
			{
				if (RoundManager.Instance.IsOwner)
				{
					startGamePage.SetActive(true);
				} else
				{
					becomeGameMaster.SetActive(true);
				}
			}
		}

		public void BecomeGameMaster()
		{
			RoundManager.Instance.NetworkObject.RequestOwnership();
		}

		public void StartGame()
		{
			if (!RoundManager.Instance.IsOwner)
				return;

			RoundManager.Instance.QueueStartGameOwnerRpc(settings);
		}

		public void EndGame()
		{
			if (!RoundManager.Instance.IsOwner)
				return;

			RoundManager.Instance.EndGameOwnerRpc();
		}

		public void SetTeams(bool teams) => settings.teams = teams;
		public void SetReapwnInBases(bool respawnInBases) => settings.respawnInBases = respawnInBases;
		public void SetPointsPerKill(byte pointsPerKill) => settings.pointsPerKill = pointsPerKill;
		public void SetPointsPerSecondHoldingPoint(byte pointsPerSecondHoldingPoint) => 
			settings.pointsPerSecondHoldingPoint = pointsPerSecondHoldingPoint;

		public void SetWinByTimer(bool winByTimer)
		{
			if (winByTimer != settings.winCondition.HasFlag(WinCondition.Timer))
				settings.winCondition ^= WinCondition.Timer;
		}

		public void SetWinByReachingScore(bool winByScore)
		{
			if (winByScore != settings.winCondition.HasFlag(WinCondition.ReachScore))
				settings.winCondition ^= WinCondition.ReachScore;
		}

		public void SetTimerSeconds(int timerSeconds) => settings.timerSeconds = timerSeconds;
		public void SetScoreTarget(short scoreTarget) => settings.scoreTarget = scoreTarget;

		public void SetTeamDeathmatchPreset() => settings = TeamDeathmatchPreset;
		public void SetKingOfThehillPreset() => settings = KingOfTheHillPreset;
	}
}
