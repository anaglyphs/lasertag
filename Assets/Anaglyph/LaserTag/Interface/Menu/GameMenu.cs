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

		private void Start()
		{
			MatchReferee.Instance.StateChanged += HandleStateChange;

			startPage.showBackButton = false;
			startButton.onClick.AddListener(StartGame);

			playingPage.showBackButton = false;
			cancelButton.onClick.AddListener(EndGame);
		}

		private void OnDestroy()
		{
			MatchReferee.Instance.StateChanged -= HandleStateChange;
		}

		private void StartGame()
		{
			MatchReferee.Instance?.QueueStartGameOwnerRpc(MatchSettings.DemoGame());
		}

		private void EndGame()
		{
			MatchReferee.Instance?.EndGameOwnerRpc();
		}

		private void HandleStateChange(MatchState prev, MatchState state)
		{
			if (state == MatchState.NotPlaying)
				startPage.NavigateHere();
			else
				playingPage.NavigateHere();
		}
	}
}
