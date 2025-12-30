using UnityEngine;

namespace Anaglyph
{
	public class AudioPoolTest : MonoBehaviour
	{
		public AudioClip clip;

		private async void Start()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(Random.Range(0.1f, 1f));

				var x = Random.Range(-10f, 10f);
				var y = Random.Range(-10f, 10f);
				var z = Random.Range(-10f, 10f);

				AudioPool.Play(clip, new Vector3(x, y, z));
			}
		}
	}
}