using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Button that navigates its enclosing <see cref="NavView"/>. With a
	/// <c>target</c> it goes to that page; without one it goes back.
	/// </summary>
	[UxmlElement]
	public sealed partial class NavButton : Button
	{
		public static readonly string ussClassName = "nav-button";

		/// <summary>Name of the page to navigate to. Empty means go back.</summary>
		[UxmlAttribute("target")]
		public string TargetPageName { get; set; }

		public NavButton()
		{
			AddToClassList(ussClassName);

			// Assigned here rather than through MakeButtonsActOnPress so that the
			// handler below survives that call, which replaces the manipulator.
			clickable = new PressClickable(Navigate);
		}

		public bool IsBackButton => string.IsNullOrEmpty(TargetPageName);

		private void Navigate()
		{
			NavView view = GetFirstAncestorOfType<NavView>();
			if (view == null)
				return;

			if (IsBackButton)
				view.GoBack();
			else
				view.GoToPage(TargetPageName);
		}

		internal void ValidateTarget(NavView view)
		{
			if (!IsBackButton)
				view.GetPage(TargetPageName);
		}
	}
}
