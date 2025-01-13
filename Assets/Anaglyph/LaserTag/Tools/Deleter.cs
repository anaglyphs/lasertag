using Anaglyph.XRTemplate;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class Deleter : MonoBehaviour
	{
		[SerializeField] private HandedControllerInput trigger = null;
		[SerializeField] private Raycaster raycaster = null;
		[SerializeField] private ObjectBoundsVisual boundsVisual = null;
		[SerializeField] private RaycasterCursor cursorVisual = null;

		private Deletable hoveredDeletable;

		private void Update()
		{
			raycaster.Raycast();

			if(raycaster.PreviousHitObject != raycaster.HitObject)
			{
				hoveredDeletable = raycaster.HitObject?.GetComponentInParent<Deletable>();
				boundsVisual.SetTrackedObject(hoveredDeletable);

				bool objectIsDeletable = hoveredDeletable != null;

				cursorVisual.gameObject.SetActive(objectIsDeletable);
			}

			if(hoveredDeletable != null 
				&& !trigger.TriggerWasDown && trigger.TriggerIsDown)
			{
				hoveredDeletable.Delete();
			}
		}
	}
}