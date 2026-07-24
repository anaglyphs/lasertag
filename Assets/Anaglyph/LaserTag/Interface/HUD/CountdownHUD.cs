using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	public class CountdownHUD : MonoBehaviour
	{
		private Label queueLabel;
		private Label countdownLabel;

		private CancellationTokenSource countdownCanceller = new();

		private void OnEnable()
		{
			queueLabel = HUDElement.Require<Label>(this, "queue-label");
			countdownLabel = HUDElement.Require<Label>(this, "countdown-label");

			HUDElement.SetDisplayed(queueLabel, false);
			HUDElement.SetDisplayed(countdownLabel, false);

			MatchReferee.StateChanged += HandleStateChange;
		}

		private void OnDisable()
		{
			MatchReferee.StateChanged -= HandleStateChange;
			countdownCanceller.Cancel();
		}

		private void HandleStateChange(MatchState state)
		{
			HUDElement.SetDisplayed(queueLabel, state == MatchState.Mustering);

			countdownCanceller.Cancel();
			HUDElement.SetDisplayed(countdownLabel, false);

			if (state == MatchState.Countdown)
			{
				countdownCanceller = new CancellationTokenSource();
				_ = CountdownTask(countdownCanceller.Token);
			}
		}

		private float CountdownTime(float atTime, float forDuration)
		{
			return Mathf.Max(0, MatchReferee.Instance.GetTimeElapsed() - atTime + forDuration);
		}

		private async Awaitable CountdownTask(CancellationToken ctkn)
		{
			try
			{
				HUDElement.SetDisplayed(countdownLabel, true);

				// time elapsed should be -3
				countdownLabel.text = "3";
				await Awaitable.WaitForSecondsAsync(CountdownTime(-3, 1), ctkn);

				// time elapsed should be -2
				countdownLabel.text = "2";
				await Awaitable.WaitForSecondsAsync(CountdownTime(-2, 1), ctkn);

				// time elapsed should be -1
				countdownLabel.text = "1";
				await Awaitable.WaitForSecondsAsync(CountdownTime(-1, 1), ctkn);

				// always show for 1.5 seconds
				countdownLabel.text = "Go!";
				await Awaitable.WaitForSecondsAsync(1.5f, ctkn);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			HUDElement.SetDisplayed(countdownLabel, false);
		}
	}
}
