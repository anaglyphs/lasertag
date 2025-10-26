using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Operator
{
	public class PageGroup : VisualElement
	{
		public VisualElement ActiveElement { get; private set; }

		public new void Add(VisualElement element)
		{
			base.Add(element);

			if (childCount == 0)
				SetActiveElement(element);
			else
				element.style.display = DisplayStyle.None;
		}
		
		public void SetActiveElement(VisualElement element)
		{
			if ((element != null && element.parent != this) || ActiveElement == element)
				return;
			
			if(ActiveElement != null)
				ActiveElement.style.display = DisplayStyle.None;
			
			ActiveElement = element;
			
			if (ActiveElement != null) 
				ActiveElement.style.display = DisplayStyle.Flex;

			MarkDirtyRepaint();
		}
	}
}