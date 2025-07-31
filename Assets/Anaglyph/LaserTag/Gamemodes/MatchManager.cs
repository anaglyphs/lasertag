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

	public class MatchManager : NetworkBehaviour
	{
		private const string NotOwnerExceptionMessage = "Only the NGO owner should call this!";

		public static MatchManager Instance { get; private set; }

		private NetworkVariable<MatchState> matchStateSync = new(MatchState.NotPlaying);
		public static MatchState MatchState => Instance.matchStateSync.Value;

		private NetworkVariable<float> timeMatchEndsSync = new(0);
		public static float TimeMatchEnds => Instance.timeMatchEndsSync.Value;

		private NetworkVariable<int> team0ScoreSync = new(0);
		private NetworkVariable<int> team1ScoreSync = new(0);
		private NetworkVariable<int> team2ScoreSync = new(0);

		private NetworkVariable<int>[] teamScoresSync;
		private NetworkVariable<byte> winningTeamSync = new();
		public static int GetTeamScore(byte team) => Instance.teamScoresSync[team].Value;
		public static byte WinningTeam => Instance.winningTeamSync.Value;

		private NetworkVariable<MatchSettings> matchSettingsSync = new();
		public static MatchSettings Settings => Instance.matchSettingsSync.Value;

		public static event Action<MatchState, MatchState> MatchStateChanged = delegate { };

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
				matchStateSync.Value = MatchState.NotPlaying;
				matchSettingsSync.Value = MatchSettings.Lobby();
			}
			
			matchStateSync.OnValueChanged += OnStateUpdateLocally;

			OnStateUpdateLocally(MatchState.NotPlaying, MatchState.NotPlaying);
		}

		private void Update()
		{
			if (!IsSpawned) return;

			var avatar = MainPlayer.Instance.Avatar;
			if (MatchState == MatchState.NotPlaying || MatchState == MatchState.Queued || avatar.Team == 0) {
				
				if (avatar.IsInBase)
					avatar.TeamOwner.teamSync.Value = avatar.InBase.Team;
			}
		}

		private void OnStateUpdateLocally(MatchState prev, MatchState state)
		{
			switch (state)
			{
				case MatchState.NotPlaying:

					MainPlayer.Instance.Respawn();
					if (IsOwner)
						matchSettingsSync.Value = MatchSettings.Lobby();

					break;

				case MatchState.Queued:

					break;

				case MatchState.Countdown:

					break;

				case MatchState.Playing:

					MainPlayer.Instance.Respawn();

					break;
			}

			MatchStateChanged.Invoke(prev, state);
		}

		public override void OnGainedOwnership()
		{
			if(MatchState == MatchState.Playing)
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
			OnStateUpdateLocally(matchStateSync.Value, MatchState.NotPlaying);
			UnsubscribeFromEvents();
		}

		[Rpc(SendTo.Owner)]
		public void QueueStartGameOwnerRpc(MatchSettings gameSettings)
		{
			matchSettingsSync.Value = gameSettings;
			matchStateSync.Value = MatchState.Queued;
			StartCoroutine(QueueStartGameAsOwnerCoroutine());
		}

		private IEnumerator QueueStartGameAsOwnerCoroutine()
		{
			OwnerCheck();

			ResetScoresRpc();

			yield return new WaitForSeconds(1);

			while (MatchState == MatchState.Queued)
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
						matchStateSync.Value = MatchState.Countdown;

				} else
					matchStateSync.Value = MatchState.Countdown;

				yield return null;
			}

			if(MatchState == MatchState.Countdown)
				yield return new WaitForSeconds(3);

			if(MatchState == MatchState.Countdown)
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

			matchStateSync.Value = MatchState.Playing;
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

			while (MatchState == MatchState.Playing)
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

			while (MatchState == MatchState.Playing)
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
			matchStateSync.Value = MatchState.NotPlaying;
			UnsubscribeFromEvents();
		}

		private void UnsubscribeFromEvents()
		{
			Networking.Avatar.OnPlayerKilledPlayer -= OnPlayerKilledPlayer;
		}


	}
}
