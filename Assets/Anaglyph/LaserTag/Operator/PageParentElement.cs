using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Anaglyph.LaserTag.Operator
{
	public class PageParentElement : VisualElement
	{
		public VisualElement ActiveElement { get; private set; }

		public PageParentElement()
		{
			RegisterCallback<AttachToPanelEvent>(_ => SetActiveElement(ActiveElement));
		}

		public void SetActiveElement(VisualElement element)
		{
			if ((element != null && element.parent != this) || ActiveElement == element)
				return;
		
			ActiveElement = element;

			foreach (var child in Children())
			{
				child.style.display = child == ActiveElement ? DisplayStyle.Flex : DisplayStyle.None;
			}
			
			MarkDirtyRepaint();
		}
	}
}