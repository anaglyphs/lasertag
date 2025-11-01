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
			var matchRef = MatchReferee.Instance;
			if (matchRef) UpdateScore(matchRef.GetTeamScore(team));
		}

		private void OnEnable()
		{
			label.text = "0";
			MatchReferee.TeamScored += OnTeamScored;

			var matchRef = MatchReferee.Instance;
			if (didStart && matchRef) UpdateScore(matchRef.GetTeamScore(team));  
		}

		private void OnDisable()
		{
			MatchReferee.TeamScored -= OnTeamScored;
		}

		private void OnTeamScored(byte scoredTeam, int score)
		{
			if (team == scoredTeam) 
				UpdateScore(score);
		}

		private void UpdateScore(int score) => label.text = score.ToString();
	}
}