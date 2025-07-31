using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Anaglyph.Menu
{
	public class HandedButton : MonoBehaviour, IPointerDownHandler
	{
		private Button button;
		public UnityEvent<bool> onClickIsRight;

		private void Awake()
		{
			button = GetComponent<Button>();
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (!button.interactable || !button.enabled || !enabled)
				return;

			if (eventData is TrackedDeviceEventData trackedDeviceEventData)
				if (trackedDeviceEventData.interactor is XRBaseInputInteractor xrInteractor)
					onClickIsRight.Invoke(xrInteractor.handedness == InteractorHandedness.Right);
		}
	}
}