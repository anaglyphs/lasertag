using System;
using Unity.Netcode;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;

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

		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;

		public WinCondition winCondition;
		public int timerSeconds;
		public short scoreTarget;

		public readonly bool CheckWinByTimer() => winCondition.HasFlag(WinCondition.Timer);
		public readonly bool CheckWinByPoints() => winCondition.HasFlag(WinCondition.ReachScore);

		public static MatchSettings DemoGame()
		{
			return new()
			{
				teams = true,
				respawnInBases = true,
				respawnSeconds = 1,
				healthRegenPerSecond = 5,

				pointsPerKill = 1,
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

		private readonly NetworkVariable<MatchState> stateSync = new(MatchState.NotPlaying);
		public MatchState State => stateSync.Value;
		public event Action<MatchState> StateChanged = delegate { };
		public event Action MatchFinished = delegate { };

		private readonly NetworkVariable<int> team0ScoreSync = new(0);
		private readonly NetworkVariable<int> team1ScoreSync = new(0);
		private readonly NetworkVariable<int> team2ScoreSync = new(0);

		private NetworkVariable<int>[] teamScoresSync;
		public int GetTeamScore(byte team) => teamScoresSync[team].Value;
		public event Action<byte> TeamScored = delegate { };

		private readonly NetworkVariable<MatchSettings> settingsSync = new();
		public MatchSettings Settings => settingsSync.Value;

		private TaskCompletionSource<bool> winByScoreCompletion;
		private CancellationTokenSource cancelTokenSrc;

		private float localTimeMatchEnds = 0;

		private void Awake()
		{
			Instance = this;

			teamScoresSync = new NetworkVariable<int>[Teams.NumTeams];

			teamScoresSync[0] = team0ScoreSync;
			teamScoresSync[1] = team1ScoreSync;
			teamScoresSync[2] = team2ScoreSync;

			stateSync.OnValueChanged += OnStateChanged;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				stateSync.Value = MatchState.NotPlaying;
				settingsSync.Value = MatchSettings.Lobby();
			}

			OnStateChanged(MatchState.NotPlaying, MatchState.NotPlaying);
		}

		private void Update()
		{
			if (!IsSpawned) return;

			var avatar = MainPlayer.Instance.Avatar;
			if (avatar == null) return;

			if (State != MatchState.Playing || avatar.Team == 0)
			{
				if (avatar.IsInBase)
					avatar.TeamOwner.teamSync.Value = avatar.InBase.Team;
			}
		}

		private void OnStateChanged(MatchState prev, MatchState state)
		{
			MainPlayer.Instance.Respawn();

			switch (state)
			{
				case MatchState.Playing:
					localTimeMatchEnds = Time.time + Settings.timerSeconds;
					break;

				case MatchState.NotPlaying:
					if (IsOwner)
						settingsSync.Value = MatchSettings.Lobby();
					break;
			}

			StateChanged.Invoke(state);
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

		public override void OnDestroy()
		{
			base.OnDestroy();
			cancelTokenSrc?.Cancel();
		}

		public override void OnNetworkDespawn()
		{
			cancelTokenSrc?.Cancel();
			OnStateChanged(stateSync.Value, MatchState.NotPlaying);
		}

		private CancellationToken CancelTaskAndPrepareNext()
		{
			cancelTokenSrc?.Cancel();

			cancelTokenSrc = new();
			return cancelTokenSrc.Token;
		}

		[Rpc(SendTo.Owner)]
		public void StartMatchRpc(MatchSettings settings)
		{
			if (State != MatchState.NotPlaying)
				return;

			settingsSync.Value = settings;

			_ = Muster(CancelTaskAndPrepareNext());
		}

		private async Task Muster(CancellationToken ctn)
		{
			try
			{
				stateSync.Value = MatchState.Mustering;

				while (State == MatchState.Mustering)
				{
					int numPlayersInbase = 0;

					foreach (Networking.PlayerAvatar player in Networking.PlayerAvatar.All.Values)
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
					ResetScoresRpc();
				}

				Task timerTask = Task.CompletedTask;
				Task scoreTask = Task.CompletedTask;

				if (settings.CheckWinByTimer())
				{
					float secondsLeft = localTimeMatchEnds - Time.time;
					timerTask = Task.Delay(1000 * Mathf.RoundToInt(secondsLeft), ctn);
				}

				if (settings.CheckWinByPoints())
				{
					winByScoreCompletion = new TaskCompletionSource<bool>();
					ctn.Register(() => winByScoreCompletion.TrySetCanceled(ctn));
					scoreTask = winByScoreCompletion.Task;
				}

				await Task.WhenAll(timerTask, scoreTask);
			}
			catch (OperationCanceledException) { }

			stateSync.Value = MatchState.NotPlaying;
			MatchFinishedRpc();

			ctn.ThrowIfCancellationRequested();
		}

		[Rpc(SendTo.Owner)]
		public void ScoreTeamRpc(byte team, int points)
		{
			if (State != MatchState.Playing) return;
			if (team == 0 || points == 0) return;

			teamScoresSync[team].Value += points;
			TeamScored.Invoke(team);

			bool canWinByScore = Settings.CheckWinByPoints();
			bool isPlaying = State == MatchState.Playing;
			bool isWinningScore = GetTeamScore(team) > Settings.scoreTarget;

			if (isPlaying && canWinByScore && isWinningScore)
			{
				winByScoreCompletion.SetResult(true);
			}
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoresRpc()
		{
			for (byte i = 0; i < teamScoresSync.Length; i++)
			{
				teamScoresSync[i].Value = 0;
			}

			foreach (Networking.PlayerAvatar player in Networking.PlayerAvatar.All.Values)
			{
				player.ResetScoreRpc();
			}
		}

		public byte CalculateWinningTeam()
		{
			byte winningTeam = 0;
			int highScore = 0;
			for (byte i = 0; i < teamScoresSync.Length; i++)
			{
				int score = GetTeamScore(i);
				if (score > highScore)
				{
					highScore = score;
					winningTeam = i;
				}
			}
			return winningTeam;
		}

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