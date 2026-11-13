using System;
using Anaglyph.Audio;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WeaponVisual : MonoBehaviour
	{
		[SerializeField] private AudioClip fireSFX;
		[SerializeField, Min(0.01f)] private float fireSFXFullVolumeDistance = 1;

		public bool IsFiring { get; private set; }

		public event Action Fired = delegate { };
		public event Action<bool> IsFiringChanged = delegate { };

		public void PlayFire()
		{
			if (fireSFX != null)
				AudioPool.Play(fireSFX, transform.position, 1, fireSFXFullVolumeDistance);

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
