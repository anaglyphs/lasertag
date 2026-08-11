using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public interface IDamageable
	{
		public struct Data
		{
			public ulong playerID;
			public float damage;
		}

		public float Health { get; }

		public void Damage(Data data);

		// Raised once per damageable hit, on the client dealing the damage.
		public static event Action<Vector3, IDamageable, Data> DamageDealt = delegate { };

		public static void DamageHierarchy(GameObject hierarchyRoot, Vector3 position, Data data,
			List<IDamageable> foundDamageables)
		{
			hierarchyRoot.GetComponentsInChildren(foundDamageables);

			foreach (IDamageable damageable in foundDamageables)
			{
				damageable.Damage(data);

				// after Damage so listeners see the target's post-hit state
				DamageDealt.Invoke(position, damageable, data);
			}
		}

		public static void DamageHierarchy(Component hierarchyRoot, Vector3 position, Data data,
			List<IDamageable> foundDamageables)
			=> DamageHierarchy(hierarchyRoot.gameObject, position, data, foundDamageables);
	}
}