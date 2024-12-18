using UnityEngine;

namespace Anaglyph.Menu
{
	[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
	public class NavPage : SuperAwakeBehavior
	{
		[SerializeField] private RectTransform rectTransform;
		[SerializeField] private CanvasGroup canvasGroup;
		public bool showBackButton = true;

		private PageNavigationView parentView;

		public RectTransform RectTransform => rectTransform;
		public CanvasGroup CanvasGroup => canvasGroup;
		public PageNavigationView ParentView => parentView;

		private void OnValidate()
		{
			this.SetComponent(ref rectTransform);
			this.SetComponent(ref canvasGroup);
		}

		protected override void SuperAwake()
		{
			parentView = GetComponentInParent<PageNavigationView>(true);
		}

		public void NavigateHere() => ParentView.GoToPage(this);
		public void GoBack() => parentView.GoBack();

		private void OnEnable()
		{
			parentView = GetComponentInParent<PageNavigationView>(true);

			if (parentView != null && parentView.CurrentPage != this)
				parentView.GoToPage(this);
		}
	}
}
