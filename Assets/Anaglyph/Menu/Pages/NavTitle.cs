using Anaglyph.Menu;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.MenuXR
{
	public class NavTitle : MonoBehaviour
	{
		private NavPage parentNavPage;
		[SerializeField] private GameObject backButton;

		[FormerlySerializedAs("titleRectTransform")] [SerializeField]
		private RectTransform titleRect;

		private float initOffsetX;

		private void Awake()
		{
			parentNavPage = GetComponentInParent<NavPage>(true);
			initOffsetX = titleRect.offsetMin.x;
		}

		private void OnEnable()
		{
			parentNavPage = GetComponentInParent<NavPage>(true);

			bool showBackButton = parentNavPage.showBackButton && parentNavPage.ParentView?.History.Count > 1;

			backButton.SetActive(showBackButton);
			float offsetX = showBackButton ? initOffsetX : 0;
			titleRect.offsetMin = new Vector2(offsetX, titleRect.offsetMin.y);
		}
	}
}