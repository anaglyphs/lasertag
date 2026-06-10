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

		public static void DamageHierarchy(GameObject hierarchyRoot, Data data)
		{
			IDamageable[] damageables = hierarchyRoot.GetComponentsInChildren<IDamageable>();

			foreach (IDamageable damageable in damageables) damageable.Damage(data);
		}

		public static void DamageHierarchy(Component hierarchyRoot, Data data)
		{
			IDamageable[] damageables = hierarchyRoot.GetComponentsInChildren<IDamageable>();

			foreach (IDamageable damageable in damageables) damageable.Damage(data);
		}
	}
}