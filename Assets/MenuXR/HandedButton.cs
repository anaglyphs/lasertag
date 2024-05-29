using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace EcMenuXR
{
	public class HandedButton : MonoBehaviour, IPointerDownHandler
	{
		public UnityEvent<XRNode> onClick;
		public UnityEvent<bool> onClickIsRight;

		public void OnPointerDown(PointerEventData eventData)
		{
			if (eventData is TrackedDeviceEventData trackedDeviceEventData)
			{
				if (trackedDeviceEventData.interactor is XRBaseControllerInteractor xrInteractor)
					if (xrInteractor.xrController is XRController xrController)
						if (xrController.controllerNode == XRNode.LeftHand || xrController.controllerNode == XRNode.RightHand)
						{
							onClick.Invoke(xrController.controllerNode);

							onClickIsRight.Invoke(xrController.controllerNode == XRNode.RightHand);
						}
			} else
			{
				onClick.Invoke(XRNode.RightHand);
				onClickIsRight.Invoke(true);
			}
		}
	}
}