using Anaglyph.Menu;
using UnityEngine;

namespace Anaglyph.MenuXR
{
    public class NavTitle : MonoBehaviour
    {
		private NavPage parentNavPage;
		[SerializeField] private GameObject backButton;
		[SerializeField] private RectTransform titleRectTransform;
		public bool alwaysHideBack;

		private void Awake()
		{
			parentNavPage = GetComponentInParent<NavPage>(true);
		}

		private void OnEnable()
		{
			parentNavPage = GetComponentInParent<NavPage>(true);

			bool showBackButton = !alwaysHideBack && parentNavPage.ParentView?.History.Count > 1;

			backButton.SetActive(showBackButton);
			titleRectTransform.anchoredPosition = showBackButton ? new Vector2(80, 0) : Vector2.zero;
		}
	}
}
