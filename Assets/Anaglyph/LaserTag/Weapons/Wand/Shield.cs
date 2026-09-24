using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class Shield : NetworkBehaviour
	{
		[SerializeField, Min(0)] private float lifetime = 0.5f;
		private float expiresAt;

		public override void OnNetworkSpawn() => expiresAt = Time.time + lifetime;

		private void Update()
		{
			if (IsSpawned && IsOwner && Time.time >= expiresAt)
				NetworkObject.Despawn(true);
		}
	}
}
