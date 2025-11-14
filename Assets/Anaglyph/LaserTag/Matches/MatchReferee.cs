using System;
using System.Text.RegularExpressions;
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

		private static MatchState _state = MatchState.NotPlaying;
		public static MatchState State => _state;
		public static event Action<MatchState> StateChanged = delegate { };
		public static event Action MatchFinished = delegate { };
		
		private static readonly int[] teamScores = new int[Teams.NumTeams];
		public static int GetTeamScore(byte team) => teamScores[team];
		
		public static event Action<byte, int> TeamScored = delegate { };

		public static MatchSettings QueuedSettings { get; private set; } = MatchSettings.Lobby();
		private static MatchSettings currentSettings;

		private TaskCompletionSource<bool> winByScoreCompletion;
		private CancellationTokenSource cancelTokenSrc;
		
		public float TimeMatchEnds { get; private set; } = 0;

		private void Awake()
		{
			Instance = this;
		}
		
		private void Start()
		{
			NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			if(NetworkManager.Singleton)
				NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				SetStateEveryoneRpc(MatchState.NotPlaying);
			}
		}
		
		void OnNetcodeStateChanged(NetcodeState netcodeState)
		{
			switch (netcodeState)
			{	
				case NetcodeState.Disconnected:
					_ = SetStateLocally(MatchState.NotPlaying);
					ResetScoresLocally();
					break;
			}
		}

		private CancellationToken StopTaskAndPrepareNext()
		{
			cancelTokenSrc?.Cancel();

			cancelTokenSrc = new();
			return cancelTokenSrc.Token;
		}

		[Rpc(SendTo.Everyone)]
		private void SetStateEveryoneRpc(MatchState state, RpcParams rpcParams = default) 
			=> _ = SetStateLocally(state);

		private async Task SetStateLocally(MatchState state)
		{
			if (_state == state)
				return;
			
			_state = state;
			StateChanged.Invoke(state);
			var ctn = StopTaskAndPrepareNext();
			
			try
			{
				if(state != MatchState.Playing)
					currentSettings = MatchSettings.Lobby();
				
				switch (_state)
				{
					case MatchState.NotPlaying:
						break;

					case MatchState.Mustering:
						while (State == MatchState.Mustering)
						{
							int numPlayersInbase = 0;

							foreach (PlayerAvatar player in PlayerAvatar.All.Values)
							{
								if (player.IsInBase)
									numPlayersInbase++;
							}

							if (numPlayersInbase != 0 && numPlayersInbase == PlayerAvatar.All.Count)
							{
								if (IsOwner)
									SetStateEveryoneRpc(MatchState.Countdown);
							}

							await Awaitable.NextFrameAsync(ctn);
							ctn.ThrowIfCancellationRequested();
						}

						break;
					
					case MatchState.Countdown:
						await Awaitable.WaitForSecondsAsync(3, ctn);	
						ctn.ThrowIfCancellationRequested();
						
						if (IsOwner)
							SetStateEveryoneRpc(MatchState.Playing);

						break;
					
					case MatchState.Playing:
						ResetScoresLocally();
						currentSettings = QueuedSettings;
						TimeMatchEnds = Time.time + currentSettings.timerSeconds;
						
						Task timerTask = Task.CompletedTask;
						Task scoreTask = Task.CompletedTask;

						try
						{
							if (currentSettings.CheckWinByTimer())
							{
								timerTask = GameTimerTask(ctn);
							}

							if (currentSettings.CheckWinByScore())
							{
								winByScoreCompletion = new TaskCompletionSource<bool>();
								ctn.Register(() => winByScoreCompletion.TrySetCanceled(ctn));
								scoreTask = winByScoreCompletion.Task;
							}

							await Task.WhenAll(timerTask, scoreTask);

						}
						catch (OperationCanceledException) { }

						if (IsOwner)
						{
							SetStateEveryoneRpc(MatchState.NotPlaying);
							MatchFinishedRpc();
						}

						ctn.ThrowIfCancellationRequested();

						break;
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private async Task GameTimerTask(CancellationToken ctn)
		{
			while (State == MatchState.Playing)
			{
				float secondsLeft = TimeMatchEnds - Time.time;
				if (secondsLeft < 0)
					return;
				
				await Awaitable.NextFrameAsync(ctn);
				ctn.ThrowIfCancellationRequested();
			}
		}
		
		[Rpc(SendTo.Everyone)]
		public void TeamScoredRpc(byte team, int points)
		{
			if (team == 0 || points == 0) return;

			teamScores[team] += points;

			TeamScored(team, points);

			if (IsOwner)
			{
				bool canWinByScore = currentSettings.CheckWinByScore();
				bool isPlaying = State == MatchState.Playing;
				bool isWinningScore = GetTeamScore(team) >= currentSettings.scoreTarget;

				if (isPlaying && canWinByScore && isWinningScore)
				{
					winByScoreCompletion.SetResult(true);
				}
			}
		}
		
		private void OnClientConnected(ulong id)
		{
			if (IsOwner && id != OwnerClientId)
			{
				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				
				float timeLeft = TimeMatchEnds - Time.time;
				
				SyncNewPlayerRpc(State, QueuedSettings, teamScores, timeLeft, sendTo);
			}
		}

		[Rpc(SendTo.Everyone)]
		public void QueueMatchEveryoneRpc(MatchSettings settings)
		{
			ResetScoresLocally();
			QueuedSettings = settings;
			_ = SetStateLocally(MatchState.Mustering);
		}
		
		[Rpc(SendTo.Everyone)]
		public void EndMatchEveryoneRpc() 
			=> _ = SetStateLocally(MatchState.NotPlaying);

		[Rpc(SendTo.SpecifiedInParams)]
		private void SyncNewPlayerRpc(MatchState state, MatchSettings settings, int[] scores, float timeLeft, RpcParams rpcParams)
		{
			if (scores.Length != teamScores.Length)
				throw new ArgumentException($"Scores array must be of length {teamScores.Length}");

			// settings
			QueuedSettings = settings;

			_ = SetStateLocally(state);
			
			// override scores
			for (byte i = 0; i < teamScores.Length; i++)
			{
				teamScores[i] += scores[i];
				TeamScored.Invoke(i, scores[i]);
			}
			
			// override time
			TimeMatchEnds = Time.time + timeLeft;
		}

		[Rpc(SendTo.Everyone)]
		public void ResetScoresEveryoneRpc() => ResetScoresLocally();

		private void ResetScoresLocally()
		{
			PlayerAvatar.Local?.ResetScoreRpc();
			for (byte i = 0; i < Teams.NumTeams; i++)
			{
				teamScores[i] = 0;
				TeamScored.Invoke(i, 0);
			}
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

		[Rpc(SendTo.Everyone)]
		public void MatchFinishedRpc()
		{
			MatchFinished.Invoke();
		}
	}
}
