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
			RoundManager.OnRoundStateChange += HandleStateChange;

			queued.enabled = false;
		}

		private void Update()
		{
			countdownTime = Mathf.Clamp(countdownTime - Time.deltaTime, 0.0f, 10.0f);

			if (showCountdownOnCountdownText)
			{
				countdownText.text = string.Format("{0,3:N1}", countdownTime + 1.0f);
			}
		}

		private void OnDestroy()
		{
			RoundManager.OnRoundStateChange -= HandleStateChange;
		}

		private async void HandleStateChange(RoundState prev, RoundState state)
		{
			switch (state)
			{
				case RoundState.NotPlaying:
					countdownText.enabled = false;
					queued.enabled = false;

					break;

				case RoundState.Queued:
					queued.enabled = true;

					break;

				case RoundState.Countdown:
					queued.enabled = false;

					countdownText.text = "";
					countdownText.enabled = true;
					countdownTime = 3.0f;
					showCountdownOnCountdownText = true;

					break;

				case RoundState.Playing:
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
