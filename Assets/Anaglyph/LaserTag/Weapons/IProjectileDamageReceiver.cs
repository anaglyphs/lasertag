using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public struct ProjectileHit : INetworkSerializeByMemcpy
	{
		public IDamageable.Data damage;
		public Vector3 position;
		public Ray ray;
		public float shotTime, speed, distance, radius;
		public bool canBeBlocked;
	}

	public interface IProjectileDamageReceiver
	{
		void ReceiveProjectileHit(ProjectileHit hit);
	}
}
