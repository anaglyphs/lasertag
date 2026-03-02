using Anaglyph.Menu;
using Anaglyph.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameMenu : MonoBehaviour
	{
		[SerializeField] private NavPagesParent navView;

		[Header(nameof(startPage))] [SerializeField]
		private NavPage startPage;

		[SerializeField] private Toggle winByScoreToggle;

		[SerializeField] private InputField timeField;
		[SerializeField] private InputField scoreField;
		[SerializeField] private InputField damageMultiplierField;

		[SerializeField] private Button startButton;

		[Header(nameof(playingPage))] [SerializeField]
		private NavPage playingPage;

		[SerializeField] private Button cancelButton;

		private MatchReferee referee => MatchReferee.Instance;

		private MatchSettings matchSettings = MatchSettings.DemoGame();

		private void Start()
		{
			MatchReferee.StateChanged += OnMatchStateChanged;
			OnMatchStateChanged(MatchReferee.State);

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			OnNetcodeStateChanged(NetcodeManagement.State);

			// start page
			startPage.showBackButton = false;

			winByScoreToggle.onValueChanged.AddListener(winByScore =>
			{
				timeField.gameObject.SetActive(!winByScore);
				scoreField.gameObject.SetActive(winByScore);
			});
			winByScoreToggle.isOn = matchSettings.CheckWinByScore();
			winByScoreToggle.onValueChanged.Invoke(winByScoreToggle.isOn);

			timeField.SetTextWithoutNotify(matchSettings.timerSeconds.ToString());
			timeField.onValueChanged.AddListener(str =>
			{
				if (float.TryParse(str, out float f))
					matchSettings.timerSeconds = (int)(f * 60f);
			});

			scoreField.SetTextWithoutNotify(matchSettings.scoreTarget.ToString());
			scoreField.onValueChanged.AddListener(str =>
			{
				if (short.TryParse(str, out short s))
					matchSettings.scoreTarget = s;
			});

			damageMultiplierField.SetTextWithoutNotify(matchSettings.damageMultiplier.ToString());
			damageMultiplierField.onValueChanged.AddListener(str =>
			{
				if (float.TryParse(str, out float f))
					matchSettings.damageMultiplier = f;
			});

			startButton.onClick.AddListener(() => referee?.QueueMatchRpc(matchSettings));

			// playing page
			playingPage.showBackButton = false;
			cancelButton.onClick.AddListener(() => referee?.EndMatchRpc());
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (state == MatchState.NotPlaying)
				startPage.NavigateHere();
			else
				playingPage.NavigateHere();
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			startButton.interactable = state == NetcodeState.Connected;
		}
	}
}