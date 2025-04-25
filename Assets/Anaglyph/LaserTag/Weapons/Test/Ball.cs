using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class Ball : MonoBehaviour
    {
		[SerializeField] private float aliveForSeconds = 10;

		private async void OnEnable()
		{
			await Awaitable.WaitForSecondsAsync(10);

			Destroy(gameObject);
		}
	}
}
