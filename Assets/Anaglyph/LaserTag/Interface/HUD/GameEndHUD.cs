using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class GameEndHUD : MonoBehaviour
	{
		private CancellationTokenSource scoreCanceller = new();

		private void Start()
		{
			MatchReferee.MatchFinished += OnMatchFinished;
			MatchReferee.StateChanged += OnStateChanged;
			
			gameObject.SetActive(false);
		}

		private void OnStateChanged(MatchState state)
		{
			if (state != MatchState.NotPlaying)
			{
				scoreCanceller.Cancel();
				gameObject.SetActive(false);
			}
		}

		private void OnDestroy()
		{
			MatchReferee.MatchFinished -= OnMatchFinished;
			MatchReferee.StateChanged -= OnStateChanged;
			scoreCanceller.Cancel();
		}

		private void OnMatchFinished()
		{
			scoreCanceller.Cancel();
			scoreCanceller = new();
			_ = ShowScore(scoreCanceller.Token);
		}

		private async Task ShowScore(CancellationToken ctn)
		{
			try
			{
				gameObject.SetActive(true);
				
				await Awaitable.WaitForSecondsAsync(5, ctn);
				ctn.ThrowIfCancellationRequested();
			}
			catch (OperationCanceledException) { }

			gameObject.SetActive(false);
		}
	}
}