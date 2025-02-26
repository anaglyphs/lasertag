using Anaglyph.Menu;
using UnityEngine;

namespace Anaglyph.Lasertag.UI
{
	public class ToolSelectButton : MonoBehaviour
	{
		public string objectName;
		private HandedButton handedButton;

		private void Awake()
		{
			handedButton = GetComponent<HandedButton>();
			handedButton.onClickIsRight.AddListener(OnClick);
		}

		private void OnClick(bool isRight)
		{
			ToolPalette p = isRight ? ToolPalette.Right : ToolPalette.Left;


			p.toolSelector.SetActiveChild(p.transform.Find(objectName).GetSiblingIndex());
		}
	}
}