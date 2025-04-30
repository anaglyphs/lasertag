using System.Collections;
using UnityEngine;

namespace Anaglyph.Zombies
{
	public class RandomSoundPlayer : MonoBehaviour
	{
		public AudioSource audioSource;
		public AudioClip[] clips;
		public Vector2 delayRange = new Vector2(3f, 7f); // Random delay between 3 and 7 seconds

		private void Start()
		{
			if (audioSource == null)
				audioSource = GetComponent<AudioSource>();

			StartCoroutine(PlayRandomSounds());
		}

		private IEnumerator PlayRandomSounds()
		{
			while (true)
			{
				float waitTime = Random.Range(delayRange.x, delayRange.y);
				yield return new WaitForSeconds(waitTime);

				if (clips.Length > 0)
				{
					AudioClip clip = clips[Random.Range(0, clips.Length)];
					audioSource.PlayOneShot(clip);
				}
			}
		}
	}
}
