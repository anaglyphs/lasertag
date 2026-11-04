using System;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public interface IDamageable
	{
		
		public struct Data : INetworkSerializeByMemcpy
		{
			public ulong playerID;
			public float damage;
		}

		public float Health { get; }

		public void Damage(Data data);

		// Raised once per damageable hit, on the client dealing the damage.
		public static event Action<Vector3, IDamageable, Data> DamageDealt = delegate { };

		public static void NotifyDamageDealt(Vector3 position, IDamageable target, Data data)
			=> DamageDealt.Invoke(position, target, data);

		public static void DamageParentOf(Component hit, Vector3 position, Data data)
		{
			IDamageable damageable = hit.GetComponentInParent<IDamageable>();
			if (damageable == null) return;

			damageable.Damage(data);

			// after Damage so listeners see the target's post-hit state
			DamageDealt.Invoke(position, damageable, data);
		}
	}
}
