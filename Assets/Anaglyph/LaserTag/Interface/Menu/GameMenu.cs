using Anaglyph.Menu;
using Anaglyph.Netcode;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameMenu : MonoBehaviour
	{
		[SerializeField] private NavPagesParent navView;

		[FormerlySerializedAs("startPage")] [Header("Start page")] [SerializeField]
		private NavPage matchPage;

		[SerializeField] private Toggle winByScoreToggle;
		[SerializeField] private TMP_InputField timeField;
		[SerializeField] private TMP_InputField scoreField;
		[SerializeField] private TMP_InputField damageMultiplierField;
		[SerializeField] private Button startButton;

		[Header("Playing page")] [SerializeField]
		private NavPage playingPage;

		[SerializeField] private Button cancelButton;

		[FormerlySerializedAs("mapEditorPage")]
		[FormerlySerializedAs("editorPage")]
		[Header("Map Editor Page")]
		[SerializeField]
		private NavPage mapManagerPage;

		[SerializeField] private Button editMapButton;

		[FormerlySerializedAs("mapEditingPage")] [Header("Map Editing page")] [SerializeField]
		private NavPage editingMapPage;

		[SerializeField] private Button finishEditingButton;

		private MatchReferee referee => MatchReferee.Instance;

		private MatchSettings matchSettings = MatchSettings.DemoGame();

		private void Start()
		{
			MatchReferee.StateChanged += OnMatchStateChanged;
			OnMatchStateChanged(MatchReferee.State);

			NetcodeManagement.StateChanged += OnNetcodeStateChanged;
			OnNetcodeStateChanged(NetcodeManagement.State);

			MapEditor.ActiveChanged += OnMapEditorStateChanged;
			OnMapEditorStateChanged(MapEditor.IsActive);

			// match page

			winByScoreToggle.onValueChanged.AddListener(winByScore =>
			{
				timeField.gameObject.SetActive(!winByScore);
				scoreField.gameObject.SetActive(winByScore);

				matchSettings.winCondition = winByScore ? WinCondition.ReachScore : WinCondition.Timer;
			});
			winByScoreToggle.isOn = matchSettings.CheckWinByScore();
			winByScoreToggle.onValueChanged.Invoke(winByScoreToggle.isOn);

			timeField.SetTextWithoutNotify((matchSettings.timerSeconds / 60f).ToString());
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

			// map manager page
			editMapButton.onClick.AddListener(() => MapEditor.SetActive(true));

			// editing map page
			editingMapPage.showBackButton = false;
			finishEditingButton.onClick.AddListener(() => MapEditor.SetActive(false));
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChanged;
			NetcodeManagement.StateChanged -= OnNetcodeStateChanged;
			MapEditor.ActiveChanged -= OnMapEditorStateChanged;
		}

		private void OnMapEditorStateChanged(bool state)
		{
			navView.SetModalPresented(editingMapPage, state);
		}

		private void OnMatchStateChanged(MatchState state)
		{
			bool playing = state != MatchState.NotPlaying;

			navView.SetModalPresented(playingPage, playing);

			if (playing)
				MapEditor.SetActive(false);
		}

		private void OnNetcodeStateChanged(NetcodeState state)
		{
			startButton.interactable = state == NetcodeState.Connected;
		}
	}
}