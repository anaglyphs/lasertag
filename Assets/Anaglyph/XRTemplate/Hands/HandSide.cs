using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-1000)]
	public class HandSide : MonoBehaviour
	{
		public bool isRight;
		public XRNode node { get; private set; }
		public UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor rayInteractor { get; private set; }

		private void Awake()
		{
			node = isRight ? XRNode.RightHand : XRNode.LeftHand;

			var rayInteractors = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true);
			foreach (var interactor in rayInteractors)
			{
				if (((XRController)interactor.xrController).controllerNode == node)
				{
					rayInteractor = interactor;
					break;
				}
			}
		}

	}
}
