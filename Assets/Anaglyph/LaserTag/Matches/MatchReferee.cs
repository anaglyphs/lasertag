using System;
using System.Threading;
using System.Threading.Tasks;
using Anaglyph.DepthKit.EnvScanning;
using Anaglyph.LaserTag.NPCs;
using Anaglyph.Lasertag.Networking;
using Anaglyph.Netcode;
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

	public enum RespawnCondition : byte
	{
		Timer,     // wait out respawnSeconds, then respawn anywhere
		InBases,   // wait out respawnSeconds, then respawn in a friendly base
		NextRound  // stay dead until the round ends
	}

	public enum MatchState : byte
	{
		NotPlaying,
		Mustering,
		Countdown,
		Playing,
		RoundBreak
	}

	[Serializable]
	public struct MatchSettings
	{
		public const float MaxHealth = 100;

		public bool teams;
		public bool spawnZombies;
		public RespawnCondition respawnCondition;
		public float respawnSeconds;
		public float healthRegenPerSecond;
		public float damageMultiplier;

		public byte pointsPerKill;
		public byte pointsPerSecondHoldingPoint;
		public byte pointsPerFlagCapture;

		public WinCondition winCondition;
		public int roundTimeSeconds;
		public short scoreTarget;
		public byte numRounds;

		/// <summary>A zero multiplier means the settings were never filled in, not "no damage".</summary>
		public readonly float ApplyDamageMultiplier(float damage)
		{
			return damage * (damageMultiplier == 0 ? 1 : damageMultiplier);
		}

		public readonly bool CheckWinByTimer()
		{
			return winCondition.HasFlag(WinCondition.Timer);
		}

		public readonly bool CheckWinByScore()
		{
			return winCondition.HasFlag(WinCondition.ReachScore);
		}

		public readonly int GetNumRounds()
		{
			return Mathf.Max(1, numRounds);
		}

		public static MatchSettings DemoGame()
		{
			return new MatchSettings
			{
				teams = true,
				spawnZombies = false,
				respawnCondition = RespawnCondition.InBases,
				respawnSeconds = 5,
				healthRegenPerSecond = 5,
				damageMultiplier = 1,

				pointsPerKill = 1,
				pointsPerSecondHoldingPoint = 1,
				pointsPerFlagCapture = 10,

				winCondition = WinCondition.Timer,
				roundTimeSeconds = 60 * 2,
				scoreTarget = 10,
				numRounds = 1
			};
		}

		public static MatchSettings Lobby()
		{
			return new MatchSettings
			{
				teams = false,
				spawnZombies = false,
				respawnCondition = RespawnCondition.Timer,
				respawnSeconds = 5,
				healthRegenPerSecond = 5,
				damageMultiplier = 1,

				pointsPerKill = 0,
				pointsPerSecondHoldingPoint = 0,
				pointsPerFlagCapture = 0,

				winCondition = WinCondition.None,
				roundTimeSeconds = 0,
				numRounds = 1
			};
		}
	}

	/// <summary>
	/// Manages 'matches' i.e. games and game mode settings.
	/// A match is one or more rounds; scores reset every round while
	/// round wins accumulate for the whole match.
	/// Singleton class
	/// </summary>
	public class MatchReferee : MonoBehaviour
	{
		public static MatchReferee Instance { get; private set; }

		public const float CountdownSeconds = 3;
		public const float RoundBreakSeconds = 5;


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
		private static readonly SyncVariable<int> roundsPlayedSync = new("match.roundsPlayed");
		private static readonly SyncList<int> teamScoresSync = new("match.scores", new int[Teams.NumTeams]);
		private static readonly SyncList<int> roundWinsSync = new("match.roundWins", new int[Teams.NumTeams]);
		private static readonly SyncEvent<ScoreMsg> scoreEvent = new("match.score", EventRoute.ToAuthority);

		public static MatchState State => stateSync.Value;
		public static MatchSettings Settings => settingsSync.Value;
		public static float TimeRoundStarted => startTimeSync.Value;
		public static int RoundsPlayed => roundsPlayedSync.Value;

		// 1-based round number for display; clamped so the final round reads
		// e.g. "3/3" while its results show, not "4/3"
		public static int CurrentRound => Mathf.Min(RoundsPlayed + 1, Settings.GetNumRounds());

		private float ServerTime => SharedNetworkTime.TimeAsFloat;

		public static int GetTeamScore(byte team)
		{
			return team < teamScoresSync.Count ? teamScoresSync[team] : 0;
		}

		public static int GetTeamRoundWins(byte team)
		{
			return team < roundWinsSync.Count ? roundWinsSync[team] : 0;
		}

		public static event Action<MatchState> StateChanged = delegate { };
		public static event Action MatchFinished = delegate { };
		public static event Action RoundFinished = delegate { };
		public static event Action<string> TimerTextChanged = delegate { };
		public static event Action<byte, int> TeamScored = delegate { };
		public static event Action<byte, int> TeamWonRound = delegate { };

		private readonly int[] lastScores = new int[Teams.NumTeams];
		private readonly int[] lastRoundWins = new int[Teams.NumTeams];
		private int lastTimerSeconds = int.MinValue;
		private CancellationTokenSource cancelSrc;
		private MonsterSpawner[] monsterSpawners;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			StateChanged = delegate { };
			MatchFinished = delegate { };
			RoundFinished = delegate { };
			TimerTextChanged = delegate { };
			TeamScored = delegate { };
			TeamWonRound = delegate { };
			// endpoint values reset when the instance re-registers them in Awake
		}

		private void Awake()
		{
			Instance = this;

			monsterSpawners = GetComponents<MonsterSpawner>();

			settingsSync.Register();
			startTimeSync.Register();
			roundsPlayedSync.Register();
			teamScoresSync.Register();
			roundWinsSync.Register();
			stateSync.Register();
			scoreEvent.Register();

			stateSync.Changed += OnStateChanged;
			settingsSync.Changed += OnSettingsChanged;
			teamScoresSync.Changed += OnScoresChanged;
			roundWinsSync.Changed += OnRoundWinsChanged;
			scoreEvent.Received += OnScoreSubmitted;
			SyncBus.AuthorityChanged += OnAuthorityChanged;

			ApplyZombieSpawning();
		}

		private void OnDestroy()
		{
			stateSync.Changed -= OnStateChanged;
			settingsSync.Changed -= OnSettingsChanged;
			teamScoresSync.Changed -= OnScoresChanged;
			roundWinsSync.Changed -= OnRoundWinsChanged;
			scoreEvent.Received -= OnScoreSubmitted;
			SyncBus.AuthorityChanged -= OnAuthorityChanged;

			scoreEvent.Unregister();
			stateSync.Unregister();
			roundWinsSync.Unregister();
			teamScoresSync.Unregister();
			roundsPlayedSync.Unregister();
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
					EndRound(msg.team);
			}
		}

		// Close out the current round, then either muster the next one or finish
		// the match. Winner 0 means a draw: nobody gets the round.
		private void EndRound(byte winningTeam)
		{
			if (!SyncBus.IsAuthority || State != MatchState.Playing) return;

			if (winningTeam != 0 && winningTeam < Teams.NumTeams)
				roundWinsSync.Set(winningTeam, GetTeamRoundWins(winningTeam) + 1);

			roundsPlayedSync.Value = RoundsPlayed + 1;

			// state changes LAST, as always
			if (RoundsPlayed >= Settings.GetNumRounds())
				stateSync.Value = MatchState.NotPlaying;
			else
				stateSync.Value = MatchState.RoundBreak;
		}

		// Everyone has to be in a base to start, and to stay there for the whole
		// countdown.
		private static bool AllPlayersInBase()
		{
			if (PlayerAvatar.All.Count == 0) return false;

			foreach (PlayerAvatar player in PlayerAvatar.All.Values)
				if (!player.IsInBase)
					return false;

			return true;
		}

		// A round is decided by elimination once every living player is on one
		// team and somebody from another team is dead. Everybody dead is a draw.
		private static bool TryGetEliminationWinner(out byte winner)
		{
			winner = 0;

			if (PlayerAvatar.All.Count == 0) return false;

			bool anyAlive = false;
			byte aliveTeam = 0;

			foreach (PlayerAvatar player in PlayerAvatar.All.Values)
			{
				if (!player.IsAlive) continue;

				if (!anyAlive)
				{
					anyAlive = true;
					aliveTeam = player.Team;
				}
				else if (player.Team != aliveTeam)
				{
					return false; // two or more teams still standing
				}
			}

			if (!anyAlive) return true; // mutual elimination: draw

			foreach (PlayerAvatar player in PlayerAvatar.All.Values)
				if (player.Team != aliveTeam)
				{
					winner = aliveTeam;
					return true;
				}

			return false; // only one team in the match; nobody to eliminate
		}

		// Timer-expiry rounds go to the leading team; ties are a draw.
		private static byte GetRoundLeader()
		{
			byte leader = 0;
			int best = 0;

			for (byte team = 1; team < Teams.NumTeams; team++)
			{
				int score = GetTeamScore(team);

				if (score > best)
				{
					best = score;
					leader = team;
				}
				else if (score == best)
				{
					leader = 0;
				}
			}

			return leader;
		}

		private void ResetScores()
		{
			if (!SyncBus.IsAuthority) return;

			for (byte i = 0; i < Teams.NumTeams; i++)
				if (GetTeamScore(i) != 0)
					teamScoresSync.Set(i, 0);
		}

		private void ResetRoundWins()
		{
			if (!SyncBus.IsAuthority) return;

			for (byte i = 0; i < Teams.NumTeams; i++)
				if (GetTeamRoundWins(i) != 0)
					roundWinsSync.Set(i, 0);
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

		private void OnRoundWinsChanged(SyncList<int>.EventData eventData)
		{
			for (byte i = 0; i < Teams.NumTeams; i++)
			{
				int wins = GetTeamRoundWins(i);
				if (wins == lastRoundWins[i]) continue;

				lastRoundWins[i] = wins;
				TeamWonRound.Invoke(i, wins);
			}
		}

		private void OnSettingsChanged(MatchSettings old, MatchSettings settings)
		{
			ApplyZombieSpawning();
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			ApplyZombieSpawning();
		}

		// Zombies path around the scanned room, so the navmesh has to be built wherever
		// one might be simulated. Only the authority spawns them, so the session gets a
		// single stream of monsters rather than one per peer.
		private void ApplyZombieSpawning()
		{
			bool zombiesInMatch = Settings.spawnZombies && State != MatchState.NotPlaying;

			if (EnvNavMesher.Instance != null)
				EnvNavMesher.Instance.enabled = zombiesInMatch;

			bool spawning = zombiesInMatch && SyncBus.IsAuthority && State == MatchState.Playing;

			foreach (MonsterSpawner spawner in monsterSpawners)
				spawner.enabled = spawning;
		}

		private async void OnStateChanged(MatchState old, MatchState state)
		{
			// a fresh match begins: round history belongs to the previous one
			if (SyncBus.IsAuthority && old == MatchState.NotPlaying && state == MatchState.Mustering)
			{
				roundsPlayedSync.Value = 0;
				ResetRoundWins();
			}

			if (old == MatchState.Playing)
			{
				if (state == MatchState.NotPlaying)
					MatchFinished.Invoke();
				else
					RoundFinished.Invoke();
			}

			ApplyZombieSpawning();

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

						UpdateTimerText(Settings.roundTimeSeconds);
						while (State == MatchState.Mustering)
						{
							if (SyncBus.IsAuthority && AllPlayersInBase())
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
						// startTime is when play begins, i.e. the countdown deadline
						while (State == MatchState.Countdown)
						{
							if (SyncBus.IsAuthority)
							{
								if (!AllPlayersInBase())
								{
									stateSync.Value = MatchState.Mustering;
									break;
								}

								if (ServerTime >= TimeRoundStarted)
								{
									stateSync.Value = MatchState.Playing;
									break;
								}
							}

							await Awaitable.NextFrameAsync(ctn);
							ctn.ThrowIfCancellationRequested();
						}

						break;

					case MatchState.Playing:
						PlayerAvatar.Local?.ResetScoreLocally();

						bool timed = Settings.CheckWinByTimer();
						bool eliminationEndsRound =
							Settings.respawnCondition == RespawnCondition.NextRound;

						while (State == MatchState.Playing)
						{
							if (timed)
							{
								float timeLeft = GetTimeLeft();
								UpdateTimerText(timeLeft);

								if (SyncBus.IsAuthority && timeLeft <= 0)
								{
									EndRound(GetRoundLeader());
									break;
								}
							}

							if (SyncBus.IsAuthority && eliminationEndsRound
							    && TryGetEliminationWinner(out byte winner))
							{
								EndRound(winner);
								break;
							}

							await Awaitable.NextFrameAsync(ctn);
							ctn.ThrowIfCancellationRequested();
						}

						break;

					case MatchState.RoundBreak:
						await Awaitable.WaitForSecondsAsync(RoundBreakSeconds, ctn);

						if (SyncBus.IsAuthority)
						{
							stateSync.Value = MatchState.Mustering;
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
			if (rounded == lastTimerSeconds) return;

			lastTimerSeconds = rounded;
			TimeSpan span = TimeSpan.FromSeconds(rounded);
			TimerTextChanged.Invoke(span.ToString(@"m\:ss"));
		}

		public float GetTimeElapsed()
		{
			return ServerTime - TimeRoundStarted;
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
					float endTime = TimeRoundStarted + Settings.roundTimeSeconds;
					timeLeft = Mathf.Max(0, endTime - ServerTime);
					break;
				default:
					timeLeft = Settings.roundTimeSeconds;
					break;
			}

			return timeLeft;
		}
	}
}
