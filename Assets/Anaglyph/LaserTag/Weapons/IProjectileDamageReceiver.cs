using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public struct ProjectileHit : INetworkSerializeByMemcpy
	{
		public IDamageable.Data damage;
		public Vector3 position;
	}

	public interface IProjectileDamageReceiver
	{
		void ReceiveProjectileHit(ProjectileHit hit);
	}
}
