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
		public float respawnSeconds;
		public float healthRegenPerSecond;

		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;

		public WinCondition winCondition;
		public int timerSeconds;
		public short scoreTarget;

		public bool CheckWinByTimer() => winCondition.HasFlag(WinCondition.Timer);
		public bool CheckWinByPoints() => winCondition.HasFlag(WinCondition.ReachScore);

		public static RoundSettings DemoGame()
		{
			return new()
			{
				teams = true,
				respawnInBases = true,
				respawnSeconds = 1,
				healthRegenPerSecond = 5,

				pointsPerKill = 2,
				pointsPerSecondHoldingPoint = 1,

				winCondition = WinCondition.Timer,
				timerSeconds = 60 * 5,
			};
		}

		public static RoundSettings Lobby()
		{
			return new()
			{
				teams = false,
				respawnInBases = false,
				respawnSeconds = 5,
				healthRegenPerSecond = 5,

				pointsPerKill = 0,
				pointsPerSecondHoldingPoint = 0,

				winCondition = WinCondition.None,
				timerSeconds = 0,
			};
		}
	}

	public class RoundManager : NetworkBehaviour
	{
		private const string NotOwnerExceptionMessage = "Only the NGO owner should call this!";

		public static RoundManager Instance { get; private set; }

		private NetworkVariable<RoundState> roundStateSync = new(RoundState.NotPlaying);
		public static RoundState RoundState => Instance.roundStateSync.Value;

		private NetworkVariable<float> timeRoundEndsSync = new(0);
		public static float TimeRoundEnds => Instance.timeRoundEndsSync.Value;

		private NetworkVariable<int> team0ScoreSync = new(0);
		private NetworkVariable<int> team1ScoreSync = new(0);
		private NetworkVariable<int> team2ScoreSync = new(0);

		private NetworkVariable<int>[] teamScoresSync;
		private NetworkVariable<byte> winningTeamSync = new();
		public static int GetTeamScore(byte team) => Instance.teamScoresSync[team].Value;
		public static byte WinningTeam => Instance.winningTeamSync.Value;

		private NetworkVariable<RoundSettings> roundSettingsSync = new();
		public static RoundSettings Settings => Instance.roundSettingsSync.Value;

		public static event Action<RoundState, RoundState> OnRoundStateChange = delegate { };

		private void OwnerCheck()
		{
			if(!IsOwner) throw new Exception(NotOwnerExceptionMessage);
		}

		private void Awake()
		{
			Instance = this;

			teamScoresSync = new NetworkVariable<int>[Teams.NumTeams];

			teamScoresSync[0] = team0ScoreSync;
			teamScoresSync[1] = team1ScoreSync;
			teamScoresSync[2] = team2ScoreSync;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				roundStateSync.Value = RoundState.NotPlaying;
				roundSettingsSync.Value = RoundSettings.Lobby();
			}
			
			roundStateSync.OnValueChanged += OnStateUpdateLocally;

			OnStateUpdateLocally(RoundState.NotPlaying, RoundState.NotPlaying);
		}

		private void Update()
		{
			if (!IsSpawned) return;

			Networking.Avatar avatar = MainPlayer.Instance.avatar;

			if (RoundState == RoundState.NotPlaying || RoundState == RoundState.Queued || avatar.Team == 0) {
				
				if (avatar.IsInBase)
					avatar.TeamOwner.teamSync.Value = avatar.InBase.Team;
			}
		}

		private void OnStateUpdateLocally(RoundState prev, RoundState state)
		{
			switch (state)
			{
				case RoundState.NotPlaying:

					MainPlayer.Instance.Respawn();
					if (IsOwner)
						roundSettingsSync.Value = RoundSettings.Lobby();

					break;

				case RoundState.Queued:

					break;

				case RoundState.Countdown:

					break;

				case RoundState.Playing:

					MainPlayer.Instance.Respawn();

					break;
			}

			OnRoundStateChange.Invoke(prev, state);
		}

		public override void OnGainedOwnership()
		{
			if(RoundState == RoundState.Playing)
				SubscribeToEvents();
		}

		public override void OnLostOwnership()
		{
			UnsubscribeFromEvents();
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			UnsubscribeFromEvents();
		}

		public override void OnNetworkDespawn()
		{
			OnStateUpdateLocally(roundStateSync.Value, RoundState.NotPlaying);
			UnsubscribeFromEvents();
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc(RoundSettings gameSettings)
		{
			roundSettingsSync.Value = gameSettings;
			roundStateSync.Value = RoundState.Queued;
			StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			OwnerCheck();

			ResetScoresRpc();

			yield return new WaitForSeconds(1);

			while (RoundState == RoundState.Queued)
			{
				if (Settings.respawnInBases)
				{
					int numPlayersInbase = 0;

					foreach (Networking.Avatar player in Networking.Avatar.AllPlayers.Values)
					{
						if (player.IsInBase)
							numPlayersInbase++;
					}

					if (numPlayersInbase != 0 && numPlayersInbase == Networking.Avatar.AllPlayers.Count)
						roundStateSync.Value = RoundState.Countdown;

				} else
					roundStateSync.Value = RoundState.Countdown;

				yield return null;
			}

			if(RoundState == RoundState.Countdown)
				yield return new WaitForSeconds(3);

			if(RoundState == RoundState.Countdown)
				StartGameOwnerRpc();
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoresRpc()
		{
			for (byte i = 0; i < teamScoresSync.Length; i++)
			{
				teamScoresSync[i].Value = 0;
			}

			foreach(Networking.Avatar player in Networking.Avatar.AllPlayers.Values)
			{
				player.ResetScoreRpc();
			}

			foreach (ControlPoint point in ControlPoint.AllControlPoints)
			{
				point.ResetPointRpc();
			}

			winningTeamSync.Value = 0;
		}

		[Rpc(SendTo.Owner)]
		private void StartGameOwnerRpc()
		{
			ResetScoresRpc();

			if (Settings.CheckWinByTimer())
				timeRoundEndsSync.Value = (float)NetworkManager.LocalTime.Time + Settings.timerSeconds;

			roundStateSync.Value = RoundState.Playing;
			SubscribeToEvents();
		}

		private void SubscribeToEvents()
		{
			OwnerCheck();

			if (Settings.CheckWinByTimer())
				StartCoroutine(GameTimerAsOwnerCoroutine());

			// sub to score events
			Networking.Avatar.OnPlayerKilledPlayer += OnPlayerKilledPlayer;
			StartCoroutine(ControlPointLoopCoroutine());
		}

		private IEnumerator GameTimerAsOwnerCoroutine()
		{
			OwnerCheck();

			while (RoundState == RoundState.Playing)
			{
				if(NetworkManager.LocalTime.TimeAsFloat > TimeRoundEnds)
					EndGameOwnerRpc();

				yield return null;
			}

			EndGameOwnerRpc();
		}

		private void OnPlayerKilledPlayer(Networking.Avatar killer, Networking.Avatar victim)
		{
			OwnerCheck();

			if (Settings.teams)
			{
				ScoreTeamRpc(killer.Team, Settings.pointsPerKill);
			} else
			{
				
			}
		}

		private IEnumerator ControlPointLoopCoroutine()
		{
			OwnerCheck();

			while (RoundState == RoundState.Playing)
			{
				foreach (ControlPoint point in ControlPoint.AllControlPoints)
				{
					if (point.MillisCaptured == 0 && point.HoldingTeam != 0)
					{
						ScoreTeamRpc(point.HoldingTeam, Settings.pointsPerSecondHoldingPoint);
					}
				}

				yield return new WaitForSeconds(1);
			}
		}

		[Rpc(SendTo.Owner)]
		public void ScoreTeamRpc(byte team, int points)
		{
			if (team == 0) return;

			teamScoresSync[team].Value += points;

			byte winningTeam = 0;
			int highScore = 0;
			for(byte i = 0; i < teamScoresSync.Length; i++)
			{
				int score = GetTeamScore(i);
				if (score > highScore) {
					highScore = score;
					winningTeam = i;
				}
			}
			winningTeamSync.Value = winningTeam;

			if (Settings.CheckWinByPoints() && teamScoresSync[team].Value > Settings.scoreTarget)
			{
				EndGameOwnerRpc();
			}
		}

		[Rpc(SendTo.Owner)]
		public void EndGameOwnerRpc()
		{
			roundStateSync.Value = RoundState.NotPlaying;
			UnsubscribeFromEvents();
		}

		private void UnsubscribeFromEvents()
		{
			Networking.Avatar.OnPlayerKilledPlayer -= OnPlayerKilledPlayer;
		}


	}
}
