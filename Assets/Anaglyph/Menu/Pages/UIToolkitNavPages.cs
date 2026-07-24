using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Anaglyph.Menu.UIToolkit
{
	public sealed class UIToolkitNavPages : IDisposable
	{
		private readonly struct ModalEntry
		{
			public readonly UIToolkitNavPage page;
			public readonly int priority;
			public readonly UIToolkitNavPage returnTo;

			public ModalEntry(UIToolkitNavPage page, int priority, UIToolkitNavPage returnTo)
			{
				this.page = page;
				this.priority = priority;
				this.returnTo = returnTo;
			}
		}

		private readonly VisualElement pageContainer;
		private readonly List<UIToolkitNavPage> pages = new();
		private readonly List<UIToolkitNavPage> history = new(5);
		private readonly List<ModalEntry> modals = new();

		public UIToolkitNavPages(VisualElement pageContainer)
		{
			this.pageContainer = pageContainer ??
				throw new ArgumentNullException(nameof(pageContainer));
		}

		public IReadOnlyList<UIToolkitNavPage> History => history;
		public UIToolkitNavPage CurrentPage { get; private set; }

		public event Action<UIToolkitNavPage> Changed = delegate { };

		public UIToolkitNavPage AddPage(
			string elementName,
			bool showBackButton = true,
			bool modalUserDismissible = false)
		{
			VisualElement element = pageContainer.Q<VisualElement>(elementName);
			if (element == null)
				throw new InvalidOperationException(
					$"Navigation page '{elementName}' was not found below '{pageContainer.name}'.");

			if (element.parent != pageContainer)
				throw new InvalidOperationException(
					$"Navigation page '{elementName}' must be a direct child of '{pageContainer.name}'.");

			UIToolkitNavPage page =
				new(this, element, showBackButton, modalUserDismissible);
			pages.Add(page);
			SetPageVisible(page, false);
			return page;
		}

		public void Start(UIToolkitNavPage firstPage)
		{
			ValidatePage(firstPage);

			if (history.Count != 0)
				throw new InvalidOperationException("This navigation view has already been started.");

			history.Add(firstPage);
			Resolve(false);
		}

		public void GoToPage(UIToolkitNavPage targetPage)
		{
			ValidatePage(targetPage);
			bool targetPageIsInHistory = PushOrTruncateHistory(targetPage);
			Resolve(targetPageIsInHistory);
		}

		public void GoBack()
		{
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
			UIToolkitNavPage page,
			int priority = 0,
			UIToolkitNavPage returnTo = null)
		{
			ValidatePage(page);
			if (returnTo != null)
				ValidatePage(returnTo);

			modals.RemoveAll(entry => entry.page == page);
			modals.Add(new ModalEntry(page, priority, returnTo));
			Resolve(false);
		}

		public void DismissModal(UIToolkitNavPage page)
		{
			int index = modals.FindIndex(entry => entry.page == page);
			if (index == -1)
				return;

			UIToolkitNavPage returnTo = modals[index].returnTo;
			modals.RemoveAt(index);

			if (returnTo != null)
				PushOrTruncateHistory(returnTo);

			Resolve(true);
		}

		public void SetModalPresented(
			UIToolkitNavPage page,
			bool present,
			int priority = 0,
			UIToolkitNavPage returnTo = null)
		{
			if (present)
				PresentModal(page, priority, returnTo);
			else
				DismissModal(page);
		}

		public void Dispose()
		{
			foreach (UIToolkitNavPage page in pages)
				page.Dispose();

			pages.Clear();
			history.Clear();
			modals.Clear();
			CurrentPage = null;
		}

		private void ValidatePage(UIToolkitNavPage page)
		{
			if (page == null)
				throw new ArgumentNullException(nameof(page));

			if (page.ParentView != this || !pages.Contains(page))
				throw new InvalidOperationException(
					"The target page does not belong to this navigation view.");
		}

		private bool PushOrTruncateHistory(UIToolkitNavPage targetPage)
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

		private UIToolkitNavPage ResolveTop()
		{
			int topModal = TopModalIndex();
			if (topModal != -1)
				return modals[topModal].page;

			return history.Count > 0 ? history[^1] : null;
		}

		private void Resolve(bool backward)
		{
			UIToolkitNavPage nextPage = ResolveTop();
			if (nextPage == CurrentPage)
			{
				UpdatePresentation();
				return;
			}

			UIToolkitNavPage previousPage = CurrentPage;
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
			foreach (UIToolkitNavPage page in pages)
			{
				SetPageVisible(page, page == CurrentPage);
				page.UpdateBackButton(history.Count > 1);
			}
		}

		private static void SetPageVisible(UIToolkitNavPage page, bool visible)
		{
			page.Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
		}
	}
}
