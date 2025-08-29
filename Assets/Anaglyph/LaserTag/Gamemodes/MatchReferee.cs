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

	public enum MatchState : byte
	{
		NotPlaying = 0,
		Queued = 1,
		Countdown = 2,
		Playing = 3,
		Finished = 4,
	}

	[Serializable]
	public struct MatchSettings : INetworkSerializeByMemcpy
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

		public static MatchSettings DemoGame()
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

		public static MatchSettings Lobby()
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

	public class MatchReferee : NetworkBehaviour
	{
		private const string NotOwnerExceptionMessage = "Only the NGO owner should call this!";

		public static MatchReferee Instance { get; private set; }

		private NetworkVariable<MatchState> stateSync = new(MatchState.NotPlaying);
		public MatchState State => stateSync.Value;
		public event Action<MatchState> StateChanged = delegate { };

		private NetworkVariable<float> timeMatchEndsSync = new(0);
		public float TimeMatchEnds => timeMatchEndsSync.Value;

		private NetworkVariable<int> team0ScoreSync = new(0);
		private NetworkVariable<int> team1ScoreSync = new(0);
		private NetworkVariable<int> team2ScoreSync = new(0);

		private NetworkVariable<int>[] teamScoresSync;
		private NetworkVariable<byte> winningTeamSync = new();
		public int GetTeamScore(byte team) => teamScoresSync[team].Value;
		public byte WinningTeam => winningTeamSync.Value;

		private NetworkVariable<MatchSettings> matchSettingsSync = new();
		public MatchSettings Settings => matchSettingsSync.Value;

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
				stateSync.Value = MatchState.NotPlaying;
				matchSettingsSync.Value = MatchSettings.Lobby();
			}
			
			stateSync.OnValueChanged += OnStateUpdateLocally;

			OnStateUpdateLocally(MatchState.NotPlaying, MatchState.NotPlaying);
		}

		private void Update()
		{
			if (!IsSpawned) return;

			var avatar = MainPlayer.Instance.Avatar;
			if (State == MatchState.NotPlaying || State == MatchState.Finished || State == MatchState.Queued || avatar.Team == 0) {
				
				if (avatar.IsInBase)
					avatar.TeamOwner.teamSync.Value = avatar.InBase.Team;
			}
		}

		private void OnStateUpdateLocally(MatchState prev, MatchState state)
		{
			switch (state)
			{
				case MatchState.NotPlaying or MatchState.Finished:

					MainPlayer.Instance.Respawn();
					if (IsOwner)
						matchSettingsSync.Value = MatchSettings.Lobby();

					break;

				case MatchState.Queued:
					MainPlayer.Instance.Respawn();
					break;

				case MatchState.Countdown:
					MainPlayer.Instance.Respawn();
					break;

				case MatchState.Playing:
					MainPlayer.Instance.Respawn();
					break;
			}

			StateChanged.Invoke(state);
		}

		public override void OnGainedOwnership()
		{
			if(State == MatchState.Playing)
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
			OnStateUpdateLocally(stateSync.Value, MatchState.NotPlaying);
			UnsubscribeFromEvents();
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc(MatchSettings gameSettings)
		{
			matchSettingsSync.Value = gameSettings;
			stateSync.Value = MatchState.Queued;
			StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			OwnerCheck();

			ResetScoresRpc();

			yield return new WaitForSeconds(1);

			while (State == MatchState.Queued)
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
						stateSync.Value = MatchState.Countdown;

				} else
					stateSync.Value = MatchState.Countdown;

				yield return null;
			}

			if(State == MatchState.Countdown)
				yield return new WaitForSeconds(3);

			if(State == MatchState.Countdown)
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
				timeMatchEndsSync.Value = (float)NetworkManager.LocalTime.Time + Settings.timerSeconds;

			stateSync.Value = MatchState.Playing;
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

			while (State == MatchState.Playing)
			{
				if(NetworkManager.LocalTime.TimeAsFloat > TimeMatchEnds)
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

			while (State == MatchState.Playing)
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
			stateSync.Value = MatchState.Finished;
			UnsubscribeFromEvents();
		}

		private void UnsubscribeFromEvents()
		{
			Networking.Avatar.OnPlayerKilledPlayer -= OnPlayerKilledPlayer;
		}


	}
}
