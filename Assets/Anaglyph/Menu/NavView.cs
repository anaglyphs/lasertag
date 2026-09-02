using System;
using System.Collections.Generic;
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

		private readonly List<NavPage> pages = new();
		private readonly List<NavPage> history = new(5);
		private readonly List<ModalEntry> modals = new();
		private bool built;

		/// <summary>Name of the page shown when this view attaches to a panel.</summary>
		[UxmlAttribute("first-page")]
		public string FirstPageName { get; set; }

		public NavView()
		{
			AddToClassList(ussClassName);
			RegisterCallback<AttachToPanelEvent>(_ => Build());
			RegisterCallback<DetachFromPanelEvent>(_ => Teardown());
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
			Resolve(targetPageIsInHistory);
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
			Resolve(false);
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

			Resolve(true);
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
				Resolve(false);
			}
		}

		private void Teardown()
		{
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

		private void Resolve(bool backward)
		{
			NavPage nextPage = ResolveTop();
			if (nextPage == CurrentPage)
			{
				UpdatePresentation();
				return;
			}

			NavPage previousPage = CurrentPage;
			previousPage?.InvokeNavigatingAway();
			if (backward)
				previousPage?.InvokeNavigatingBack();

			CurrentPage = nextPage;
			UpdatePresentation();
			CurrentPage?.InvokeNavigatingHere();
			Changed.Invoke(CurrentPage);
		}

		private void UpdatePresentation()
		{
			foreach (NavPage page in pages)
			{
				SetPageVisible(page, page == CurrentPage);
				page.UpdateBackButtons(history.Count > 1);
			}
		}

		private static void SetPageVisible(NavPage page, bool visible)
		{
			page.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
