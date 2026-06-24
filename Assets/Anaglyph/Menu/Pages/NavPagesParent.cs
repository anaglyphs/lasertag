using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Menu
{
	[HideSiblingsOfSelectedChild]
	public class NavPagesParent : MonoBehaviour
	{
		private List<NavPage> history = new(5);
		public List<NavPage> History => history;

		private readonly List<ModalEntry> modals = new();

		private readonly struct ModalEntry
		{
			public readonly NavPage page;
			public readonly int priority;
			public readonly NavPage returnTo; // (optional)

			public ModalEntry(NavPage page, int priority, NavPage returnTo)
			{
				this.page = page;
				this.priority = priority;
				this.returnTo = returnTo;
			}
		}

		private NavPage previousPage;
		private NavPage currentPage;
		public NavPage CurrentPage => currentPage;

		[SerializeField] private RectTransform rectTransform;

		private bool transitioning;
		private double transitionStartTime;
		private bool goingBack;

		public float transitionLengthSeconds = 0.3f;
		public AnimationCurve normalizedTransitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

		[SerializeField] private NavPage firstPageActive;

		public event Action<NavPage> Changed = delegate { };
		[SerializeField] private UnityEvent<NavPage> onPageChange = new();

		private void OnValidate()
		{
			rectTransform = GetComponent<RectTransform>();
			firstPageActive = GetComponentInChildren<NavPage>(true);
		}

		private void Awake()
		{
			Changed += onPageChange.Invoke;
		}

		private void Start()
		{
			StopTransition();
			GoToPage(firstPageActive);
		}

		private void ValidatePage(NavPage page)
		{
			if (page == null)
				throw new Exception("Target page should not be null");

			if (page.transform.parent != transform)
				throw new Exception("Target page isn't a child of this page navigation viewer");
		}

		// --- User navigation (history layer) ---

		public void GoToPage(NavPage targetPage)
		{
			ValidatePage(targetPage);

			bool targetPageIsInHistory = PushOrTruncateHistory(targetPage);

			Resolve(targetPageIsInHistory);
		}

		public void GoBack()
		{
			// A modal owns the screen: never navigate the history layer underneath it.
			// A user-dismissible modal (e.g. a dialog) closes on Back instead.
			int topModal = TopModalIndex();
			if (topModal != -1)
			{
				if (modals[topModal].page.modalUserDismissible)
					DismissModal(modals[topModal].page);
				return;
			}

			if (history.Count < 2) return;

			GoToPage(history[history.Count - 2]);
		}

		// Adds the page to history (forward) or truncates back to it (backward).
		// Returns true if the page was already in history (i.e. this is a "back" move).
		private bool PushOrTruncateHistory(NavPage targetPage)
		{
			int targetPageHistoryIndex = history.IndexOf(targetPage);
			bool targetPageIsInHistory = targetPageHistoryIndex != -1;

			if (!targetPageIsInHistory)
				history.Add(targetPage);
			else
				history.RemoveRange(targetPageHistoryIndex + 1, history.Count - targetPageHistoryIndex - 1);

			return targetPageIsInHistory;
		}

		// --- State-driven presentation (modal layer) ---

		// Present a mode/dialog page on top of the navigation layer. Higher priority
		// wins when several are active at once; ties resolve to the most recently
		// presented. returnTo (optional) is the nav page revealed when this dismisses.
		public void PresentModal(NavPage page, int priority = 0, NavPage returnTo = null)
		{
			ValidatePage(page);

			modals.RemoveAll(m => m.page == page);
			modals.Add(new ModalEntry(page, priority, returnTo));

			Resolve(false);
		}

		public void DismissModal(NavPage page)
		{
			int idx = modals.FindIndex(m => m.page == page);
			if (idx == -1) return;

			NavPage returnTo = modals[idx].returnTo;
			modals.RemoveAt(idx);

			// Point the nav layer at the requested landing page before revealing it,
			// so dismissing a mode never strands the user on a phantom page.
			if (returnTo != null)
				PushOrTruncateHistory(returnTo);

			Resolve(true);
		}

		// Convenience for state callbacks: bind a mode page directly to a bool.
		public void SetModalPresented(NavPage page, bool present, int priority = 0, NavPage returnTo = null)
		{
			if (present)
				PresentModal(page, priority, returnTo);
			else
				DismissModal(page);
		}

		// --- Resolution ---

		// Index of the highest-priority active modal, or -1 if none.
		// Ties go to the most recently presented (later index), giving dialogs LIFO order.
		private int TopModalIndex()
		{
			if (modals.Count == 0) return -1;

			int top = 0;
			for (int i = 1; i < modals.Count; i++)
				if (modals[i].priority >= modals[top].priority)
					top = i;

			return top;
		}

		// The page that should currently be on screen: the top modal, else the
		// current navigation page.
		private NavPage ResolveTop()
		{
			int topModal = TopModalIndex();
			if (topModal != -1)
				return modals[topModal].page;

			return history.Count > 0 ? history[^1] : null;
		}

		// Recompute what should be shown and transition to it if it changed.
		private void Resolve(bool backward)
		{
			NavPage top = ResolveTop();

			if (top == currentPage)
				return;

			StartTransition(backward, currentPage, top);
			Changed.Invoke(top);
		}

		private void StartTransition(bool backward, NavPage fromPage, NavPage toPage)
		{
			if (toPage == null || fromPage == null)
			{
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

			currentPage.NavigatingHere.Invoke();
			previousPage.NavigatingAway.Invoke();
			if (goingBack)
				previousPage.NavigatingBack.Invoke();
		}

		private void OnDisable()
		{
			StopTransition();
		}

		private void DeactivateAllOtherObjects()
		{
			int currentIndex = currentPage != null ? currentPage.transform.GetSiblingIndex() : -1;

			for (int i = 0; i < transform.childCount; i++)
				if (i != currentIndex)
					transform.GetChild(i).gameObject.SetActive(false);
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

			if (goingBack)
			{
				prevPageOffset *= -1;
				currentPageOffset *= -1;
			}

			previousPage.RectTransform.anchoredPosition = new Vector2(prevPageOffset, 0);
			currentPage.RectTransform.anchoredPosition = new Vector2(currentPageOffset, 0);

			if (transitionNormalized > 1)
				StopTransition();
		}
	}
}