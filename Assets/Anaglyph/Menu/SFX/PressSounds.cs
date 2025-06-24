using UnityEngine;
using UnityEngine.EventSystems;

namespace Anaglyph.Menu
{
	public class PressSounds : MonoBehaviour, IPointerDownHandler, IPointerClickHandler
    {
		public AudioClip press;
		public AudioClip click;

		public void OnPointerDown(PointerEventData eventData)
		{
			if(press != null)
				AudioSource.PlayClipAtPoint(press, transform.position);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (click != null)
				AudioSource.PlayClipAtPoint(click, transform.position);
		}
	}
}
