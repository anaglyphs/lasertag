using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Menu
{
	public class NavPage : MonoBehaviour
	{
		[SerializeField] private RectTransform rectTransform;
		[SerializeField] private CanvasGroup canvasGroup;
		public bool showBackButton = true;

		private NavPagesParent parentView;

		public RectTransform RectTransform => rectTransform;
		public CanvasGroup CanvasGroup => canvasGroup;
		public NavPagesParent ParentView => parentView;

		public UnityEvent NavigatingHere = new();
		public UnityEvent NavigatingAway = new();
		public UnityEvent NavigatingBack = new();

		private void OnValidate()
		{
			TryGetComponent(out rectTransform);
			TryGetComponent(out canvasGroup);
		}

		private void Awake()
		{
			UpdateNavParent();
		}

		public void UpdateNavParent()
		{
			parentView = GetComponentInParent<NavPagesParent>(true);
		}

		public void NavigateHere() => parentView.GoToPage(this);
		public void GoBack() => parentView.GoBack();
	}
}