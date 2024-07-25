using Anaglyph.LaserTag;
using Anaglyph.LaserTag.Networking;
using System;
using System.Collections;
using System.Runtime.InteropServices.WindowsRuntime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	public enum WinCondition : byte
	{
		None = 0,
		Timer = 1,
		ReachScore = 2,
	}

	public enum RoundState : byte
	{
		NotPlaying = 0,
		Queued = 1,
		Countdown = 2,
		Playing = 3,
	}

	[Serializable]
	public struct GameSettings : INetworkSerializeByMemcpy
	{
		public bool teams;
		public bool respawnInBases;

		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;

		public WinCondition winCondition;
		public int timerSeconds;
		public short scoreTarget;

		public bool CheckWinByTimer() => winCondition.HasFlag(WinCondition.Timer);
		public bool CheckWinByPoints() => winCondition.HasFlag(WinCondition.ReachScore);
	}

	public class RoundManager : NetworkBehaviour
	{
		public static RoundManager Instance { get; private set; }

		public NetworkVariable<RoundState> roundStateSync = new();
		public RoundState RoundState => roundStateSync.Value;

		public NetworkVariable<float> timeGameEndsSync = new();
		public float TimeGameEnds => timeGameEndsSync.Value;

		//public NetworkList<int> teamScoresSync;
		public NetworkVariable<int>[] teamScoresSync = new NetworkVariable<int>[TeamManagement.NumTeams];
		public int GetTeamScore(byte team) => teamScoresSync[team].Value;

		private NetworkVariable<GameSettings> activeGameSettingsSync = new();
		public GameSettings ActiveGameSettings => activeGameSettingsSync.Value;

		private Coroutine gameQueueCoroutineHandle;
		private Coroutine controlPointScoreLoopCoroutineHandle;
		private Coroutine gameTimerCoroutineHandle;

		public static event Action OnGameCountdownEveryone = delegate { };
		public static event Action OnGameStartEveryone = delegate { };
		public static event Action OnGameEndEveryone = delegate { };
		public static event Action OnBecomeGameMasterLocal = delegate { };
		public static event Action OnLoseGameMasterLocal = delegate { };

		private void Awake()
		{
			Instance = this;
			//teamScoresSync = new NetworkList<int>();

			for(int i = 0; i < teamScoresSync.Length; i++)
			{
				teamScoresSync[i] = new();
			}
		}

		//public override void OnNetworkSpawn()
		//{
		//	for (int i = 0; i < TeamManagement.NumTeams; i++)
		//	{
		//		teamScoresSync.Add(0);
		//	}
		//}

		public override void OnGainedOwnership()
		{
			OnBecomeGameMasterLocal.Invoke();
		}

		public override void OnLostOwnership()
		{
			OnLoseGameMasterLocal.Invoke();
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc(GameSettings gameSettings)
		{
			activeGameSettingsSync.Value = gameSettings;
			roundStateSync.Value = RoundState.Queued;
			gameQueueCoroutineHandle = StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			if (!IsOwner)
				yield return null;

			ResetScoresRpc();

			if (ActiveGameSettings.respawnInBases)
			{
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
			}

			roundStateSync.Value = RoundState.Countdown;

			StartCountdownEveryoneRpc();

			yield return new WaitForSeconds(4);

			StartGameOwnerRpc();
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoresRpc()
		{
			for (byte i = 0; i < teamScoresSync.Length; i++)
			{
				NetworkVariable<int> score = new();
				score.Initialize(this);
				teamScoresSync[i] = score;
			}
		}

		[Rpc(SendTo.Everyone)]
		private void StartCountdownEveryoneRpc()
		{
			OnGameCountdownEveryone.Invoke();
		}

		[Rpc(SendTo.Owner)]
		public void StartGameOwnerRpc()
		{
			if (ActiveGameSettings.CheckWinByTimer())
			{
				timeGameEndsSync.Value = (float)NetworkManager.LocalTime.Time + ActiveGameSettings.timerSeconds;
				gameTimerCoroutineHandle = StartCoroutine(GameTimerAsOwnerCoroutine());
			}

			roundStateSync.Value = RoundState.Playing;

			// sub to score events
			Player.OnPlayerKilledPlayer += OnPlayerKilledPlayerAsOwner;
			controlPointScoreLoopCoroutineHandle = StartCoroutine(ControlPointLoopCoroutine());

			StartGameEveryoneRpc();
		}

		[Rpc(SendTo.Everyone)]
		public void StartGameEveryoneRpc()
		{
			OnGameStartEveryone.Invoke();
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
			while (RoundState == RoundState.Playing)
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
		public void EndGameOwnerRpc()
		{
			roundStateSync.Value = RoundState.NotPlaying;

			Player.OnPlayerKilledPlayer -= OnPlayerKilledPlayerAsOwner;

			if (gameQueueCoroutineHandle != null)
				StopCoroutine(gameQueueCoroutineHandle);;

			if (controlPointScoreLoopCoroutineHandle != null)
				StopCoroutine(controlPointScoreLoopCoroutineHandle);

			if (gameTimerCoroutineHandle != null)
				StopCoroutine(gameTimerCoroutineHandle);

			EndGameEveryoneRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void EndGameEveryoneRpc()
		{
			OnGameEndEveryone.Invoke();
		}
	}
}
