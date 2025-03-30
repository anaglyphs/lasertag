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
			RoundManager.OnRoundStateChange += HandleStateChange;

			startPage.showBackButton = false;
			startButton.onClick.AddListener(StartGame);

			playingPage.showBackButton = false;
			cancelButton.onClick.AddListener(EndGame);
		}

		private void OnDestroy()
		{
			RoundManager.OnRoundStateChange -= HandleStateChange;
		}

		private void StartGame()
		{
			RoundManager.Instance?.QueueStartGameOwnerRpc(RoundSettings.DemoGame());
		}

		private void EndGame()
		{
			RoundManager.Instance?.EndGameOwnerRpc();
		}

		private void HandleStateChange(RoundState prev, RoundState state)
		{
			if (state == RoundState.NotPlaying)
				startPage.NavigateHere();
			else
				playingPage.NavigateHere();
		}
	}
}
