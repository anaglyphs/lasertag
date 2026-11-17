using UnityEngine.UIElements;

namespace Anaglyph.Menu
{
	/// <summary>
	/// A scroll view whose direction a style sheet chooses: <c>--scroll-direction: horizontal</c> or
	/// <c>vertical</c>. Without the property it keeps its <c>mode</c> attribute.
	/// </summary>
	[UxmlElement]
	public sealed partial class StyleDirectedScrollView : ScrollView
	{
		private static readonly CustomStyleProperty<string> scrollDirectionProperty = new("--scroll-direction");

		public StyleDirectedScrollView()
		{
			RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
		}

		private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
		{
			if (!evt.customStyle.TryGetValue(scrollDirectionProperty, out string direction)) return;
			ScrollViewMode styledMode = direction.Trim() == "horizontal" ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical;
			if (mode != styledMode) mode = styledMode;
		}
	}
}
