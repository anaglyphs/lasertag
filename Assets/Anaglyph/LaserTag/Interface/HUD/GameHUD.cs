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
		    bool show = state != MatchState.NotPlaying;
		    
		    gameObject.SetActive(show);

		    if (show)
		    {
			    MatchSettings settings = MatchReferee.Settings;

			    timerGoalHUD.SetActive(settings.winCondition == WinCondition.Timer);
			    scoreGoalHUD.SetActive(settings.winCondition == WinCondition.ReachScore);

			    switch (settings.winCondition)
			    {
				    case WinCondition.Timer:
					    UpdateTimerText();
					    break;
				    
				    case WinCondition.ReachScore:
					    scoreTarget.text = settings.scoreTarget.ToString();
					    break;
			    }
		    }
		    else
		    {
			    timerGoalHUD.SetActive(false);
			    scoreGoalHUD.SetActive(false);
		    }
	    }

	    private void Update()
	    {
		    bool playing = MatchReferee.State == MatchState.Playing;

		    if (!playing) return;

		    switch (MatchReferee.Settings.winCondition)
		    {
			    case WinCondition.Timer:
				    UpdateTimerText();
				    break;
			    
			    case WinCondition.ReachScore:
				    break;
		    }
	    }

	    private void UpdateTimerText()
	    {
		    float seconds = 0;
		    var matchRef = MatchReferee.Instance;
		    if (matchRef)
		    {
			    if (MatchReferee.State == MatchState.Playing)
					seconds = matchRef.GetTimeLeft();
			    else
					seconds = MatchReferee.Settings.timerSeconds;
		    }
		    
		    var time = TimeSpan.FromSeconds(seconds);
		    timerTarget.text = time.ToString(@"m\:ss");
	    }
    }
}
