using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Anaglyph.XRTemplate
{

	// TODO: find a better way to do this
	public class DisableOverUI : MonoBehaviour
	{
		[SerializeField] private Behaviour[] behaviours;
		[SerializeField] private XRRayInteractor rayInteractor;

		private bool overUI = true;

		private void Update()
		{
			bool newOverUI = rayInteractor.IsOverUIGameObject();
			if (newOverUI != overUI) {
				overUI = newOverUI;
				foreach (Behaviour behaviour in behaviours)
				{
					behaviour.enabled = !overUI;
				}
			}
		}
	}
}
