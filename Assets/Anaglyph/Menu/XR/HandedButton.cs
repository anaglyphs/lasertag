using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.Menu
{
	public class HandedButton : MonoBehaviour, IPointerDownHandler
	{
		public UnityEvent<bool> onClickIsRight;

		public void OnPointerDown(PointerEventData eventData)
		{
			if (eventData is TrackedDeviceEventData trackedDeviceEventData)
				if (trackedDeviceEventData.interactor is XRBaseInputInteractor xrInteractor)
					onClickIsRight.Invoke(xrInteractor.handedness == InteractorHandedness.Right);
		}
	}
}