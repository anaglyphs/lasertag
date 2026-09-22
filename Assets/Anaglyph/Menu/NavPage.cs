using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// One screen of a <see cref="NavView"/>. Must be a direct child of the view
	/// it belongs to.
	/// </summary>
	[UxmlElement]
	public sealed partial class NavPage : VisualElement
	{
		public static readonly string ussClassName = "nav-page";

		private readonly List<NavButton> backButtons = new();

		public NavPage()
		{
			AddToClassList(ussClassName);
			usageHints |= UsageHints.DynamicTransform;
			focusable = true;
			tabIndex = -1;
		}

		/// <summary>Whether this page's back buttons appear once there is history to go back to.</summary>
		[UxmlAttribute("show-back-button")]
		public bool ShowBackButton { get; set; } = true;

		/// <summary>Whether a back press dismisses this page while it is presented as a modal.</summary>
		[UxmlAttribute("modal-user-dismissible")]
		public bool ModalUserDismissible { get; set; }

		public NavView ParentView { get; private set; }

		public event Action NavigatingHere = delegate { };
		public event Action NavigatingAway = delegate { };
		public event Action NavigatingBack = delegate { };

		public void NavigateHere()
		{
			RequireParentView().GoToPage(this);
		}

		public void GoBack()
		{
			RequireParentView().GoBack();
		}

		internal void Initialize(NavView parentView)
		{
			ParentView = parentView;
			backButtons.Clear();

			if (parentView == null)
				return;

			this.Query<NavButton>().ForEach(button =>
			{
				if (button.IsBackButton && button.GetFirstAncestorOfType<NavPage>() == this)
					backButtons.Add(button);
			});
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

		internal void UpdateBackButtons(bool hasHistory)
		{
			DisplayStyle display = ShowBackButton && hasHistory
				? DisplayStyle.Flex
				: DisplayStyle.None;

			foreach (NavButton button in backButtons)
				button.style.display = display;
		}

		private NavView RequireParentView()
		{
			if (ParentView == null)
				throw new InvalidOperationException(
					$"Navigation page '{name}' does not belong to a NavView.");

			return ParentView;
		}
	}
}
