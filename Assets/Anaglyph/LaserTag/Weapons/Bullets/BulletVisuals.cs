using System;
using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	public class BulletVisuals : MonoBehaviour
	{
		private Bullet bullet = null;
		[SerializeField] private LineRenderer lineRenderer = null;
		[SerializeField, Min(0f), Tooltip("Maximum world-space length of the visible laser body.")]
		private float bodyLength = 1f;
		[SerializeField, Range(0.01f, 1f),
		 Tooltip("Normalized percentage of the texture occupied by the laser body. For example, 62 / 112 = 0.554.")]
		private float bodyTexturePercentage = 62f / 112f;
		[SerializeField] private DepthLight depthLight = null;
		[SerializeField] private ParticleSystem impactEffect = null;

		//private MaterialPropertyBlock propertyBlock;

		public Color defaultColor = Teams.Colors[1];

		private void Awake()
		{
			//propertyBlock = new();

			bullet = GetComponentInParent<Bullet>();
			lineRenderer.positionCount = 2;
			lineRenderer.useWorldSpace = true;
			lineRenderer.enabled = false;

			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private Vector3 prevBulletPosition;

		private void HandleFire()
		{
			depthLight.enabled = true;
			lineRenderer.enabled = true;

			prevBulletPosition = bullet.transform.position;
			depthLight.transform.position = prevBulletPosition;
			UpdateLineRenderer(prevBulletPosition);

			NetworkManager manager = NetworkManager.Singleton;
			NetworkObject playerObject = manager.ConnectedClients[bullet.OwnerClientId].PlayerObject;
			TeamOwner teamOwner = playerObject.GetComponent<Networking.PlayerAvatar>().TeamOwner;
			byte team = teamOwner.Team;

			Color color = Teams.Colors[team];

			if (team == 0)
				color = defaultColor;

			depthLight.color = color;
			//propertyBlock.SetColor(TeamColorer.ColorID, color);
			//lineRenderer.SetPropertyBlock(propertyBlock);
			lineRenderer.startColor = color;
			lineRenderer.endColor = color;

			ParticleSystem.MainModule partMod = impactEffect.main;
			partMod.startColor = color;
		}

		private async void HandleCollision()
		{
			lineRenderer.enabled = false;
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

			if (lineRenderer.enabled)
				UpdateLineRenderer(pos);
		}

		private void UpdateLineRenderer(Vector3 bulletPosition)
		{
			Ray shotRay = bullet.Shot.ray;
			Vector3 direction = shotRay.direction.normalized;

			if (direction == Vector3.zero)
				direction = bullet.transform.forward;

			float travelDistance = Mathf.Max(
				0f,
				Vector3.Dot(bulletPosition - shotRay.origin, direction));
			float visibleBodyLength = Mathf.Min(bodyLength, travelDistance);
			float clampedBodyPercentage = Mathf.Clamp(bodyTexturePercentage, 0.01f, 1f);
			float visibleLineLength = visibleBodyLength / clampedBodyPercentage;
			float endMargin = (visibleLineLength - visibleBodyLength) * 0.5f;

			Vector3 headPosition = bulletPosition + direction * endMargin;
			Vector3 tailPosition = headPosition - direction * visibleLineLength;

			lineRenderer.SetPosition(0, tailPosition);
			lineRenderer.SetPosition(1, headPosition);
		}
	}
}
