using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class FireballVisuals : MonoBehaviour
    {
		private Bullet bullet = null;
		[SerializeField] private DepthLightDriver depthLight = null;
		[SerializeField] private ParticleSystem flightParticles;

		private void Awake()
		{
			bullet = GetComponentInParent<Bullet>();

			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private void HandleFire()
		{
			depthLight.enabled = true;

			flightParticles.Play(true);
		}

		private async void HandleCollision()
		{
			flightParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

			await Awaitable.NextFrameAsync();

			depthLight.enabled = false;
		}
	}
}
