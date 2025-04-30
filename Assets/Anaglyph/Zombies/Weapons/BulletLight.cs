using UnityEngine;

namespace Anaglyph.Zombies
{
    public class BulletLight : MonoBehaviour
    {
		private Bullet bullet = null;
		private MeshRenderer meshRenderer = null;

		private void Awake()
		{
			TryGetComponent(out meshRenderer);
			bullet = GetComponentInParent<Bullet>();

			bullet.OnFire.AddListener(HandleFire);
			bullet.OnCollide.AddListener(HandleCollision);
		}

		private void HandleFire()
		{
			meshRenderer.enabled = true;

			prevBulletPosition = bullet.transform.position;
			transform.position = prevBulletPosition;
		}

		private async void HandleCollision()
		{
			await Awaitable.NextFrameAsync();
			meshRenderer.enabled = false;
		}

		private Vector3 prevBulletPosition;
		private void LateUpdate()
		{
			var pos = bullet.transform.position;
			transform.position = Vector3.Lerp(pos, prevBulletPosition, 0.5f);
			prevBulletPosition = pos;
		}
	}
}
