using System;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;
using Anaglyph.Lasertag.Networking;
using Anaglyph.Netcode;

namespace Anaglyph.Lasertag
{
	[Flags]
	public enum WinCondition : byte
	{
		None = 0b00000000,
		Timer = 0b00000001,
		ReachScore = 0b00000010,
	}

	[Flags]
	public enum MatchState : byte
	{
		NotPlaying = 0b00000001,
		Mustering = 0b00000010,
		Countdown = 0b00000100,
		Playing = 0b00001000,
	}

	[Serializable]
	public struct MatchSettings : INetworkSerializeByMemcpy
	{
		public bool teams;
		public bool respawnInBases;
		public float respawnSeconds;
		public float healthRegenPerSecond;
		public float damageMultiplier;

		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;
		public byte pointsPerFlagCapture;

		public WinCondition winCondition;
		public int timerSeconds;
		public short scoreTarget;

		public readonly bool CheckWinByTimer() => winCondition.HasFlag(WinCondition.Timer);
		public readonly bool CheckWinByScore() => winCondition.HasFlag(WinCondition.ReachScore);

		public static MatchSettings DemoGame()
		{
			return new()
			{
				teams = true,
				respawnInBases = true,
				respawnSeconds = 5,
				healthRegenPerSecond = 5,
				damageMultiplier = 1,

				pointsPerKill = 1,
				pointsPerSecondHoldingPoint = 1,
				pointsPerFlagCapture = 10,

				winCondition = WinCondition.Timer,
				timerSeconds = 60 * 2,
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
				damageMultiplier = 1,

				pointsPerKill = 0,
				pointsPerSecondHoldingPoint = 0,
				pointsPerFlagCapture = 0,

				winCondition = WinCondition.None,
				timerSeconds = 0,
			};
		}
	}

	public class MatchReferee : NetworkBehaviour
	{
		public static MatchReferee Instance { get; private set; }

		private readonly NetworkVariable<MatchState> stateSync = new();
		public static MatchState State { get; private set; } = MatchState.NotPlaying;
		public static event Action<MatchState> StateChanged = delegate { };
		public static event Action MatchFinished = delegate { };
		
		private static readonly int[] teamScores = new int[Teams.NumTeams];
		public static int GetTeamScore(byte team) => teamScores[team];
		
		public static event Action<byte, int> TeamScored = delegate { };

		private readonly NetworkVariable<MatchSettings> settingsSync = new();
		public static MatchSettings Settings { get; private set; } = MatchSettings.Lobby();

		private TaskCompletionSource<bool> winByScoreCompletion;
		private CancellationTokenSource cancelTokenSrc;
		
		public float TimeMatchEnds { get; private set; } = 0;

		private void Awake()
		{
			Instance = this;
			
			settingsSync.OnValueChanged += (_, settings) =>
			{
				SetSettingsLocally(settings);
			};
			
			stateSync.OnValueChanged += (_, state) =>
			{
				SetStateLocally(state);
			};
			
			SetSettingsLocally(MatchSettings.Lobby());
			SetStateLocally(MatchState.NotPlaying);
		}
		
		

		private void Start()
		{
			NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			if(NetworkManager.Singleton)
				NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
		}

		public override void OnNetworkSpawn()
		{
			SetSettingsLocally(settingsSync.Value);
			SetStateLocally(stateSync.Value);
		}
		
		public override void OnNetworkDespawn()
		{
			ResetScoresLocally();
			cancelTokenSrc?.Cancel();
			
			SetSettingsLocally(MatchSettings.Lobby());
			SetStateLocally(MatchState.NotPlaying);
		}
		
		private void SetStateLocally(MatchState state)
		{
			State = state;

			switch (state)
			{
				case MatchState.Mustering:
					ResetScoresLocally();
					break;
				
				case MatchState.Playing:
					
					break;

				case MatchState.NotPlaying:
					if (IsOwner)
						settingsSync.Value = MatchSettings.Lobby();
					break;
			}

			StateChanged.Invoke(state);
		}
		
		private void SetSettingsLocally(MatchSettings settings)
		{
			Settings = settings;
		}
		
		public override void OnGainedOwnership()
		{
			CancellationToken ctn = CancelTaskAndPrepareNext();

			switch (State)
			{
				case MatchState.Mustering:
					_ = Muster(ctn);
					break;

				case MatchState.Playing:
					_ = RunMatch(Settings, ctn);
					break;

				default:
					stateSync.Value = MatchState.NotPlaying;
					break;
			}
		}
		
		private void OnClientConnected(ulong id)
		{
			if (IsOwner && id != OwnerClientId)
			{
				float timeLeft = TimeMatchEnds - Time.time;
				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				SyncLateJoinerRpc(teamScores, timeLeft, sendTo);
			}
		}

		private CancellationToken CancelTaskAndPrepareNext()
		{
			cancelTokenSrc?.Cancel();

			cancelTokenSrc = new();
			return cancelTokenSrc.Token;
		}

		[Rpc(SendTo.Everyone)]
		public void StartMatchRpc(MatchSettings settings)
		{
			if (State != MatchState.NotPlaying)
				return;

			Settings = settings;

			_ = Muster(CancelTaskAndPrepareNext());
		}

		private async Task Muster(CancellationToken ctn)
		{
			try
			{
				State = MatchState.Mustering;

				while (State == MatchState.Mustering)
				{
					int numPlayersInbase = 0;

					foreach (PlayerAvatar player in PlayerAvatar.All.Values)
					{
						if (player.IsInBase)
							numPlayersInbase++;
					}

					if (numPlayersInbase != 0 && numPlayersInbase == Networking.PlayerAvatar.All.Count)
					{
						_ = Countdown(ctn);
						break;
					}

					await Awaitable.NextFrameAsync(ctn);
				}

			}
			catch (OperationCanceledException)
			{
				stateSync.Value = MatchState.NotPlaying;
				throw;
			}
		}

		private async Task Countdown(CancellationToken ctn)
		{
			try
			{
				stateSync.Value = MatchState.Countdown;
				await Awaitable.WaitForSecondsAsync(3, ctn);
				ctn.ThrowIfCancellationRequested();
				_ = RunMatch(Settings, ctn);

			}
			catch (OperationCanceledException)
			{
				stateSync.Value = MatchState.NotPlaying;
				throw;
			}
		}

		private async Task RunMatch(MatchSettings settings, CancellationToken ctn)
		{
			try
			{
				// check if starting a new match
				// this task could be run by a different client resuming a match
				// i.e. if the original match starter leaves
				bool startingNewMatch = stateSync.Value != MatchState.Playing;
				if (startingNewMatch)
				{
					settingsSync.Value = settings;
					stateSync.Value = MatchState.Playing;
				}

				Task timerTask = Task.CompletedTask;
				Task scoreTask = Task.CompletedTask;

				if (settings.CheckWinByTimer())
				{
					float secondsLeft = TimeMatchEnds - Time.time;
					timerTask = Task.Delay(1000 * Mathf.RoundToInt(secondsLeft), ctn);
				}

				if (settings.CheckWinByScore())
				{
					winByScoreCompletion = new TaskCompletionSource<bool>();
					ctn.Register(() => winByScoreCompletion.TrySetCanceled(ctn));
					scoreTask = winByScoreCompletion.Task;
				}

				await Task.WhenAll(timerTask, scoreTask);
				MatchFinishedRpc();
			}
			catch (OperationCanceledException) { }

			stateSync.Value = MatchState.NotPlaying;

			ctn.ThrowIfCancellationRequested();
		}

		[Rpc(SendTo.Everyone)]
		public void ScoreTeamRpc(byte team, int points)
		{
			if (team == 0 || points == 0) return;

			teamScores[team] += points;

			TeamScored(team, points);

			if (IsOwner)
			{
				bool canWinByScore = Settings.CheckWinByScore();
				bool isPlaying = State == MatchState.Playing;
				bool isWinningScore = GetTeamScore(team) >= Settings.scoreTarget;

				if (isPlaying && canWinByScore && isWinningScore)
				{
					winByScoreCompletion.SetResult(true);
				}
			}
		}

		[Rpc(SendTo.SpecifiedInParams)]
		public void SyncLateJoinerRpc(int[] scores, float timeLeft, RpcParams rpcParams)
		{
			if (scores.Length != teamScores.Length)
				return;
			
			TimeMatchEnds = Time.time + timeLeft;

			for (byte i = 0; i < teamScores.Length; i++)
			{
				teamScores[i] += scores[i];
				TeamScored.Invoke(i, scores[i]);
			}
		}

		[Rpc(SendTo.Everyone)]
		public void ResetScoresRpc()
		{
			PlayerAvatar.Local.ResetScoreRpc();
			ResetScoresLocally();
		}

		private void ResetScoresLocally()
		{
			for (int i = 0; i < Teams.NumTeams; i++)
				teamScores[i] = 0;
		}

		// public byte CalculateWinningTeam()
		// {
		// 	byte winningTeam = 0;
		// 	int highScore = 0;
		// 	for (byte i = 0; i < teamScoresSync.Length; i++)
		// 	{
		// 		int score = GetTeamScore(i);
		// 		if (score > highScore)
		// 		{
		// 			highScore = score;
		// 			winningTeam = i;
		// 		}
		// 	}
		// 	return winningTeam;
		// }

		public float GetTimeLeft() => Mathf.Max(TimeMatchEnds - Time.time, 0);

		[Rpc(SendTo.Owner)]
		public void EndMatchRpc()
		{
			cancelTokenSrc?.Cancel();
		}

		[Rpc(SendTo.Everyone)]
		public void MatchFinishedRpc()
		{
			MatchFinished.Invoke();
		}
	}
}
