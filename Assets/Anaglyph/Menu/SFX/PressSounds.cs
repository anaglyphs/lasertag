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
				AudioPool.Play(press, transform.position);
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (click != null)
				AudioPool.Play(click, transform.position);
		}
	}
}
