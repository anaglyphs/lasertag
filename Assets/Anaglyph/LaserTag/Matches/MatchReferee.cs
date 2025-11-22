using System;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.Lasertag.Networking;
using Anaglyph.Netcode;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[Flags]
	public enum WinCondition : byte
	{
		None = 0b00000000,
		Timer = 0b00000001,
		ReachScore = 0b00000010
	}

	[Flags]
	public enum MatchState : byte
	{
		NotPlaying = 0b00000001,
		Mustering = 0b00000010,
		Countdown = 0b00000100,
		Playing = 0b00001000
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

		public readonly bool CheckWinByTimer()
		{
			return winCondition.HasFlag(WinCondition.Timer);
		}

		public readonly bool CheckWinByScore()
		{
			return winCondition.HasFlag(WinCondition.ReachScore);
		}

		public static MatchSettings DemoGame()
		{
			return new MatchSettings
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
				timerSeconds = 60 * 2
			};
		}

		public static MatchSettings Lobby()
		{
			return new MatchSettings
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
				timerSeconds = 0
			};
		}
	}

	public class MatchReferee : NetworkBehaviour
	{
		public static MatchReferee Instance { get; private set; }
		private static readonly int[] _teamScores = new int[Teams.NumTeams];

		public static int GetTeamScore(byte team)
		{
			return _teamScores[team];
		}

		private CancellationTokenSource cancelSrc;

		private static MatchState _state = MatchState.NotPlaying;
		public static MatchState State => _state;
		public static event Action<MatchState> StateChanged = delegate { };
		public static event Action MatchFinished = delegate { };
		public static event Action<string> TimerTextChanged = delegate { };

		private static MatchSettings _settings = MatchSettings.Lobby();
		public static MatchSettings Settings => _settings;
		public float TimeMatchEnds { get; private set; }

		private void Awake()
		{
			Instance = this;
			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnNetcodeStateChanged(NetcodeState netcodeState)
		{
			switch (netcodeState)
			{
				case NetcodeState.Disconnected:
					_ = SetStateLocally(MatchState.NotPlaying);
					ResetScoresLocally();
					break;
			}
		}

		public static event Action<byte, int> TeamScored = delegate { };

		public override void OnNetworkSpawn()
		{
			NetworkManager.OnClientConnectedCallback += OnClientConnected;

			if (IsOwner)
			{
				SyncStateRpc(MatchState.NotPlaying);
				SyncSettingsRpc(MatchSettings.Lobby());
			}
		}

		public override void OnNetworkDespawn()
		{
			NetworkManager.OnClientConnectedCallback -= OnClientConnected;
		}

		private void OnClientConnected(ulong id)
		{
			if (IsOwner && id != OwnerClientId)
			{
				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				SyncSettingsRpc(Settings, sendTo);
				SyncStateRpc(State, sendTo);

				if (State == MatchState.Playing)
				{
					var timeLeft = TimeMatchEnds - Time.time;
					SyncOngoingMatchRpc(_teamScores, timeLeft, sendTo);
				}
			}
		}

		[Rpc(SendTo.Everyone, AllowTargetOverride = true)]
		private void SyncSettingsRpc(MatchSettings settings, RpcParams rpcParams = default)
		{
			_settings = settings;
		}

		[Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Owner)]
		private void SyncOngoingMatchRpc(int[] scores, float timeLeft, RpcParams rpcParams)
		{
			if (scores.Length != _teamScores.Length)
				throw new ArgumentException($"Scores array must be of length {_teamScores.Length}");

			// override scores
			for (byte i = 0; i < _teamScores.Length; i++)
			{
				_teamScores[i] += scores[i];
				TeamScored.Invoke(i, scores[i]);
			}

			// override time
			TimeMatchEnds = Time.time + timeLeft;
		}

		[Rpc(SendTo.Everyone)]
		public void QueueMatchRpc(MatchSettings settings)
		{
			ResetScoresLocally();
			_settings = settings;
			_ = SetStateLocally(MatchState.Mustering);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner, AllowTargetOverride = true)]
		private void SyncStateRpc(MatchState state, RpcParams rpcParams = default)
		{
			_ = SetStateLocally(state);
		}

		private async Task SetStateLocally(MatchState state)
		{
			if (_state == state)
				return;

			_state = state;

			StateChanged.Invoke(State);

			cancelSrc?.Cancel();
			cancelSrc = new CancellationTokenSource();
			var ctn = cancelSrc.Token;

			try
			{
				switch (_state)
				{
					case MatchState.NotPlaying:
						_settings = MatchSettings.Lobby();
						break;

					case MatchState.Mustering:

						UpdateTimerText(Settings.timerSeconds);
						while (State == MatchState.Mustering)
						{
							var numPlayersInBase = 0;

							foreach (var player in PlayerAvatar.All.Values)
								if (player.IsInBase)
									numPlayersInBase++;

							if (numPlayersInBase != 0 && numPlayersInBase == PlayerAvatar.All.Count)
								if (IsOwner)
									SyncStateRpc(MatchState.Countdown);

							await Awaitable.NextFrameAsync(ctn);
							ctn.ThrowIfCancellationRequested();
						}

						break;

					case MatchState.Countdown:
						await Awaitable.WaitForSecondsAsync(3, ctn);
						ctn.ThrowIfCancellationRequested();

						if (IsOwner)
							SyncStateRpc(MatchState.Playing);

						break;

					case MatchState.Playing:
						ResetScoresLocally();
						TimeMatchEnds = Time.time + Settings.timerSeconds;

						if (Settings.CheckWinByTimer())
							while (State == MatchState.Playing)
							{
								await Awaitable.WaitForSecondsAsync(1, ctn);
								
								var timeLeft = GetTimeLeft();
								UpdateTimerText(timeLeft);

								if (timeLeft <= 0)
								{
									if (IsOwner)
										FinishMatchEveryoneRpc();

									break;
								}
								
								ctn.ThrowIfCancellationRequested();
							}

						break;
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		[Rpc(SendTo.Everyone)]
		public void ScoreRpc(byte team, int points)
		{
			if (team == 0 || points == 0) return;

			_teamScores[team] += points;

			TeamScored(team, points);

			if (IsOwner && State == MatchState.Playing)
			{
				var canWinByScore = Settings.CheckWinByScore();
				var isWinningScore = GetTeamScore(team) >= Settings.scoreTarget;

				if (canWinByScore && isWinningScore) FinishMatchEveryoneRpc();
			}
		}

		[Rpc(SendTo.Everyone)]
		public void EndMatchRpc()
		{
			_ = SetStateLocally(MatchState.NotPlaying);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void FinishMatchEveryoneRpc()
		{
			MatchFinished.Invoke();
			_ = SetStateLocally(MatchState.NotPlaying);
		}

		private void ResetScoresLocally()
		{
			PlayerAvatar.Local?.ResetScoreRpc();
			for (byte i = 0; i < Teams.NumTeams; i++)
			{
				_teamScores[i] = 0;
				TeamScored.Invoke(i, 0);
			}
		}

		private void UpdateTimerText(float seconds)
		{
			int rounded = Mathf.RoundToInt(seconds);
			TimeSpan span = TimeSpan.FromSeconds(rounded);
			TimerTextChanged.Invoke(span.ToString(@"m\:ss"));
		}

		public float GetTimeLeft()
		{
			float seconds = Settings.timerSeconds;

			if (State == MatchState.Playing)
				seconds = TimeMatchEnds - Time.time;
			
			return seconds;
		}
	}
}