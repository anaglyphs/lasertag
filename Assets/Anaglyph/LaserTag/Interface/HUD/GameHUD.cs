using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(9999)]
    public class GameHUD : MonoBehaviour
    {
	    [SerializeField] private GameObject scoreGoalHUD;
	    [SerializeField] private GameObject timerGoalHUD;
	    
	    [SerializeField] private Text timerTarget;
	    [SerializeField] private Text scoreTarget;

		private void Awake()
		{
			MatchReferee.StateChanged += OnMatchStateChange;
			OnMatchStateChange(MatchReferee.State);
		}

		private void OnDestroy()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
		}

	    private void OnMatchStateChange(MatchState state)
	    {
		    bool playing = state == MatchState.Playing;
		    
		    gameObject.SetActive(playing);

			if (state == MatchState.NotPlaying)
			{
				timerGoalHUD.SetActive(false);
				scoreGoalHUD.SetActive(false);
			}
			else
			{
				MatchSettings settings = MatchReferee.Settings;

				timerGoalHUD.SetActive(settings.winCondition == WinCondition.Timer);
				scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);

				switch (settings.winCondition)
				{
					case WinCondition.ReachScore:
						scoreTarget.text = settings.scoreTarget.ToString();
						break;
				}
			}
	    }

	    private void Update()
	    {
		    bool playing = MatchReferee.State == MatchState.Playing;

		    if (!playing) return;

		    switch (MatchReferee.Settings.winCondition)
		    {
			    case WinCondition.Timer:
				    TimeSpan time = TimeSpan.FromSeconds(MatchReferee.Instance.GetTimeLeft());
					timerTarget.text = time.ToString(@"m\:ss");
				    break;
			    
			    case WinCondition.ReachScore:
				    
				    break;
		    }
	    }
    }
}
