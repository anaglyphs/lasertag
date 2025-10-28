using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class ScoreLabel : MonoBehaviour
    {
	    public byte team;
	    private Text label;

	    private void Awake()
	    {
		    label = GetComponent<Text>();
	    }

	    private void OnEnable()
	    {
		    MatchReferee.StateChanged += OnMatchStateChanged;
		    MatchReferee.TeamScored += OnTeamScored;
		    UpdateScore(team);

	    }

	    private void OnDisable()
	    {
		    MatchReferee.StateChanged -= OnMatchStateChanged;
		    MatchReferee.TeamScored -=  OnTeamScored;
	    }

	    private void OnMatchStateChanged(MatchState state)
	    {
		    switch (state)
		    {
			    case MatchState.Mustering:
				    label.text = "0";
				    break;
		    }
	    }

	    private void OnTeamScored(byte scoredTeam, int points)
	    {
		    if(team == scoredTeam) UpdateScore(scoredTeam);
	    }

	    private void UpdateScore(byte team)
	    {
		    label.text = MatchReferee.Instance.GetTeamScore(team).ToString();
	    }
    }
}
