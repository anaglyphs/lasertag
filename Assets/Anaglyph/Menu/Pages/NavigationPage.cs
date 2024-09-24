using UnityEngine;

namespace Anaglyph.Menu
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
	public class NavigationPage : SuperAwakeBehavior
    {
        public RectTransform RectTransform { get; private set; }
        public CanvasGroup CanvasGroup { get; private set; }
		public PageNavigationView ParentView { get; private set; }

		protected override void SuperAwake()
		{
			RectTransform = GetComponent<RectTransform>();
			CanvasGroup = GetComponent<CanvasGroup>();
			ParentView = GetComponentInParent<PageNavigationView>(true);
		}

		private void OnEnable()
		{
			if(ParentView.CurrentPage != this)
				ParentView.GoToPage(this);
		}
	}
}
