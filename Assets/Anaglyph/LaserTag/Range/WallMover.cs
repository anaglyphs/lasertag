using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class WallMover : MonoBehaviour
    {
		public float MaxDist = 10f;
		
		public float Speed = 1;


        private float spawnTime;
		private float startZ;

		private void Awake()
		{
			spawnTime = Time.time;
			startZ = transform.position.z;
		}

		private void Update()
		{
			float lifetime = Time.time - spawnTime;

			float z = MaxDist - lifetime * Speed + startZ;

			z = mod(z, MaxDist);

			transform.position = new(0, 0, z);
		}

		private static float mod(float x, float m)
		{
			return (x % m + m) % m;
		}
	}
}
