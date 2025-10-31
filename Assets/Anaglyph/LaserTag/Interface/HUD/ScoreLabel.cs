using System;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class ScoreLabel : MonoBehaviour
    {
	    [SerializeField] private byte team;
	    private Text label;

	    public void SetTeam(byte newScore)
	    {
		    team = newScore;
		    if(enabled)
				UpdateScore();
	    }

	    private void Awake()
	    {
		    label = GetComponent<Text>();
	    }
	    
	    private void Start()
	    {
		    UpdateScore();
	    }

	    private void OnEnable()
	    {
		    MatchReferee.StateChanged += OnMatchStateChanged;
		    MatchReferee.TeamScored += OnTeamScored;
		    
		    if(didStart)
			    UpdateScore();
	    }

	    private void OnDisable()
	    {
		    MatchReferee.StateChanged -= OnMatchStateChanged;
		    MatchReferee.TeamScored -=  OnTeamScored;
	    }

	    private void OnMatchStateChanged(MatchState state)
	    {
			UpdateScore();
	    }

	    private void OnTeamScored(byte scoredTeam, int points)
	    {
		    if(team == scoredTeam) UpdateScore();
	    }

	    private void UpdateScore()
	    {
		    int score = 0;
		    var referee = MatchReferee.Instance;
		    if(referee)
				score = referee.GetTeamScore(team);
		    label.text = score.ToString();
	    }
    }
}
