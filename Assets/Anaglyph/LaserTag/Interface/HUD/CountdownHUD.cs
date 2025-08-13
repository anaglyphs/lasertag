using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class CountdownHUD : MonoBehaviour
	{
		[SerializeField] private Graphic queued;
		[SerializeField] private Text countdownText;

		float countdownTime = 3.0f;
		private bool showCountdownOnCountdownText = false;


		private void Start()
		{
			MatchManager.MatchStateChanged += HandleStateChange;

			queued.enabled = false;
		}

		private void Update()
		{
			countdownTime = Mathf.Clamp(countdownTime - Time.deltaTime, 0.0f, 10.0f);

			if (showCountdownOnCountdownText)
			{
				countdownText.text = string.Format($"{0,3:N1}", countdownTime + 1.0f);
			}
		}

		private void OnDestroy()
		{
			MatchManager.MatchStateChanged -= HandleStateChange;
		}

		private async void HandleStateChange(MatchState prev, MatchState state)
		{
			switch (state)
			{
				case MatchState.NotPlaying:
					countdownText.enabled = false;
					queued.enabled = false;

					break;

				case MatchState.Queued:
					queued.enabled = true;

					break;

				case MatchState.Countdown:
					queued.enabled = false;

					countdownText.text = "";
					countdownText.enabled = true;
					countdownTime = 3.0f;
					showCountdownOnCountdownText = true;

					break;

				case MatchState.Playing:
					queued.enabled = false;

					showCountdownOnCountdownText = false;
					countdownText.text = "Go!";
					countdownText.enabled = true;

					await Awaitable.WaitForSecondsAsync(1);

					countdownText.enabled = false;

					break;
			}
		}
	}
}
