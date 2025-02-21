using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Anaglyph.Menu
{
    public class LineVisualEnabler : MonoBehaviour
    {
        private static List<LineVisualEnabler> lineVisualEnablers = new();

		private static void UpdateLineVisuals()
		{
			bool visible = lineVisualEnablers.Count > 0;

			XRInteractorLineVisual[] visuals = FindObjectsByType<XRInteractorLineVisual>(FindObjectsSortMode.None);

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
