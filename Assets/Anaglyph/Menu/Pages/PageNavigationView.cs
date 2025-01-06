using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.Menu
{
	public class PageNavigationView : MonoBehaviour
	{
		private List<NavPage> history = new(5);
		public List<NavPage> History => history;

		private NavPage previousPage;
		private NavPage currentPage;
		public NavPage CurrentPage => currentPage;

		private RectTransform rectTransform;

		private bool transitioning;
		private double transitionStartTime;
		private bool goingBack;

		public float transitionLengthSeconds = 0.3f;
		public AnimationCurve normalizedTransitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

		[SerializeField] private NavPage firstPageActive;

		private void OnValidate()
		{
			firstPageActive = GetComponentInChildren<NavPage>(true);
		}

		private void Awake()
		{
			rectTransform = GetComponent<RectTransform>();
		}

		private void Start()
		{
			StopTransition();
			GoToPage(firstPageActive);
		}

		public void GoToPage(NavPage targetPage)
		{
			if(targetPage == null)
				throw new Exception("Target page should not be null");

			if (targetPage.transform.parent != transform)
				throw new Exception("Target page isn't a child of this page navigation viewer");

			if (targetPage == currentPage)
				return;

			int targetPageHistoryIndex = history.IndexOf(targetPage);
			bool targetPageIsInHistory = targetPageHistoryIndex != -1;

			if(!targetPageIsInHistory)
				history.Add(targetPage);
			else
				history.RemoveRange(targetPageHistoryIndex + 1, history.Count - targetPageHistoryIndex - 1);

			StartTransition(targetPageIsInHistory, currentPage, targetPage);
		}
		
		public void GoBack()
		{
			if (history.Count < 2) return;

			GoToPage(history[history.Count - 2]);
		}

		private void StartTransition(bool backward, NavPage fromPage, NavPage toPage)
		{
			if (toPage == null || fromPage == null) {
				currentPage = toPage;
				currentPage.CanvasGroup.interactable = true;
				StopTransition();
				return;
			}

			StopTransition();

			previousPage = fromPage;
			currentPage = toPage;

			previousPage.CanvasGroup.interactable = false;
			currentPage.CanvasGroup.interactable = true;

			currentPage.gameObject.SetActive(true);

			transitioning = true;
			transitionStartTime = Time.time;
			goingBack = backward;
		}

		private void OnDisable()
		{
			StopTransition();
		}

		private void DeactivateAllOtherObjects()
		{
			int currentIndex = currentPage != null ? currentPage.transform.GetSiblingIndex() : -1;

			for (int i = 0; i < transform.childCount; i++)
			{
				if(i != currentIndex)
					transform.GetChild(i).gameObject.SetActive(false);
			}
		}

		private void StopTransition()
		{
			transitioning = false;
			transitionStartTime = -transitionLengthSeconds;

			DeactivateAllOtherObjects();

			if (currentPage != null)
			{
				currentPage.gameObject.SetActive(true);
				currentPage.RectTransform.anchoredPosition = new Vector2(0, 0);
			}
		}

		private void Update()
		{
			if (!transitioning) return;

			double transitionTime = Time.time - transitionStartTime;
			float transitionNormalized = (float)transitionTime / transitionLengthSeconds;

			//float offset = Mathf.Lerp(0, rectTransform.rect.width, transitionNormalized);
			float offset = normalizedTransitionCurve.Evaluate(transitionNormalized) * rectTransform.rect.width;

			float prevPageOffset = -offset;
			float currentPageOffset = rectTransform.rect.width - offset;

			if(goingBack)
			{
				prevPageOffset *= -1;
				currentPageOffset *= -1;
			}
			
			previousPage.RectTransform.anchoredPosition = new Vector2(prevPageOffset, 0);
			currentPage.RectTransform.anchoredPosition = new Vector2(currentPageOffset, 0);

			if(transitionNormalized > 1)
				StopTransition();
		}

#if UNITY_EDITOR

		[InitializeOnLoad]
		static class PageNavigationEditorHelper
		{
			static PageNavigationEditorHelper()
			{
				Selection.selectionChanged -= OnEditorSelectionChange;
				Selection.selectionChanged += OnEditorSelectionChange;
			}

			private static void OnEditorSelectionChange()
			{
				GameObject selected = Selection.activeGameObject;
				Transform parent = selected?.transform.parent;

				if (parent?.GetComponent<PageNavigationView>() == null)
					return;

				for (int i = 0; i < parent.childCount; i++)
				{
					GameObject g = parent.GetChild(i).gameObject;

					g.SetActive(g == selected);
				}

				if (selected != null && selected.transform.parent == parent)
				{
					for (int i = 0; i < parent.childCount; i++)
					{
						GameObject g = parent.GetChild(i).gameObject;

						g.SetActive(g == selected);
					}
				}
			}
		}

#endif

	}
}