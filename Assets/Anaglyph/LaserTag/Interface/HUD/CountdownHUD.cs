using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class CountdownHUD : MonoBehaviour
	{
		[SerializeField] private Graphic queued;
		[SerializeField] private Text countdownText;

		private MatchReferee matchReferee => MatchReferee.Instance;

		private Task countdownTask = Task.CompletedTask;
		private CancellationTokenSource countdownCanceller = new();

		private void Start()
		{
			matchReferee.StateChanged += HandleStateChange;
			queued.enabled = false;
			countdownText.enabled = false;
		}

		private void OnDestroy()
		{
			matchReferee.StateChanged -= HandleStateChange;
		}

		private async void HandleStateChange(MatchState state)
		{
			queued.enabled = state == MatchState.Queued;

			if (state != MatchState.Countdown && !countdownTask.IsCompleted)
			{
				countdownCanceller.Cancel();
				countdownText.enabled = false;
			}

			switch (state)
			{
				case MatchState.Countdown:
					countdownTask = CountdownTask(countdownCanceller.Token);
					break;

				case MatchState.Playing:
					queued.enabled = false;
					countdownText.text = "Go!";
					countdownText.enabled = true;

					await Awaitable.WaitForSecondsAsync(1);

					countdownText.enabled = false;

					break;
			}
		}

		private async Task CountdownTask(CancellationToken ctn)
		{
			countdownText.enabled = true;

			countdownText.text = "3";
			await Awaitable.WaitForSecondsAsync(1);
			if (ctn.IsCancellationRequested) return;

			countdownText.text = "2";
			await Awaitable.WaitForSecondsAsync(1);
			if (ctn.IsCancellationRequested) return;

			countdownText.text = "1";
			await Awaitable.WaitForSecondsAsync(1);

			countdownText.enabled = false;
		}
	}
}
