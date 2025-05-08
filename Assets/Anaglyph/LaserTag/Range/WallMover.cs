using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class WallMover : MonoBehaviour
    {
		public float Speed = 1;

        private float startTime;
		private float startZ;

		private void Start()
		{
			startTime = Time.time;
			startZ = transform.position.z;
		}

		private void Update()
		{
			float lifetime = Time.time - startTime;

			float z = lifetime * -Speed + startZ;

			transform.position = new(0, 0, z);
		}
	}
}
