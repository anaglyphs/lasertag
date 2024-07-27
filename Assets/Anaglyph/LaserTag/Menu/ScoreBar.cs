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
			this.SetComponent(ref image);
		}

		private void Update()
		{
			int divideBy = 0;

			if (RoundManager.Instance.ActiveSettings.CheckWinByTimer())
			{
				divideBy = RoundManager.Instance.GetTeamScore(RoundManager.Instance.WinningTeam);
				
			} else if(RoundManager.Instance.ActiveSettings.CheckWinByPoints())
			{
				divideBy = RoundManager.Instance.ActiveSettings.scoreTarget;
			}

			float fillAmount = divideBy > 0 ? (float)RoundManager.Instance.GetTeamScore(team) / (float)divideBy : 0;

			image.fillAmount = Mathf.Lerp(image.fillAmount, fillAmount, 20 * Time.deltaTime);
		}

		private void OnDisable()
		{
			image.fillAmount = 0;
		}
	}
}
