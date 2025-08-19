using StrikerLink.Shared.Utils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class LightningVisuals : MonoBehaviour
	{
		private Bullet bullet = null;
		[SerializeField] private DepthLightDriver depthLight = null;
		[SerializeField] private ParticleSystem particles;

		private void Awake()
		{
			bullet = GetComponentInParent<Bullet>();

			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private void HandleFire()
		{
			depthLight.enabled = true;
			particles.Clear();
			particles.Play(true);
		}

		private async void HandleCollision()
		{
			particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
			await Awaitable.NextFrameAsync();

			depthLight.enabled = false;
		}
	}
}
