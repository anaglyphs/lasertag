using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EcMenuXR
{
	public class ImmediatePressButton : Button, IPointerDownHandler
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
