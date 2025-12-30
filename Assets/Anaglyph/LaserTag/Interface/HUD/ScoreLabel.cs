using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class ScoreLabel : MonoBehaviour
	{
		[SerializeField] private byte team;
		private Text label;

		private void Awake()
		{
			label = GetComponent<Text>();
		}

		private void Start()
		{
			UpdateScore(MatchReferee.GetTeamScore(team));
		}

		private void OnEnable()
		{
			label.text = "0";
			MatchReferee.TeamScored += OnTeamScored;
			
			if (didStart) UpdateScore(MatchReferee.GetTeamScore(team));  
		}

		private void OnDisable()
		{
			MatchReferee.TeamScored -= OnTeamScored;
		}

		private void OnTeamScored(byte scoredTeam, int points)
		{
			if (team == scoredTeam) 
				UpdateScore(MatchReferee.GetTeamScore(scoredTeam));
		}

		private void UpdateScore(int score) => label.text = score.ToString();
	}
}