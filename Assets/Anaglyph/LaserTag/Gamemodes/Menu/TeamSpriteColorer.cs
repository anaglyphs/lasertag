using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
    public class TeamSpriteColorer : MonoBehaviour
    {
        [SerializeField] private byte team;
		[SerializeField] private float multiply = 0;


		private void OnValidate()
		{
			Color multiplied = TeamManagement.TeamColors[team] * multiply;
			multiplied.a = 1;
			GetComponent<Image>().color =  multiplied;
		}
	}
}
