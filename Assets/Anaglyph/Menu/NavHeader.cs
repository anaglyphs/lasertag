using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// Title bar for a <see cref="NavPage"/>: a back button followed by the page
	/// title. The back button navigates through the enclosing <see cref="NavView"/>,
	/// which shows and hides it according to the page's
	/// <see cref="NavPage.ShowBackButton"/> and the current history.
	/// </summary>
	[UxmlElement]
	public sealed partial class NavHeader : VisualElement
	{
		public static readonly string ussClassName = "nav-header";
		public static readonly string titleUssClassName = "nav-header__title";
		public static readonly string backButtonUssClassName = "nav-header__back-button";

		private readonly Label titleLabel = new();

		[UxmlAttribute("title")]
		public string Title
		{
			get => titleLabel.text;
			set => titleLabel.text = value;
		}

		public NavHeader()
		{
			AddToClassList(ussClassName);

			NavButton backButton = new();// { text = "‹" };
			backButton.AddToClassList(backButtonUssClassName);
			titleLabel.AddToClassList(titleUssClassName);

			hierarchy.Add(backButton);
			hierarchy.Add(titleLabel);
		}
	}
}
