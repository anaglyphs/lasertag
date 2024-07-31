using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class GameSettingsMenu : MonoBehaviour
	{
		public static readonly RoundSettings TeamDeathmatchPreset = new RoundSettings()
		{
			teams = true,
			respawnInBases = true,

			pointsPerKill = 1,
			pointsPerSecondHoldingPoint = 0,

			winCondition = WinCondition.Timer,
			timerSeconds = 300,
			scoreTarget = 0,
		};

		public static readonly RoundSettings KingOfTheHillPreset = new RoundSettings()
		{
			teams = true,
			respawnInBases = true,

			pointsPerKill = 0,
			pointsPerSecondHoldingPoint = 1,

			winCondition = WinCondition.Timer,
			timerSeconds = 300,
			scoreTarget = 0,
		};

		public RoundSettings settings = new();

		[SerializeField] private GameObject startGamePage;
		[SerializeField] private GameObject gameRunningPage;

		private void Awake()
		{
			settings = TeamDeathmatchPreset;
		}

		private void Start()
		{
			RoundManager.Instance.roundStateSync.OnValueChanged += OnGameStateChange;

			OnGameStateChange(0, RoundManager.Instance.RoundState);
		}

		private void OnGameStateChange(RoundState prev, RoundState state)
		{
			if (RoundManager.Instance.RoundState != RoundState.NotPlaying)
				gameRunningPage.SetActive(true);
			else
				startGamePage.SetActive(true);
		}

		public void BecomeGameMaster()
		{
			RoundManager.Instance.NetworkObject.RequestOwnership();
		}

		public void StartGame()
		{
			RoundManager.Instance.QueueStartGameOwnerRpc(settings);
		}

		public void EndGame()
		{
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

		public void SetTeamDeathmatchPreset() => CopySettingsWithSeconds(TeamDeathmatchPreset, settings.timerSeconds);
		public void SetKingOfTheHillPreset() => CopySettingsWithSeconds(KingOfTheHillPreset, settings.timerSeconds);

		private void CopySettingsWithSeconds(RoundSettings copyFrom, int timerSeconds)
		{
			settings = copyFrom;
			settings.timerSeconds = timerSeconds;
		}
	}
}
