using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;


namespace Anaglyph.XRTemplate
{
    public class InteractorLineVisualEnabler : MonoBehaviour
    {
        private static List<InteractorLineVisualEnabler> lineVisualEnablers = new();

		private static void UpdateLineVisuals()
		{
			bool visible = lineVisualEnablers.Count > 0;

			XRInteractorLineVisual[] visuals = FindObjectsOfType<XRInteractorLineVisual>(true);

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
