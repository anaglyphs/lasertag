using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class TeamSpriteColorer : MonoBehaviour
    {
        [SerializeField] private byte team;
		[SerializeField] private float multiply = 1;

		[SerializeField] private Image image;

		private void OnValidate()
		{
			TryGetComponent(out image);

			Color multiplied = TeamManagement.TeamColors[team] * multiply;
			multiplied.a = 1;
			image.color =  multiplied;
		}
	}
}
