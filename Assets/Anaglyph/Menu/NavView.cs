using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Container for <see cref="NavPage"/> children. Shows one page at a time,
	/// keeps a back history, and stacks modals on top by priority.
	/// </summary>
	[UxmlElement]
	public sealed partial class NavView : VisualElement
	{
		private enum TransitionKind { None, Forward, Back, PresentModal, DismissModal }

		private readonly struct ModalEntry
		{
			public readonly NavPage page;
			public readonly int priority;
			public readonly NavPage returnTo;

			public ModalEntry(NavPage page, int priority, NavPage returnTo)
			{
				this.page = page;
				this.priority = priority;
				this.returnTo = returnTo;
			}
		}

		public static readonly string ussClassName = "nav-view";
		private static readonly CustomStyleProperty<float> transitionDurationProperty = new("--nav-transition-duration-ms");

		private readonly List<NavPage> pages = new();
		private readonly List<NavPage> history = new(5);
		private readonly List<ModalEntry> modals = new();
		private bool built;
		private int navigationVersion;
		private readonly IVisualElementScheduledItem transitionTick;
		private readonly List<VisualElement> suspendedFocus = new();
		private NavPage outgoingPage;
		private Vector2 incomingStart;
		private Vector2 outgoingEnd;
		private double transitionStart;
		private float transitionDuration;
		private Action restoreTransitionStyles;
		private const string modalTransitionClass = "nav-page--modal-transition";

		/// <summary>Name of the page shown when this view attaches to a panel.</summary>
		[UxmlAttribute("first-page")]
		public string FirstPageName { get; set; }

		[UxmlAttribute("transition-duration-ms")]
		public int TransitionDurationMs { get; set; } = 220;

		public NavView()
		{
			AddToClassList(ussClassName);
			transitionTick = schedule.Execute(AdvanceTransition).Every(0);
			transitionTick.Pause();
			RegisterCallback<AttachToPanelEvent>(_ => Build());
			RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
			RegisterCallback<PointerDownEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<PointerUpEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<PointerMoveEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<ClickEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<MouseDownEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<MouseUpEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<MouseMoveEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<ContextClickEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<WheelEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<KeyDownEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<KeyUpEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<NavigationMoveEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<NavigationSubmitEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<NavigationCancelEvent>(FilterPageInput, TrickleDown.TrickleDown);
			RegisterCallback<PointerCaptureEvent>(OnPointerCaptured, TrickleDown.TrickleDown);
			RegisterCallback<FocusInEvent>(OnPageFocused, TrickleDown.TrickleDown);
		}

		public IReadOnlyList<NavPage> History => history;
		public NavPage CurrentPage { get; private set; }

		public event Action<NavPage> Changed = delegate { };

		/// <summary>Finds the single navigation view in a menu's visual tree.</summary>
		public static NavView RequireIn(VisualElement root)
		{
			NavView view = root.Q<NavView>();
			if (view == null)
				throw new InvalidOperationException(
					"A NavView was not found in the visual tree.");

			return view;
		}

		public NavPage GetPage(string pageName)
		{
			NavPage page = this.Q<NavPage>(pageName);
			if (page == null)
				throw new InvalidOperationException(
					$"Navigation page '{pageName}' was not found below '{name}'.");

			return page;
		}

		public void GoToPage(string pageName)
		{
			GoToPage(GetPage(pageName));
		}

		public void GoToPage(NavPage targetPage)
		{
			EnsureBuilt();
			ValidatePage(targetPage);
			bool targetPageIsInHistory = PushOrTruncateHistory(targetPage);
			Resolve(targetPageIsInHistory ? TransitionKind.Back : TransitionKind.Forward);
		}

		public void GoBack()
		{
			EnsureBuilt();

			int topModal = TopModalIndex();
			if (topModal != -1)
			{
				if (modals[topModal].page.ModalUserDismissible)
					DismissModal(modals[topModal].page);

				return;
			}

			if (history.Count >= 2)
				GoToPage(history[^2]);
		}

		public void PresentModal(
			NavPage page,
			int priority = 0,
			NavPage returnTo = null)
		{
			EnsureBuilt();
			ValidatePage(page);
			if (returnTo != null)
				ValidatePage(returnTo);

			modals.RemoveAll(entry => entry.page == page);
			modals.Add(new ModalEntry(page, priority, returnTo));
			Resolve(TransitionKind.PresentModal);
		}

		public void DismissModal(NavPage page)
		{
			EnsureBuilt();

			int index = modals.FindIndex(entry => entry.page == page);
			if (index == -1)
				return;

			NavPage returnTo = modals[index].returnTo;
			modals.RemoveAt(index);

			if (returnTo != null)
				PushOrTruncateHistory(returnTo);

			Resolve(TransitionKind.DismissModal);
		}

		public void SetModalPresented(
			NavPage page,
			bool present,
			int priority = 0,
			NavPage returnTo = null)
		{
			if (present)
				PresentModal(page, priority, returnTo);
			else
				DismissModal(page);
		}

		private void EnsureBuilt()
		{
			if (!built)
				Build();
		}

		// Collects the pages and validates every navigation target in one pass, so
		// a page or button that names something missing fails at startup rather
		// than the first time somebody presses it.
		private void Build()
		{
			Teardown();
			built = true;

			this.Query<NavPage>().ForEach(page =>
			{
				if (page.GetFirstAncestorOfType<NavView>() != this)
					return;

				if (page.hierarchy.parent != this)
					throw new InvalidOperationException(
						$"Navigation page '{page.name}' must be a direct child of '{name}'.");

				page.Initialize(this);
				pages.Add(page);
				SetPageVisible(page, false);
			});

			this.Query<NavButton>().ForEach(button =>
			{
				if (button.GetFirstAncestorOfType<NavView>() == this)
					button.ValidateTarget(this);
			});

			if (!string.IsNullOrEmpty(FirstPageName))
			{
				history.Add(GetPage(FirstPageName));
				Resolve(TransitionKind.None);
			}
		}

		private void Teardown()
		{
			++navigationVersion;
			FinishTransition();
			foreach (NavPage page in pages)
				page.Initialize(null);

			built = false;
			pages.Clear();
			history.Clear();
			modals.Clear();
			CurrentPage = null;
		}

		private void ValidatePage(NavPage page)
		{
			if (page == null)
				throw new ArgumentNullException(nameof(page));

			if (page.ParentView != this || !pages.Contains(page))
				throw new InvalidOperationException(
					"The target page does not belong to this navigation view.");
		}

		private bool PushOrTruncateHistory(NavPage targetPage)
		{
			int targetIndex = history.IndexOf(targetPage);
			bool targetIsInHistory = targetIndex != -1;

			if (!targetIsInHistory)
			{
				history.Add(targetPage);
			}
			else
			{
				int removeCount = history.Count - targetIndex - 1;
				if (removeCount > 0)
					history.RemoveRange(targetIndex + 1, removeCount);
			}

			return targetIsInHistory;
		}

		private int TopModalIndex()
		{
			if (modals.Count == 0)
				return -1;

			int top = 0;
			for (int i = 1; i < modals.Count; i++)
				if (modals[i].priority >= modals[top].priority)
					top = i;

			return top;
		}

		private NavPage ResolveTop()
		{
			int topModal = TopModalIndex();
			if (topModal != -1)
				return modals[topModal].page;

			return history.Count > 0 ? history[^1] : null;
		}

		private void Resolve(TransitionKind transition)
		{
			NavPage nextPage = ResolveTop();
			if (nextPage == CurrentPage)
			{
				UpdatePresentation();
				return;
			}

			FinishTransition();
			int version = ++navigationVersion;
			NavPage previousPage = CurrentPage;
			previousPage?.InvokeNavigatingAway();
			if (version != navigationVersion) return;
			if (transition is TransitionKind.Back or TransitionKind.DismissModal)
				previousPage?.InvokeNavigatingBack();
			if (version != navigationVersion) return;

			CurrentPage = nextPage;
			StartTransition(previousPage, nextPage, transition);
			UpdatePresentation();
			if (version != navigationVersion) return;
			CurrentPage?.InvokeNavigatingHere();
			if (version != navigationVersion) return;
			Changed.Invoke(CurrentPage);
		}

		private void UpdatePresentation()
		{
			foreach (NavPage page in pages)
			{
				SetPageVisible(page, page == CurrentPage || page == outgoingPage);
				if (page != outgoingPage) page.UpdateBackButtons(history.Count > 1);
			}
		}

		private void StartTransition(NavPage previous, NavPage next, TransitionKind kind)
		{
			float durationMs = customStyle.TryGetValue(transitionDurationProperty, out float styledDuration)
				? styledDuration : TransitionDurationMs;
			if (previous == null || next == null || kind == TransitionKind.None ||
				durationMs <= 0 || panel?.contextType != ContextType.Player ||
				!IsDisplayed(this) || !IsDisplayed(previous) ||
				!(previous.layout.width > 0) || !(previous.layout.height > 0))
				return;

			previous.Query<NavView>().ForEach(view => view.FinishTransition());
			Rect bounds = previous.layout;
			var previousLayout = (previous.style.position, previous.style.left, previous.style.top,
				previous.style.width, previous.style.height, previous.style.translate);
			var nextTranslate = next.style.translate;
			var viewLayout = (style.minWidth, style.maxWidth, style.minHeight, style.maxHeight, style.overflow);
			NavPage front = kind == TransitionKind.DismissModal ? previous : next;
			int frontIndex = hierarchy.IndexOf(front);
			bool modal = kind is TransitionKind.PresentModal or TransitionKind.DismissModal;
			bool hadModalClass = front.ClassListContains(modalTransitionClass);
			restoreTransitionStyles = () =>
			{
				(previous.style.position, previous.style.left, previous.style.top,
					previous.style.width, previous.style.height, previous.style.translate) = previousLayout;
				next.style.translate = nextTranslate;
				(style.minWidth, style.maxWidth, style.minHeight, style.maxHeight, style.overflow) = viewLayout;
				front.EnableInClassList(modalTransitionClass, hadModalClass);
				if (front.parent == this && frontIndex < hierarchy.childCount - 1)
					front.PlaceBehind(hierarchy[frontIndex]);
			};

			outgoingPage = previous;
			style.minWidth = layout.width;
			style.maxWidth = layout.width;
			style.minHeight = layout.height;
			style.maxHeight = layout.height;
			style.overflow = Overflow.Hidden;
			previous.style.position = Position.Absolute;
			previous.style.left = bounds.x - resolvedStyle.borderLeftWidth - previous.resolvedStyle.marginLeft;
			previous.style.top = bounds.y - resolvedStyle.borderTopWidth - previous.resolvedStyle.marginTop;
			previous.style.width = bounds.width;
			previous.style.height = bounds.height;
			front.BringToFront();
			if (modal) front.AddToClassList(modalTransitionClass);

			incomingStart = kind switch
			{
				TransitionKind.Forward => new Vector2(bounds.width, 0),
				TransitionKind.Back => new Vector2(-bounds.width, 0),
				TransitionKind.PresentModal => new Vector2(0, bounds.height),
				_ => Vector2.zero
			};
			outgoingEnd = kind switch
			{
				TransitionKind.Forward => new Vector2(-bounds.width, 0),
				TransitionKind.Back => new Vector2(bounds.width, 0),
				TransitionKind.DismissModal => new Vector2(0, bounds.height),
				_ => Vector2.zero
			};
			SetPageVisible(next, true);
			SetTranslation(previous, Vector2.zero);
			SetTranslation(next, incomingStart);
			transitionDuration = durationMs / 1000f;
			transitionStart = Time.realtimeSinceStartupAsDouble;
			transitionTick.Resume();

			bool transferFocus = focusController?.focusedElement is VisualElement focused &&
				(previous == focused || previous.Contains(focused));
			SuspendFocus(previous);
			ReleasePagePointers(previous);
			if (transferFocus) next.Focus();
		}

		private static bool IsDisplayed(VisualElement element)
		{
			for (; element != null; element = element.parent)
				if (!element.visible || element.resolvedStyle.display == DisplayStyle.None)
					return false;
			return true;
		}

		private void AdvanceTransition()
		{
			if (outgoingPage == null) return;
			float progress = (float)((Time.realtimeSinceStartupAsDouble - transitionStart) / transitionDuration);
			if (progress >= 1 || !IsDisplayed(this))
			{
				FinishTransition();
				return;
			}

			float eased = 1 - Mathf.Pow(1 - progress, 3);
			SetTranslation(outgoingPage, outgoingEnd * eased);
			SetTranslation(CurrentPage, incomingStart * (1 - eased));
		}

		private void FinishTransition()
		{
			transitionTick.Pause();
			if (outgoingPage == null) return;
			SetPageVisible(outgoingPage, false);
			restoreTransitionStyles?.Invoke();
			restoreTransitionStyles = null;
			outgoingPage = null;
			foreach (VisualElement element in suspendedFocus)
				element.focusable = true;
			suspendedFocus.Clear();
		}

		private static void SetTranslation(NavPage page, Vector2 offset)
		{
			page.style.translate = new Translate(offset.x, offset.y);
		}

		private void SuspendFocus(VisualElement element)
		{
			if (element.focusable)
			{
				suspendedFocus.Add(element);
				element.focusable = false;
			}
			for (int i = 0; i < element.hierarchy.childCount; i++)
				SuspendFocus(element.hierarchy[i]);
		}

		private void ReleasePagePointers(NavPage page)
		{
			for (int id = 0; id < PointerId.maxPointers; id++)
				if (panel.GetCapturingElement(id) is VisualElement captured &&
					(captured == page || page.Contains(captured)))
					captured.ReleasePointer(id);
		}

		private bool TargetsInactivePage(IEventHandler target)
		{
			for (var element = target as VisualElement; element != null && element != this; element = element.parent)
				if (element is NavPage page && page.ParentView == this)
					return page != CurrentPage;
			return false;
		}

		private void FilterPageInput(EventBase evt)
		{
			if (panel?.contextType != ContextType.Player || !TargetsInactivePage(evt.target)) return;
			focusController?.IgnoreEvent(evt);
			evt.StopImmediatePropagation();
		}

		private void OnPointerCaptured(PointerCaptureEvent evt)
		{
			if (panel?.contextType == ContextType.Player && TargetsInactivePage(evt.target))
				evt.target.ReleasePointer(evt.pointerId);
		}

		private void OnPageFocused(FocusInEvent evt)
		{
			if (panel?.contextType == ContextType.Player && TargetsInactivePage(evt.target))
				CurrentPage?.Focus();
		}

		private static void SetPageVisible(NavPage page, bool visible)
		{
			page.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
