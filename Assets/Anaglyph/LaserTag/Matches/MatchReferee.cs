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
		
		private static readonly int[] _teamScores = new int[Teams.NumTeams];
		public static int GetTeamScore(byte team) => _teamScores[team];
		public static event Action<byte, int> TeamScored = delegate { };

		public static MatchSettings Settings { get; private set; } = MatchSettings.Lobby();
		
		private CancellationTokenSource cancelSrc;
		
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
		
		private void OnClientConnected(ulong id)
		{
			if (IsOwner && id != OwnerClientId)
			{
				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				SetMatchSettingsEveryoneRpc(Settings, sendTo);
				SetStateEveryoneRpc(State, sendTo);

				if (State == MatchState.Playing)
				{
					float timeLeft = TimeMatchEnds - Time.time;
					SyncOngoingMatchRpc(_teamScores, timeLeft, sendTo);
				}
			}
		}

		[Rpc(SendTo.Everyone, AllowTargetOverride = true)]
		private void SetMatchSettingsEveryoneRpc(MatchSettings settings, RpcParams rpcParams)
		{
			Settings = settings;
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
		public void QueueMatchEveryoneRpc(MatchSettings settings)
		{
			ResetScoresLocally();
			Settings = settings;
			_ = SetStateLocally(MatchState.Mustering);
		}
		
		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner, AllowTargetOverride = true)]
		private void SetStateEveryoneRpc(MatchState state, RpcParams rpcParams = default) 
			=> _ = SetStateLocally(state);

		private async Task SetStateLocally(MatchState state)
		{
			if (_state == state)
				return;
			
			_state = state;
			
			StateChanged.Invoke(_state);
			
			cancelSrc?.Cancel();
			cancelSrc = new();
			var ctn = cancelSrc.Token;
			
			try
			{
				switch (_state)
				{
					case MatchState.NotPlaying:
						Settings = MatchSettings.Lobby();
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
						TimeMatchEnds = Time.time + Settings.timerSeconds;
						
						while (State == MatchState.Playing)
						{
							float secondsLeft = TimeMatchEnds - Time.time;
							if (secondsLeft < 0)
							{
								if(IsOwner)
									FinishMatchEveryoneRpc();
								
								break;
							}

							await Awaitable.NextFrameAsync(ctn);
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
		public void TeamScoredRpc(byte team, int points)
		{
			if (team == 0 || points == 0) return;

			_teamScores[team] += points;

			TeamScored(team, points);

			if (IsOwner && State == MatchState.Playing)
			{
				bool canWinByScore = Settings.CheckWinByScore();
				bool isWinningScore = GetTeamScore(team) >= Settings.scoreTarget;

				if (canWinByScore && isWinningScore)
				{
					FinishMatchEveryoneRpc();
				}
			}
		}
		
		[Rpc(SendTo.Everyone)]
		public void EndMatchEveryoneRpc() 
			=> _ = SetStateLocally(MatchState.NotPlaying);

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
		
		public float GetTimeLeft() => Mathf.Max(TimeMatchEnds - Time.time, 0);
	}
}
