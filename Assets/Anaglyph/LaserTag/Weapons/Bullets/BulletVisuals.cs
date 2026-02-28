using System;
using System.Threading;
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
		[SerializeField] private ParticleSystem impactEffect = null;

		//private MaterialPropertyBlock propertyBlock;

		public Color defaultColor = Teams.Colors[1];

		private void Awake()
		{
			//propertyBlock = new();

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

			NetworkManager manager = NetworkManager.Singleton;
			NetworkObject playerObject = manager.ConnectedClients[bullet.OwnerClientId].PlayerObject;
			TeamOwner teamOwner = playerObject.GetComponent<Networking.PlayerAvatar>().TeamOwner;
			byte team = teamOwner.Team;

			Color color = Teams.Colors[team];

			if (team == 0)
				color = defaultColor;

			depthLight.color = color;
			//propertyBlock.SetColor(TeamColorer.ColorID, color);
			//trailRenderer.SetPropertyBlock(propertyBlock);
			trailRenderer.startColor = color;
			trailRenderer.endColor = color;

			ParticleSystem.MainModule partMod = impactEffect.main;
			partMod.startColor = color;
		}

		private async void HandleCollision()
		{
			impactEffect.Play();

			CancellationToken ctkn = destroyCancellationToken;

			try
			{
				await Awaitable.NextFrameAsync(ctkn);
				ctkn.ThrowIfCancellationRequested();
				depthLight.enabled = false;
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void LateUpdate()
		{
			Vector3 pos = bullet.transform.position;
			Vector3 prevPos = prevBulletPosition;
			depthLight.transform.position = Vector3.Lerp(pos, prevPos, 0.5f);
			prevBulletPosition = pos;
		}
	}
}