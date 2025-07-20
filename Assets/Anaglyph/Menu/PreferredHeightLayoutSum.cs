using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	public class LayoutMinHeightSum : UIBehaviour, ILayoutElement
	{
		float ILayoutElement.minWidth => throw new System.NotImplementedException();

		float ILayoutElement.preferredWidth => throw new System.NotImplementedException();

		float ILayoutElement.flexibleWidth => throw new System.NotImplementedException();

		float ILayoutElement.minHeight => throw new System.NotImplementedException();

		float ILayoutElement.preferredHeight => throw new System.NotImplementedException();

		float ILayoutElement.flexibleHeight => throw new System.NotImplementedException();

		int ILayoutElement.layoutPriority => throw new System.NotImplementedException();

		void ILayoutElement.CalculateLayoutInputHorizontal()
		{
			throw new System.NotImplementedException();
		}

		void ILayoutElement.CalculateLayoutInputVertical()
		{
			throw new System.NotImplementedException();
		}
	}
}
