using UnityEngine;

namespace Anaglyph.Zombies
{
    public class DestroyAfterSeconds : MonoBehaviour
    {
        [SerializeField] private float seconds;

        private async void Start()
        {
            await Awaitable.WaitForSecondsAsync(5);

            Destroy(gameObject);
        }
    }
}
