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
	public struct RoundSettings : INetworkSerializeByMemcpy
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

		public NetworkVariable<float> timeRoundEndsSync = new();
		public float TimeRoundEnds => timeRoundEndsSync.Value;

		//public NetworkList<int> teamScoresSync;
		public NetworkVariable<int>[] teamScoresSync;
		public int GetTeamScore(byte team) => teamScoresSync[team].Value;

		private NetworkVariable<RoundSettings> activeSettingsSync = new();
		public RoundSettings ActiveSettings => activeSettingsSync.Value;

		private Coroutine roundQueueCoroutineHandle;
		private Coroutine controlPointScoreLoopCoroutineHandle;
		private Coroutine roundTimerCoroutineHandle;

		public static event Action OnGameCountdownEveryone = delegate { };
		public static event Action OnGameStartEveryone = delegate { };
		public static event Action OnGameEndEveryone = delegate { };
		public static event Action OnBecomeGameMasterLocal = delegate { };
		public static event Action OnLoseGameMasterLocal = delegate { };

		private void Awake()
		{
			Instance = this;
			//teamScoresSync = new NetworkList<int>();

			teamScoresSync = new NetworkVariable<int>[TeamManagement.NumTeams]; 
			for (int i = 0; i < teamScoresSync.Length; i++)
			{
				NetworkVariable<int> score = new(0);
				score.Initialize(this);

				teamScoresSync[i] = score;
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

			if(RoundState == RoundState.Playing)
				SubscribeToEvents();
		}

		public override void OnLostOwnership()
		{
			OnLoseGameMasterLocal.Invoke();

			UnsubscribeFromEvents();
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			UnsubscribeFromEvents();
		}

		public override void OnNetworkDespawn()
		{
			UnsubscribeFromEvents();
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc(RoundSettings gameSettings)
		{
			activeSettingsSync.Value = gameSettings;
			roundStateSync.Value = RoundState.Queued;
			roundQueueCoroutineHandle = StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			if (!IsOwner)
				yield return null;

			ResetScoresRpc();

			if (ActiveSettings.respawnInBases)
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

			foreach(Player player in Player.AllPlayers.Values)
			{
				player.ResetScoreRpc();
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
			roundStateSync.Value = RoundState.Playing;
			SubscribeToEvents();
			StartGameEveryoneRpc();
		}

		private void SubscribeToEvents()
		{
			if (ActiveSettings.CheckWinByTimer())
			{
				timeRoundEndsSync.Value = (float)NetworkManager.LocalTime.Time + ActiveSettings.timerSeconds;
				roundTimerCoroutineHandle = StartCoroutine(GameTimerAsOwnerCoroutine());
			}

			// sub to score events
			Player.OnPlayerKilledPlayer += OnPlayerKilledPlayerAsOwner;
			controlPointScoreLoopCoroutineHandle = StartCoroutine(ControlPointLoopCoroutine());
		}

		[Rpc(SendTo.Everyone)]
		public void StartGameEveryoneRpc()
		{
			OnGameStartEveryone.Invoke();
		}

		private IEnumerator GameTimerAsOwnerCoroutine()
		{
			yield return new WaitForSeconds(ActiveSettings.timerSeconds);

			EndGameOwnerRpc();
		}

		private void OnPlayerKilledPlayerAsOwner(Player killer, Player victim)
		{
			if (!IsOwner)
				return;

			if (ActiveSettings.teams)
			{
				ScoreRpc(killer.Team, ActiveSettings.pointsPerKill);
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
						ScoreRpc(point.ControllingTeam, ActiveSettings.pointsPerSecondHoldingPoint);
					}
				}

				yield return new WaitForSeconds(1);
			}
		}

		[Rpc(SendTo.Owner)]
		public void ScoreRpc(byte team, int points)
		{
			teamScoresSync[team].Value += points;

			if (ActiveSettings.CheckWinByPoints() && teamScoresSync[team].Value > ActiveSettings.scoreTarget)
			{
				EndGameOwnerRpc();
			}
		}

		[Rpc(SendTo.Owner)]
		public void EndGameOwnerRpc()
		{
			roundStateSync.Value = RoundState.NotPlaying;

			UnsubscribeFromEvents();

			EndGameEveryoneRpc();
		}

		private void UnsubscribeFromEvents()
		{
			Player.OnPlayerKilledPlayer -= OnPlayerKilledPlayerAsOwner;

			if (roundQueueCoroutineHandle != null)
				StopCoroutine(roundQueueCoroutineHandle); ;

			if (controlPointScoreLoopCoroutineHandle != null)
				StopCoroutine(controlPointScoreLoopCoroutineHandle);

			if (roundTimerCoroutineHandle != null)
				StopCoroutine(roundTimerCoroutineHandle);
		}

		[Rpc(SendTo.Everyone)]
		private void EndGameEveryoneRpc()
		{
			OnGameEndEveryone.Invoke();
		}
	}
}
