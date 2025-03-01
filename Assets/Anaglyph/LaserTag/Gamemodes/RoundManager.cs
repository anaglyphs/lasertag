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

		public static RoundSettings Default()
		{
			return new()
			{
				teams = true,
				respawnInBases = true,

				pointsPerKill = 1,
				pointsPerSecondHoldingPoint = 1,

				winCondition = WinCondition.Timer,
				timerSeconds = 60 * 5,
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

		private NetworkVariable<RoundSettings> activeSettingsSync = new();
		public static RoundSettings ActiveSettings => Instance.activeSettingsSync.Value;

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
				roundStateSync.Value = RoundState.NotPlaying;
			
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
					MainPlayer.Instance.currentRole.ReturnToBaseOnDie = false;

					break;

				case RoundState.Queued:

					break;

				case RoundState.Countdown:

					break;

				case RoundState.Playing:

					MainPlayer.Instance.Respawn();
					MainPlayer.Instance.currentRole.ReturnToBaseOnDie = ActiveSettings.respawnInBases;

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
			activeSettingsSync.Value = gameSettings;
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
				if (ActiveSettings.respawnInBases)
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

			if (ActiveSettings.CheckWinByTimer())
				timeRoundEndsSync.Value = (float)NetworkManager.LocalTime.Time + ActiveSettings.timerSeconds;

			roundStateSync.Value = RoundState.Playing;
			SubscribeToEvents();
		}

		private void SubscribeToEvents()
		{
			OwnerCheck();

			if (ActiveSettings.CheckWinByTimer())
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

			if (ActiveSettings.teams)
			{
				ScoreTeamRpc(killer.Team, ActiveSettings.pointsPerKill);
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
						ScoreTeamRpc(point.HoldingTeam, ActiveSettings.pointsPerSecondHoldingPoint);
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
		}

		private void UnsubscribeFromEvents()
		{
			Networking.Avatar.OnPlayerKilledPlayer -= OnPlayerKilledPlayer;
		}


	}
}
