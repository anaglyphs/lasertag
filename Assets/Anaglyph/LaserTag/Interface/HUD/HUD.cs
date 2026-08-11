using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Head-locked HUD. At most one overlay is on screen at a time; which one is
	/// decided in <see cref="ResolveOverlay"/>. Everything is polled from match,
	/// player and netcode state, so there is no subscription or activation
	/// bookkeeping to keep in sync.
	/// </summary>
	[DefaultExecutionOrder(9999)]
	public class HUD : MonoBehaviour
	{
		private const float ResultsSeconds = 5;
		private const float ReadyFlashSeconds = 1;
		private const float GoSeconds = 1.5f;

		private const string ConnectingText = "Connecting...";
		private const string AligningText = "Aligning...";
		private const string ReadyText = "Ready!";

		private const int NoCountdown = -1;
		private const int GoCountdown = 0;

		private enum Overlay
		{
			None,
			Connection,
			Results,
			Death,
			Countdown,
			Muster
		}

		private VisualElement connectionHUD;
		private VisualElement resultsHUD;
		private VisualElement deathHUD;
		private VisualElement countdownHUD;
		private VisualElement musterHUD;

		private Label connectionLabel;
		private Label respawnLabel;
		private Label countdownLabel;
		private Label resultsTitle;
		private Label resultsRoundLabel;
		private Label[] resultsScores;

		private Overlay shownOverlay = Overlay.None;

		private MatchState lastMatchState;
		private bool wasReady;
		private float resultsHideTime;
		private float readyHideTime;

		// last values pushed into labels, so per-frame refreshes only allocate
		// strings when the text actually changes
		private string shownConnectionText;
		private string shownRespawnText;
		private int shownCountdown;
		private int shownRound;
		private readonly int[] shownScores = new int[Teams.NumTeams];

		private void OnEnable()
		{
			connectionHUD = HUDElement.Require<VisualElement>(this, "connection-hud");
			resultsHUD = HUDElement.Require<VisualElement>(this, "results-hud");
			deathHUD = HUDElement.Require<VisualElement>(this, "death-hud");
			countdownHUD = HUDElement.Require<VisualElement>(this, "countdown-hud");
			musterHUD = HUDElement.Require<VisualElement>(this, "muster-hud");

			connectionLabel = HUDElement.Require<Label>(this, "connection-label");
			respawnLabel = HUDElement.Require<Label>(this, "respawn-label");
			countdownLabel = HUDElement.Require<Label>(this, "countdown-label");
			resultsTitle = HUDElement.Require<Label>(this, "results-title");
			resultsRoundLabel = HUDElement.Require<Label>(this, "results-round-label");

			resultsScores = new Label[Teams.NumTeams];
			resultsScores[1] = HUDElement.Require<Label>(this, "results-red-score");
			resultsScores[2] = HUDElement.Require<Label>(this, "results-blue-score");

			shownOverlay = Overlay.None;
			lastMatchState = MatchReferee.State;
			wasReady = IsReady();
			resultsHideTime = 0;
			readyHideTime = 0;

			foreach (Overlay overlay in System.Enum.GetValues(typeof(Overlay)))
				Display(overlay, false);
		}

		private void OnDisable()
		{
			foreach (Overlay overlay in System.Enum.GetValues(typeof(Overlay)))
				Display(overlay, false);

			shownOverlay = Overlay.None;
		}

		private void Update()
		{
			TrackResults();
			TrackReadyFlash();

			Overlay overlay = ResolveOverlay();

			if (overlay != shownOverlay)
			{
				Display(shownOverlay, false);
				Display(overlay, true);
				shownOverlay = overlay;
				InvalidateShownText();
			}

			RefreshContent(overlay);
		}

		// A round or match ends the moment play stops. Results then hold the
		// screen for their own duration, which outlasts nothing else.
		private void TrackResults()
		{
			MatchState state = MatchReferee.State;

			if (state != lastMatchState)
			{
				if (lastMatchState == MatchState.Playing)
					resultsHideTime = Time.time + ResultsSeconds;
				else if (state == MatchState.Countdown || state == MatchState.Playing)
					resultsHideTime = 0;

				lastMatchState = state;
			}
		}

		private void TrackReadyFlash()
		{
			bool ready = IsReady();

			if (ready && !wasReady)
				readyHideTime = Time.time + ReadyFlashSeconds;

			wasReady = ready;
		}

		private Overlay ResolveOverlay()
		{
			// connecting and aligning block play, so they outrank everything
			if (GetBlockingConnectionText() != null) return Overlay.Connection;
			if (Time.time < resultsHideTime) return Overlay.Results;
			if (MainPlayer.Instance != null && !MainPlayer.Instance.IsAlive) return Overlay.Death;
			if (GetCountdown() != NoCountdown) return Overlay.Countdown;
			if (MatchReferee.State == MatchState.Mustering) return Overlay.Muster;
			if (Time.time < readyHideTime) return Overlay.Connection;

			return Overlay.None;
		}

		private void RefreshContent(Overlay overlay)
		{
			switch (overlay)
			{
				case Overlay.Connection:
					SetText(connectionLabel, ref shownConnectionText,
						GetBlockingConnectionText() ?? ReadyText);
					break;

				case Overlay.Results:
					RefreshResults();
					break;

				case Overlay.Death:
					RefreshRespawnLabel();
					break;

				case Overlay.Countdown:
					int countdown = GetCountdown();
					if (countdown != shownCountdown)
					{
						shownCountdown = countdown;
						countdownLabel.text = countdown == GoCountdown
							? "Go!"
							: countdown.ToString();
					}

					break;
			}
		}

		private void RefreshResults()
		{
			MatchSettings settings = MatchReferee.Settings;
			int numRounds = settings.GetNumRounds();
			bool multiRound = numRounds > 1;

			resultsTitle.text = MatchReferee.State == MatchState.NotPlaying
				? "GAME OVER"
				: "ROUND OVER";

			HUDElement.SetDisplayed(resultsRoundLabel, multiRound);

			if (multiRound)
			{
				// RoundsPlayed already counts the round being shown
				int round = Mathf.Clamp(MatchReferee.RoundsPlayed, 1, numRounds);
				if (round != shownRound)
				{
					shownRound = round;
					resultsRoundLabel.text = $"ROUND {round} / {numRounds}";
				}
			}

			// a match of several rounds is decided by rounds won, not by the
			// score of the round that just ended
			for (byte team = 1; team < Teams.NumTeams; team++)
			{
				int value = multiRound
					? MatchReferee.GetTeamRoundWins(team)
					: MatchReferee.GetTeamScore(team);

				SetInt(resultsScores[team], ref shownScores[team], value);
			}
		}

		private void RefreshRespawnLabel()
		{
			MatchSettings settings = MatchReferee.Settings;
			string text;

			if (settings.respawnCondition == RespawnCondition.NextRound
			    && MatchReferee.State == MatchState.Playing)
			{
				text = "WAIT FOR NEXT ROUND";
			}
			else if (settings.respawnCondition == RespawnCondition.InBases
			         && !MainPlayer.Instance.IsInFriendlyBase)
			{
				text = "GO TO:   BASE";
			}
			else
			{
				float timeSinceDeath = Time.time - MainPlayer.Instance.LastDeathTime;
				float timeToRespawn = settings.respawnSeconds - timeSinceDeath;
				text = $"RESPAWN: {timeToRespawn:F1}s";
			}

			if (text != shownRespawnText)
			{
				shownRespawnText = text;
				respawnLabel.text = text;
			}
		}

		private static bool IsReady()
		{
			return NetcodeManagement.State == NetcodeState.Connected
			       && ColocationManager.IsColocated;
		}

		private static string GetBlockingConnectionText()
		{
			NetcodeState state = NetcodeManagement.State;

			if (state == NetcodeState.Connecting)
				return ConnectingText;

			if (state == NetcodeState.Connected && !ColocationManager.IsColocated)
				return AligningText;

			return null;
		}

		/// <summary>
		/// The number to show mid-countdown, <see cref="GoCountdown"/> for the
		/// moment play starts, or <see cref="NoCountdown"/>.
		/// </summary>
		private static int GetCountdown()
		{
			if (MatchReferee.Instance == null)
				return NoCountdown;

			float elapsed = MatchReferee.Instance.GetTimeElapsed();

			if (MatchReferee.State == MatchState.Countdown)
				return Mathf.Clamp(Mathf.CeilToInt(-elapsed), 1,
					Mathf.CeilToInt(MatchReferee.CountdownSeconds));

			if (MatchReferee.State == MatchState.Playing && elapsed < GoSeconds)
				return GoCountdown;

			return NoCountdown;
		}

		private void Display(Overlay overlay, bool displayed)
		{
			VisualElement element = overlay switch
			{
				Overlay.Connection => connectionHUD,
				Overlay.Results => resultsHUD,
				Overlay.Death => deathHUD,
				Overlay.Countdown => countdownHUD,
				Overlay.Muster => musterHUD,
				_ => null
			};

			if (element != null)
				HUDElement.SetDisplayed(element, displayed);
		}

		private void InvalidateShownText()
		{
			shownConnectionText = null;
			shownRespawnText = null;
			shownCountdown = NoCountdown;
			shownRound = 0;

			for (int i = 0; i < shownScores.Length; i++)
				shownScores[i] = int.MinValue;
		}

		private static void SetText(Label label, ref string shown, string text)
		{
			if (shown == text) return;

			shown = text;
			label.text = text;
		}

		private static void SetInt(Label label, ref int shown, int value)
		{
			if (shown == value) return;

			shown = value;
			label.text = value.ToString();
		}
	}
}
