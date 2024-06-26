using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Anaglyph.LaserTag.UI
{
	[ExecuteAlways]
	public class OnRectChange : UIBehaviour
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
