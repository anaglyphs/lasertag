using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	public class ScoreLabel : MonoBehaviour
	{
		public enum ScoreSource : byte
		{
			TeamScore,
			RoundWins
		}

		[SerializeField] private byte team;
		[SerializeField] private ScoreSource source = ScoreSource.TeamScore;
		[SerializeField] private string labelName;

		private Label label;

		private void Start()
		{
			Refresh();
		}

		private void OnEnable()
		{
			label = HUDElement.Require<Label>(this, labelName);
			label.text = "0";
			MatchReferee.TeamScored += OnTeamScored;
			MatchReferee.TeamWonRound += OnTeamWonRound;

			if (didStart) Refresh();
		}

		private void OnDisable()
		{
			MatchReferee.TeamScored -= OnTeamScored;
			MatchReferee.TeamWonRound -= OnTeamWonRound;
		}

		public void SetSource(ScoreSource newSource)
		{
			source = newSource;

			if (didStart && isActiveAndEnabled)
				Refresh();
		}

		private void OnTeamScored(byte scoredTeam, int points)
		{
			if (source == ScoreSource.TeamScore && scoredTeam == team)
				Refresh();
		}

		private void OnTeamWonRound(byte winningTeam, int totalWins)
		{
			if (source == ScoreSource.RoundWins && winningTeam == team)
				Refresh();
		}

		private void Refresh()
		{
			int value = source == ScoreSource.RoundWins
				? MatchReferee.GetTeamRoundWins(team)
				: MatchReferee.GetTeamScore(team);

			label.text = value.ToString();
		}
	}
}
