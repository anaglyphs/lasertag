using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
    public class Selector : MonoBehaviour
    {
		public EventVariable<int> index = new(0);

        [SerializeField] private RectTransform selectionIndicator;

		private Button[] buttons;

		private void Awake()
		{
			buttons = GetComponentsInChildren<Button>();

			for (int i = 0; i < buttons.Length; i++)
			{
				int iCap = i;

				buttons[i].onClick.AddListener(delegate { index.Set(iCap); });
			}

			index.AddListenerAndCheck(UpdateSelectionIndicator);
		}

		private void UpdateSelectionIndicator(int i)
		{
			selectionIndicator.SetParent(buttons[i].transform);
			//selectionIndicator.anchoredPosition = new Vector2(0, 0);
			selectionIndicator.anchorMin = new Vector2(0, 0);
			selectionIndicator.anchorMax = new Vector2(1, 1);
			selectionIndicator.offsetMax = new Vector2(0, 0);
			selectionIndicator.offsetMin = new Vector2(0, 0);
		}
	}
}
