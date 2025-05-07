using Unity.Netcode;

namespace Anaglyph.Lasertag.Gallery
{
    public class DespawnAtZ : NetworkBehaviour
    {
		public float MinZ = 0;

		private void Update()
		{
			if (transform.position.z < MinZ)
				NetworkObject.Despawn();
		}
	}
}
