using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class CountdownHUD : MonoBehaviour
	{
		[SerializeField] private Graphic queued;
		[SerializeField] private Text countdownText;

		private MatchReferee matchReferee => MatchReferee.Instance;

		float countdownTime = 0;
		private bool showCountdownOnCountdownText = false;


		private void Start()
		{
			matchReferee.StateChanged += HandleStateChange;

			queued.enabled = false;
		}

		private void Update()
		{
			float time = countdownTime - Time.time;

			if (showCountdownOnCountdownText)
			{
				countdownText.text = string.Format($"{Mathf.CeilToInt(time)}");
			}
		}

		private void OnDestroy()
		{
			matchReferee.StateChanged -= HandleStateChange;
		}

		private async void HandleStateChange(MatchState state)
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
					countdownTime = Time.time + 3;
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
