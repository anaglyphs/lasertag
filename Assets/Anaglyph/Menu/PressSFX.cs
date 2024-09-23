using UnityEngine;
using UnityEngine.EventSystems;

namespace Anaglyph.Menu
{
	public class PressSFX : MonoBehaviour, IPointerDownHandler
    {
		public AudioClip clip;

		public void OnPointerDown(PointerEventData eventData)
		{
			AudioSource.PlayClipAtPoint(clip, transform.position);
		}
	}
}
