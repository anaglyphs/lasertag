using UnityEngine;

namespace Anaglyph.Zombies
{
    public class DestroySoundPlayer : MonoBehaviour
    {
        public AudioClip[] clips;

		private void OnDestroy()
		{
			AudioClip clip = clips[Random.Range(0, clips.Length)];
			AudioSource.PlayClipAtPoint(clip, transform.position);
		}
	}
}
