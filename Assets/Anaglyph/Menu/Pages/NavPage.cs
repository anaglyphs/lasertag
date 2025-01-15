using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Menu
{
	// [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
	public class NavPage : MonoBehaviour
	{
		[SerializeField] private RectTransform rectTransform;
		[SerializeField] private CanvasGroup canvasGroup;
		public bool showBackButton = true;

		private PageNavigationView parentView;

		public RectTransform RectTransform => rectTransform;
		public CanvasGroup CanvasGroup => canvasGroup;
		public PageNavigationView ParentView => parentView;

		public UnityEvent<bool> OnVisible = new();

		private bool started = false;

		private void OnValidate()
		{
			TryGetComponent(out rectTransform);
			TryGetComponent(out canvasGroup);
		}

		private void Awake()
		{
			parentView = GetComponentInParent<PageNavigationView>(true);
		}

		private void Start()
		{
			started = true;
		}

		public void NavigateHere() => parentView.GoToPage(this);
		public void GoBack() => parentView.GoBack();

		private void OnEnable()
		{
			if (!started) return;

			parentView = GetComponentInParent<PageNavigationView>(true);

			OnVisible.Invoke(gameObject.activeInHierarchy);
		}

		private void OnDisable()
		{
			OnVisible.Invoke(gameObject.activeInHierarchy);
		}
	}
}
