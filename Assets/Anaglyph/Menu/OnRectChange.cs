using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Anaglyph.Lasertag.UI
{
	[ExecuteAlways]
	public class RectEvents : UIBehaviour
	{
		private bool started = false;

		protected override void Start()
		{
			started = true;
		}

		public UnityEvent OnChange = new();
		protected override void OnRectTransformDimensionsChange()
		{
			if (started)
			{
				OnChange.Invoke();
			}
		}
	}
}
