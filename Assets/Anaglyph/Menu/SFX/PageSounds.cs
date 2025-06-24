using UnityEngine;

namespace Anaglyph.Menu
{
	public class PageSounds : MonoBehaviour
	{
		private NavPage page;

		public AudioClip navBack;

		private void Awake()
		{
			TryGetComponent(out page);

			page.NavigatingBack.AddListener(OnNavigatingBack);
		}

		private void OnNavigatingBack()
		{
			if (navBack != null)
				AudioSource.PlayClipAtPoint(navBack, transform.position);
		}
	}
}
