using Anaglyph.Menu;
using UnityEngine;

namespace Anaglyph.LaserTag.UI
{
	public class SpawnObjectButton : MonoBehaviour
	{
		public GameObject objectToSpawn;
		private HandedButton handedButton;

		private void Awake()
		{
			handedButton = GetComponent<HandedButton>();
			handedButton.onClickIsRight.AddListener(OnClick);
		}

		private void OnClick(bool isRight)
		{
			ToolPalette p = isRight ? ToolPalette.Right : ToolPalette.Left;
			p.OpenSpawnerWithObject(objectToSpawn);
		}
	}
}