using UnityEngine;

namespace Anaglyph.Menu
{
	public class EnabledSounds : MonoBehaviour
    {
		public AudioClip enable;
		public AudioClip disable;

		private void OnEnable()
		{
			if(enable != null)
				AudioSource.PlayClipAtPoint(enable, transform.position);
		}

		private void OnDisable()
		{
			if (disable != null)
				AudioSource.PlayClipAtPoint(disable, transform.position);
		}
	}
}
