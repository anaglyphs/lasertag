using System;
using System.Threading;
using Anaglyph.Audio;
using Anaglyph.LaserTag.Weapons.Bullets;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class SpellProjectileVisual : MonoBehaviour
	{
		[SerializeField] private Light pointLight;
		[SerializeField] private ParticleSystem[] particles;
		[SerializeField] private AudioClip fireSFX;
		private Bullet bullet;

		private void Awake()
		{
			bullet = GetComponent<Bullet>();
			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private void OnDestroy()
		{
			bullet.OnFire -= HandleFire;
			bullet.OnCollide -= HandleCollision;
		}

		private void HandleFire()
		{
			pointLight.enabled = true;
			foreach (ParticleSystem effect in particles)
			{
				effect.Clear(true);
				effect.Play(true);
			}

			if (fireSFX != null)
				AudioPool.Play(fireSFX, transform.position);
		}

		private async void HandleCollision()
		{
			foreach (ParticleSystem effect in particles)
				effect.Stop(true, ParticleSystemStopBehavior.StopEmitting);

			CancellationToken ctkn = destroyCancellationToken;

			try
			{
				await Awaitable.NextFrameAsync(ctkn);
				pointLight.enabled = false;
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void OnDisable()
		{
			pointLight.enabled = false;
			foreach (ParticleSystem effect in particles)
				effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
	}
}
