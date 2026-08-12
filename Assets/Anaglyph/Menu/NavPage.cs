using System;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	public sealed class NavPage
	{
		private readonly Button backButton;

		internal NavPage(
			NavView parentView,
			VisualElement root,
			bool showBackButton,
			bool modalUserDismissible)
		{
			ParentView = parentView;
			Root = root;
			ShowBackButton = showBackButton;
			ModalUserDismissible = modalUserDismissible;

			backButton = root.Q<Button>("back-button");
			if (backButton != null)
				backButton.clicked += GoBack;
		}

		public VisualElement Root { get; }
		public NavView ParentView { get; }
		public bool ShowBackButton { get; set; }
		public bool ModalUserDismissible { get; set; }

		public event Action NavigatingHere = delegate { };
		public event Action NavigatingAway = delegate { };
		public event Action NavigatingBack = delegate { };

		public void NavigateHere()
		{
			ParentView.GoToPage(this);
		}

		public void GoBack()
		{
			ParentView.GoBack();
		}

		internal void InvokeNavigatingHere()
		{
			NavigatingHere.Invoke();
		}

		internal void InvokeNavigatingAway()
		{
			NavigatingAway.Invoke();
		}

		internal void InvokeNavigatingBack()
		{
			NavigatingBack.Invoke();
		}

		internal void UpdateBackButton(bool hasHistory)
		{
			if (backButton != null)
				backButton.style.display =
					ShowBackButton && hasHistory ? DisplayStyle.Flex : DisplayStyle.None;
		}

		internal void Dispose()
		{
			if (backButton != null)
				backButton.clicked -= GoBack;
		}
	}
}
