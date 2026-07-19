using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class CountdownHUD : MonoBehaviour
	{
		[SerializeField] private Graphic queued;
		[SerializeField] private TMP_Text countdownText;

		private CancellationTokenSource countdownCanceller = new();

		private void Start()
		{
			MatchReferee.StateChanged += HandleStateChange;
			queued.enabled = false;
			countdownText.enabled = false;
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= HandleStateChange;
		}

		private void HandleStateChange(MatchState state)
		{
			queued.enabled = state == MatchState.Mustering;

			countdownCanceller.Cancel();
			countdownText.enabled = false;

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
			
			countdownText.enabled = true;
			
			// time elapsed should be -3
			countdownText.text = "3";
			await Awaitable.WaitForSecondsAsync(CountdownTime(-3, 1), ctkn);

			// time elapsed should be -2
			countdownText.text = "2";
			await Awaitable.WaitForSecondsAsync(CountdownTime(-2, 1), ctkn);

			// time elapsed should be -1
			countdownText.text = "1";
			await Awaitable.WaitForSecondsAsync(CountdownTime(-1, 1), ctkn);
			
			// always show for 1.5 seconds
			countdownText.text = "Go!";
			await Awaitable.WaitForSecondsAsync(1.5f, ctkn);

			countdownText.enabled = false;
		}
	}
}