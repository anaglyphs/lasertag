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
			MatchManager.MatchStateChanged += HandleStateChange;

			startPage.showBackButton = false;
			startButton.onClick.AddListener(StartGame);

			playingPage.showBackButton = false;
			cancelButton.onClick.AddListener(EndGame);
		}

		private void OnDestroy()
		{
			MatchManager.MatchStateChanged -= HandleStateChange;
		}

		private void StartGame()
		{
			MatchManager.Instance?.QueueStartGameOwnerRpc(MatchSettings.DemoGame());
		}

		private void EndGame()
		{
			MatchManager.Instance?.EndGameOwnerRpc();
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
