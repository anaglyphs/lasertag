using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class ScoreBar : MonoBehaviour
    {
		[SerializeField] private Image image;
		public byte team = 0;

		private void OnValidate()
		{
			TryGetComponent(out image);
		}

		private void Update()
		{
			int divideBy = 0;

			if (RoundManager.ActiveSettings.CheckWinByTimer())
			{
				divideBy = RoundManager.GetTeamScore(RoundManager.WinningTeam);
				
			} else if(RoundManager.ActiveSettings.CheckWinByPoints())
			{
				divideBy = RoundManager.ActiveSettings.scoreTarget;
			}

			float fillAmount = divideBy > 0 ? (float)RoundManager.GetTeamScore(team) / (float)divideBy : 0;

			image.fillAmount = Mathf.Lerp(image.fillAmount, fillAmount, 20 * Time.deltaTime);
		}

		private void OnEnable() => image.fillAmount = 0;
	}
}
