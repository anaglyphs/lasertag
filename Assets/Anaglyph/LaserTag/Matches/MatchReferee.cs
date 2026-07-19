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

	public enum MatchState : byte
	{
		NotPlaying,
		Mustering,
		Countdown,
		Playing
	}

	[Serializable]
	public struct MatchSettings
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
				timerSeconds = 60 * 2,
				scoreTarget = 10
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

	/// <summary>
	/// Manages 'matches' i.e. games and game mode settings
	/// Singleton class
	/// </summary>
	public class MatchReferee : MonoBehaviour
	{
		public static MatchReferee Instance { get; private set; }

		public const float CountdownSeconds = 3;


		private struct ScoreMsg
		{
			public byte team;
			public int points;
		}

		// ALWAYS change state LAST after other match related variables.
		// You wouldn't want e.g. scores or timer not reset before a match begins!
		private static readonly SyncVariable<MatchState> stateSync = new("match.state");

		private static readonly SyncVariable<MatchSettings> settingsSync =
			new("match.settings", MatchSettings.Lobby());

		private static readonly SyncVariable<float> startTimeSync = new("match.startTime");
		private static readonly SyncList<int> teamScoresSync = new("match.scores", new int[Teams.NumTeams]);
		private static readonly SyncEvent<ScoreMsg> scoreEvent = new("match.score", EventRoute.ToAuthority);

		public static MatchState State => stateSync.Value;
		public static MatchSettings Settings => settingsSync.Value;
		public static float TimeMatchStarted => startTimeSync.Value;
		private float ServerTime => NetworkManager.Singleton.ServerTime.TimeAsFloat;

		public static int GetTeamScore(byte team)
		{
			return team < teamScoresSync.Count ? teamScoresSync[team] : 0;
		}

		public static event Action<MatchState> StateChanged = delegate { };
		public static event Action MatchFinished = delegate { };
		public static event Action<string> TimerTextChanged = delegate { };
		public static event Action<byte, int> TeamScored = delegate { };

		private readonly int[] lastScores = new int[Teams.NumTeams];
		private CancellationTokenSource cancelSrc;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			StateChanged = delegate { };
			MatchFinished = delegate { };
			TimerTextChanged = delegate { };
			TeamScored = delegate { };
			// endpoint values reset when the instance re-registers them in Awake
		}

		private void Awake()
		{
			Instance = this;

			settingsSync.Register();
			startTimeSync.Register();
			teamScoresSync.Register();
			stateSync.Register();
			scoreEvent.Register();

			stateSync.Changed += OnStateChanged;
			teamScoresSync.Changed += OnScoresChanged;
			scoreEvent.Received += OnScoreSubmitted;
		}

		private void OnDestroy()
		{
			stateSync.Changed -= OnStateChanged;
			teamScoresSync.Changed -= OnScoresChanged;
			scoreEvent.Received -= OnScoreSubmitted;

			scoreEvent.Unregister();
			stateSync.Unregister();
			teamScoresSync.Unregister();
			startTimeSync.Unregister();
			settingsSync.Unregister();

			cancelSrc?.Cancel();
		}

		// ---- any-peer commands -------------------------------------------------

		// Settings land before the state flips everywhere: both requests ride the
		// same sequenced channel through the authority.
		public void QueueMatch(MatchSettings settings)
		{
			settingsSync.Request(settings);
			stateSync.Request(MatchState.Mustering);
		}

		public void EndMatch()
		{
			stateSync.Request(MatchState.NotPlaying);
		}

		// Called by whoever detects the scoring (kill, point held, flag capture);
		// only the authority receives it and accumulates into the replicated list.
		public void Score(byte team, int points)
		{
			if (team == 0 || points == 0) return;
			scoreEvent.Raise(new ScoreMsg { team = team, points = points });
		}

		// ---- authority reactions -------------------------------------------------

		private void OnScoreSubmitted(ulong sender, ScoreMsg msg)
		{
			if (msg.team == 0 || msg.team >= Teams.NumTeams || msg.points == 0) return;

			teamScoresSync.Set(msg.team, GetTeamScore(msg.team) + msg.points);

			if (State == MatchState.Playing)
			{
				bool canWinByScore = Settings.CheckWinByScore();
				bool isWinningScore = GetTeamScore(msg.team) >= Settings.scoreTarget;

				if (canWinByScore && isWinningScore)
					stateSync.Value = MatchState.NotPlaying;
			}
		}

		private void ResetScores()
		{
			if (!SyncBus.IsAuthority) return;

			for (byte i = 0; i < Teams.NumTeams; i++)
				if (GetTeamScore(i) != 0)
					teamScoresSync.Set(i, 0);
		}

		// ---- every-peer reactions ------------------------------------------------

		private void OnScoresChanged(SyncList<int>.EventData eventData)
		{
			for (byte i = 0; i < Teams.NumTeams; i++)
			{
				int score = GetTeamScore(i);
				if (score == lastScores[i]) continue;

				int delta = score - lastScores[i];
				lastScores[i] = score;
				TeamScored.Invoke(i, delta);
			}
		}

		private async void OnStateChanged(MatchState old, MatchState state)
		{
			if (old == MatchState.Playing && state == MatchState.NotPlaying)
				MatchFinished.Invoke();

			StateChanged.Invoke(state);

			await RunStatePhase(state);
		}

		// The per-state loops run on every peer (timer text is local UI); only the
		// authority advances the replicated state.
		private async Task RunStatePhase(MatchState state)
		{
			cancelSrc?.Cancel();
			cancelSrc = new CancellationTokenSource();
			CancellationToken ctn = cancelSrc.Token;

			try
			{
				switch (state)
				{
					case MatchState.NotPlaying:
						if (SyncBus.IsAuthority)
							settingsSync.Value = MatchSettings.Lobby();
						break;

					case MatchState.Mustering:
						ResetScores();
						PlayerAvatar.Local?.ResetScoreLocally();

						UpdateTimerText(Settings.timerSeconds);
						while (State == MatchState.Mustering)
						{
							int numPlayersInBase = 0;

							foreach (PlayerAvatar player in PlayerAvatar.All.Values)
								if (player.IsInBase)
									numPlayersInBase++;

							if (numPlayersInBase != 0 && numPlayersInBase == PlayerAvatar.All.Count)
								if (SyncBus.IsAuthority)
								{
									ResetScores();
									startTimeSync.Value = ServerTime + CountdownSeconds;
									stateSync.Value = MatchState.Countdown;
								}

							await Awaitable.NextFrameAsync(ctn);
							ctn.ThrowIfCancellationRequested();
						}

						break;

					case MatchState.Countdown:
						await Awaitable.WaitForSecondsAsync(CountdownSeconds, ctn);

						if (SyncBus.IsAuthority)
						{
							stateSync.Value = MatchState.Playing;
						}

						break;

					case MatchState.Playing:
						PlayerAvatar.Local?.ResetScoreLocally();

						if (Settings.CheckWinByTimer())
							while (State == MatchState.Playing)
							{
								await Awaitable.WaitForSecondsAsync(1, ctn);

								float timeLeft = GetTimeLeft();
								UpdateTimerText(timeLeft);

								if (timeLeft <= 0)
								{
									if (SyncBus.IsAuthority)
										stateSync.Value = MatchState.NotPlaying;

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

		private void UpdateTimerText(float seconds)
		{
			int rounded = Mathf.RoundToInt(seconds);
			TimeSpan span = TimeSpan.FromSeconds(rounded);
			TimerTextChanged.Invoke(span.ToString(@"m\:ss"));
		}

		public float GetTimeElapsed()
		{
			return ServerTime - TimeMatchStarted;
		}

		public float GetTimeLeft()
		{
			float timeLeft;

			switch (State)
			{
				case MatchState.NotPlaying:
					timeLeft = 0;
					break;
				case MatchState.Playing:
					float endTime = TimeMatchStarted + Settings.timerSeconds;
					timeLeft = Mathf.Max(0, endTime - ServerTime);
					break;
				default:
					timeLeft = Settings.timerSeconds;
					break;
			}

			return timeLeft;
		}
	}
}