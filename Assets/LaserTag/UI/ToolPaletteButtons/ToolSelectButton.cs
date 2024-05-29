using EcMenuXR;
using UnityEngine;

namespace LaserTag.UI
{
	public class ToolSelectButton : MonoBehaviour
	{
		public int toolIndex;
		private HandedButton handedButton;

		private void Awake()
		{
			handedButton = GetComponent<HandedButton>();
			handedButton.onClickIsRight.AddListener(OnClick);
		}

		private void OnClick(bool isRight)
		{
			ToolPalette p = isRight ? ToolPalette.Right : ToolPalette.Left;
			p.toolSelector.SetActiveChild(toolIndex);
		}
	}
}