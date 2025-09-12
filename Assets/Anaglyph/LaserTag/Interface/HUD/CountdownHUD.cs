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

		private CancellationTokenSource countdownCanceller = new();

		private void Start()
		{
			matchReferee.StateChanged += HandleStateChange;
			queued.enabled = false;
			countdownText.enabled = false;
		}

		private void OnDestroy()
		{
			if (matchReferee != null)
				matchReferee.StateChanged -= HandleStateChange;
		}

		private void HandleStateChange(MatchState state)
		{
			queued.enabled = state == MatchState.Mustering;

			countdownCanceller.Cancel();
			countdownText.enabled = false;

			switch (state)
			{
				case MatchState.Countdown:
					countdownCanceller = new();
					_ = CountdownTask(countdownCanceller.Token);
					break;

				case MatchState.Playing:
					countdownCanceller = new();
					_ = GoTask(countdownCanceller.Token);
					break;
			}
		}

		private async Task CountdownTask(CancellationToken ctn)
		{
			countdownText.enabled = true;

			countdownText.text = "3";
			await Awaitable.WaitForSecondsAsync(1, ctn);

			countdownText.text = "2";
			await Awaitable.WaitForSecondsAsync(1, ctn);

			countdownText.text = "1";
			await Awaitable.WaitForSecondsAsync(1, ctn);
		}

		private async Task GoTask(CancellationToken ctn)
		{
			countdownText.enabled = true;

			countdownText.text = "Go!";
			await Awaitable.WaitForSecondsAsync(1.5f, ctn);

			countdownText.enabled = false;
		}
	}
}