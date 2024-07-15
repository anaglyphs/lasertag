using Anaglyph.LaserTag;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class TeamButton : MonoBehaviour
    {
        [SerializeField] private byte team;

        private Button button;
        private Image image;

		private void Awake()
		{
			button = GetComponent<Button>();
			image = GetComponent<Image>();

			button.onClick.AddListener(OnClick);
		}

		private void OnClick()
		{
			MainPlayer.Instance.team = team;
		}

		private void Start()
		{
			image.color = TeamManagement.TeamColors[team];
		}
	}
}
