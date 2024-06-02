using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;

namespace Anaglyph.Menu
{
	[MovedFrom(false, null, "ImmediatePressButton", "MenuXR")]
	public class ButtonActOnPress : Button, IPointerDownHandler
	{

		void IPointerDownHandler.OnPointerDown(PointerEventData eventData)
		{
			if (!IsActive() || !IsInteractable())
				return;

			UISystemProfilerApi.AddMarker("Button.onClick", this);
			onClick.Invoke();
		}

		public override void OnPointerClick(PointerEventData eventData)
		{

		}
	}
}
