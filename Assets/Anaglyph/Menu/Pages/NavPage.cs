using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif


namespace Anaglyph.Menu
{
	// [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
	public class NavPage : MonoBehaviour
	{
		[SerializeField] private RectTransform rectTransform;
		[SerializeField] private CanvasGroup canvasGroup;
		public bool showBackButton = true;

		private NavPagesParent parentView;

		public RectTransform RectTransform => rectTransform;
		public CanvasGroup CanvasGroup => canvasGroup;
		public NavPagesParent ParentView => parentView;

		public UnityEvent<bool> OnVisible = new();

		private bool started = false;

		private void OnValidate()
		{
			TryGetComponent(out rectTransform);
			TryGetComponent(out canvasGroup);
		}

		private void Awake()
		{
			parentView = GetComponentInParent<NavPagesParent>(true);
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

			parentView = GetComponentInParent<NavPagesParent>(true);

			OnVisible.Invoke(gameObject.activeInHierarchy);
		}

		private void OnDisable()
		{
			OnVisible.Invoke(gameObject.activeInHierarchy);
		}


	}

#if UNITY_EDITOR

	[InitializeOnLoad]
	static class NavPageEditorHelper
	{
		static SceneVisibilityManager Visibility => SceneVisibilityManager.instance;

		static NavPageEditorHelper()
		{
			Selection.selectionChanged += delegate
			{
				GameObject selectedObject = Selection.activeGameObject;

				if (selectedObject == null)
					return;
				
				if (!selectedObject.TryGetComponent(out NavPage navPage))
					return;

				Visibility.Hide(selectedObject.transform.parent.gameObject, true);
				Visibility.Show(selectedObject, true);
			};
		}
	}

#endif

}