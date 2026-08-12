using System;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponVisual : MonoBehaviour
	{
		public bool IsFiring { get; private set; }

		public event Action Fired = delegate { };
		public event Action<bool> IsFiringChanged = delegate { };

		public void PlayFire()
		{
			Fired.Invoke();
		}

		public void SetFiring(bool firing)
		{
			if (IsFiring == firing)
				return;

			IsFiring = firing;
			IsFiringChanged.Invoke(firing);
		}

		private void OnDisable()
		{
			SetFiring(false);
		}
	}
}
