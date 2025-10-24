using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class GameMenu : MonoBehaviour
	{
		[SerializeField] private NavPagesParent navView;

		[Header(nameof(startPage))]
		[SerializeField] private NavPage startPage = null;
		[SerializeField] private Button startButton = null;

		[Header(nameof(playingPage))]
		[SerializeField] private NavPage playingPage = null;
		[SerializeField] private Button cancelButton = null;

		private MatchReferee referee => MatchReferee.Instance;

		private void Start()
		{
			MatchReferee.StateChanged += HandleStateChange;

			startPage.showBackButton = false;
			startButton.onClick.AddListener(StartGame);

			playingPage.showBackButton = false;
			cancelButton.onClick.AddListener(EndGame);
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= HandleStateChange;
		}

		private void StartGame()
		{
			referee?.StartMatchRpc(MatchSettings.DemoGame());
		}

		private void EndGame()
		{
			referee?.EndMatchRpc();
		}

		private void HandleStateChange(MatchState state)
		{
			if (state == MatchState.NotPlaying)
				startPage.NavigateHere();
			else
				playingPage.NavigateHere();
		}
	}
}