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

		private async Awaitable CountdownTask(CancellationToken ctn)
		{
			countdownText.enabled = true;

			float timeElapsed = MatchReferee.Current.GetTimeElapsed();

			countdownText.text = "3";
			await Awaitable.WaitForSecondsAsync(Mathf.Max(0, 1 - timeElapsed), ctn);

			countdownText.text = "2";
			await Awaitable.WaitForSecondsAsync(Mathf.Max(0, 2 - timeElapsed), ctn);

			countdownText.text = "1";
			await Awaitable.WaitForSecondsAsync(Mathf.Max(0, 3 - timeElapsed), ctn);

			countdownText.text = "Go!";
			await Awaitable.WaitForSecondsAsync(Mathf.Max(0, 4.5f - timeElapsed), ctn);

			countdownText.enabled = false;
		}
	}
}