using Anaglyph.LaserTag;
using Anaglyph.LaserTag.Networking;
using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public enum WinCondition : byte
	{
		None = 0,
		Timer = 1,
		PointsTarget = 2,
	}

	[Serializable]
	public struct GameSettings
	{
		public bool teams;
		public bool respawnInBases;
		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;
		public WinCondition winCondition;
		public bool useTimer;
		public int timerSeconds;
		public int scoreTarget;

		public bool CheckWinByTimer() => (winCondition & WinCondition.Timer) != WinCondition.None;

		public bool CheckWinByPoints() => (winCondition & WinCondition.PointsTarget) != WinCondition.None;
	}

	public class RoundManager : NetworkBehaviour
	{
		public static RoundManager Instance { get; private set; }

		private NetworkVariable<bool> gameIsOnSync;
		public bool GameIsOn => gameIsOnSync.Value;

		private NetworkVariable<float> timeGameEndsSync;
		public float TimeGameEnds => timeGameEndsSync.Value;

		private NetworkVariable<int>[] teamScoresSync;
		public int GetTeamScore(byte team) => teamScoresSync[team].Value;

		private NetworkVariable<GameSettings> activeGameSettingsSync;
		public GameSettings ActiveGameSettings => activeGameSettingsSync.Value;

		private Coroutine gameQueueCoroutineHandle;
		private Coroutine controlPointScoreLoopCoroutineHandle;
		private Coroutine gameTimerCoroutineHandle;

		public static event Action OnGameCountdown = delegate { };
		public static event Action OnGameStart = delegate { };
		public static event Action OnGameEnd = delegate { };

		private void Awake()
		{
			Instance = this;

			teamScoresSync = new NetworkVariable<int>[TeamManagement.NumTeams];
			for(int i = 0; i < teamScoresSync.Length; i++)
				teamScoresSync[i] = new NetworkVariable<int>(0);

			Player.OnPlayerKilledPlayer += OnPlayerKilledPlayerAsOwner;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			Player.OnPlayerKilledPlayer -= OnPlayerKilledPlayerAsOwner;
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc()
		{
			if (!IsOwner)
				return;

			gameQueueCoroutineHandle = StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		[Rpc(SendTo.Owner)]
		public void CancelQueuedGameOwnerRpc()
		{
			if (gameQueueCoroutineHandle != null)
				StopCoroutine(gameQueueCoroutineHandle);
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			if (!IsOwner)
				yield return null;

			ResetScoresRpc();

			if (ActiveGameSettings.respawnInBases)
			{
				GlobalMessage.Instance.Set("Everyone must be in base for the round to start!");

				int numPlayersInbase;
				do
				{
					numPlayersInbase = 0;

					foreach (Player player in Player.AllPlayers.Values)
					{
						if (player.IsInBase)
							numPlayersInbase++;
					}

					yield return null;

				} while (numPlayersInbase < Player.AllPlayers.Count);

				GlobalMessage.Instance.Set("");
			}

			StartCountdownEveryoneRpc();

			yield return new WaitForSeconds(4);

			StartGameOwnerRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void StartCountdownEveryoneRpc()
		{
			OnGameCountdown.Invoke();
		}

		[Rpc(SendTo.Owner)]
		public void StartGameOwnerRpc()
		{
			if (ActiveGameSettings.CheckWinByTimer())
			{
				timeGameEndsSync.Value = (float)NetworkManager.LocalTime.Time + ActiveGameSettings.timerSeconds;
				gameTimerCoroutineHandle = StartCoroutine(GameTimerAsOwnerCoroutine());
			}

			gameIsOnSync.Value = true;

			controlPointScoreLoopCoroutineHandle = StartCoroutine(ControlPointLoopCoroutine());

			StartGameEveryoneRpc();
		}

		[Rpc(SendTo.Everyone)]
		public void StartGameEveryoneRpc()
		{
			OnGameStart.Invoke();
		}

		private IEnumerator GameTimerAsOwnerCoroutine()
		{
			yield return new WaitForSeconds(ActiveGameSettings.timerSeconds);

			EndGameOwnerRpc();
		}

		private void OnPlayerKilledPlayerAsOwner(Player killer, Player victim)
		{
			if (!IsOwner)
				return;

			if (ActiveGameSettings.teams)
			{
				ScoreRpc(killer.Team, ActiveGameSettings.pointsPerKill);
			} else
			{
				// todo player scores
			}
		}

		private IEnumerator ControlPointLoopCoroutine()
		{
			while (GameIsOn)
			{
				foreach (ControlPoint point in ControlPoint.AllControlPoints)
				{
					if (!point.IsBeingCaptured && point.ControllingTeam != 0)
					{
						ScoreRpc(point.ControllingTeam, ActiveGameSettings.pointsPerSecondHoldingPoint);
					}
				}

				yield return new WaitForSeconds(1);
			}
		}

		[Rpc(SendTo.Owner)]
		public void ScoreRpc(byte team, int points)
		{
			teamScoresSync[team].Value += points;

			if(ActiveGameSettings.CheckWinByPoints() && teamScoresSync[team].Value > ActiveGameSettings.scoreTarget)
			{
				EndGameOwnerRpc();
			}
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoresRpc()
		{
			foreach(var score in teamScoresSync)
				score.Value = 0;
		}

		[Rpc(SendTo.Owner)]
		private void EndGameOwnerRpc()
		{
			if (!IsOwner)
				return;

			gameIsOnSync.Value = false;

			if (controlPointScoreLoopCoroutineHandle != null)
				StopCoroutine(controlPointScoreLoopCoroutineHandle);

			if (gameTimerCoroutineHandle != null)
				StopCoroutine(gameTimerCoroutineHandle);

			EndGameEveryoneRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void EndGameEveryoneRpc()
		{
			OnGameEnd.Invoke();
		}
	}
}
