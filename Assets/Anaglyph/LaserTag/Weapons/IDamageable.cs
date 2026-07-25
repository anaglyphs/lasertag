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

		public void Damage(Data data);

		public static void DamageHierarchy(GameObject hierarchyRoot, Data data, List<IDamageable> foundDamageables)
		{
			hierarchyRoot.GetComponentsInChildren(foundDamageables);

			foreach (IDamageable damageable in foundDamageables) damageable.Damage(data);
		}

		public static void DamageHierarchy(Component hierarchyRoot, Data data, List<IDamageable> foundDamageables)
		{
			hierarchyRoot.GetComponentsInChildren(foundDamageables);

			foreach (IDamageable damageable in foundDamageables) damageable.Damage(data);
		}
	}
}