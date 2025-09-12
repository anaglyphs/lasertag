using Unity.Netcode;
using UnityEngine;
using UnityEngine.VFX;

namespace Anaglyph.Lasertag
{
    public class BulletVisuals : MonoBehaviour
    {
		private Bullet bullet = null;
		[SerializeField] private TrailRenderer trailRenderer = null;
		[SerializeField] private DepthLight depthLight = null;
		[SerializeField] private VisualEffect impactEffect = null;

		private MaterialPropertyBlock propertyBlock;

		public Color defaultColor = Teams.Colors[1];

		private void Awake()
		{
			propertyBlock = new();

			bullet = GetComponentInParent<Bullet>();

			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private Vector3 prevBulletPosition;

		private void HandleFire()
		{
			depthLight.enabled = true;
			trailRenderer.Clear();

			prevBulletPosition = bullet.transform.position;
			depthLight.transform.position = prevBulletPosition;

			var manager = NetworkManager.Singleton;
			var playerObject = manager.ConnectedClients[bullet.OwnerClientId].PlayerObject;
			var teamOwner = playerObject.GetComponent<Networking.PlayerAvatar>().TeamOwner;
			var team = teamOwner.Team;

			var color = Teams.Colors[team];

			if (team == 0)
				color = defaultColor;

			depthLight.color = color;
			propertyBlock.SetColor(TeamColorer.ColorID, color);
			trailRenderer.SetPropertyBlock(propertyBlock);

			impactEffect.SetVector4(TeamColorer.ColorID, color);
		}

		private async void HandleCollision()
		{
			impactEffect.Play();

			await Awaitable.NextFrameAsync();
			depthLight.enabled = false;
		}

		private void LateUpdate()
		{
			var pos = bullet.transform.position;
			var prevPos = prevBulletPosition;
			depthLight.transform.position = Vector3.Lerp(pos, prevPos, 0.5f);
			prevBulletPosition = pos;
		}
	}
}
