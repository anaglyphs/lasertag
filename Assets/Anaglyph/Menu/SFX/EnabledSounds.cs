using UnityEngine;

namespace Anaglyph.Menu
{
	public class EnabledSounds : MonoBehaviour
    {
		public AudioClip enable;
		public AudioClip disable;

		private void OnEnable()
		{
			if (enable != null)
				AudioPool.Play(enable, transform.position);
		}

		private void OnDisable()
		{
			if (disable != null)
				AudioPool.Play(disable, transform.position);
		}
	}
}
