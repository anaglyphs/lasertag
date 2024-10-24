using System.Collections.Generic;
using UnityEngine;



namespace Anaglyph.XRTemplate
{
    public class InteractorLineVisualEnabler : MonoBehaviour
    {
        private static List<InteractorLineVisualEnabler> lineVisualEnablers = new();

		private static void UpdateLineVisuals()
		{
			bool visible = lineVisualEnablers.Count > 0;

			UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual[] visuals = FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>(true);

			foreach (var visual in visuals)
			{
				visual.enabled = visible;
			}
		}

		private void OnEnable()
		{
			lineVisualEnablers.Add(this);
			UpdateLineVisuals();
		}

		private void OnDisable()
		{
			lineVisualEnablers.Remove(this);
			UpdateLineVisuals();
		}
	}
}
