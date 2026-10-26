using System;
using System.Threading;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.LaserTag.Shaders;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace Anaglyph.LaserTag.Weapons.Bullets
{
	public class BulletVisuals : MonoBehaviour
	{
		private Bullet bullet = null;
		[SerializeField, Tooltip("Cube drawn with the Laser shader. Its Z scale is the body length plus a glow cap at each end; X and Y are the glow diameter.")]
		private MeshRenderer laserBody = null;
		[SerializeField, Min(0f), Tooltip("Maximum world-space length of the visible laser body.")]
		private float bodyLength = 1f;
		[SerializeField, Min(0.001f), Tooltip("World-space radius the glow fades out by.")]
		private float glowRadius = 0.05f;
		[SerializeField] private Light pointLight = null;
		[SerializeField] private ParticleSystem impactEffect = null;

		private static readonly int ColorID = Shader.PropertyToID("_Color");
		private MaterialPropertyBlock propertyBlock;

		public Color defaultColor = Teams.Colors[1];

		private void Awake()
		{
			propertyBlock = new();

			bullet = GetComponentInParent<Bullet>();
			laserBody.enabled = false;

			bullet.OnFire += HandleFire;
			bullet.OnCollide += HandleCollision;
		}

		private Vector3 prevBulletPosition;
		// distance along the shot ray where the bullet hit; the body keeps
		// flying and drains into this point instead of vanishing on impact
		private float hitDistance = float.PositiveInfinity;

		private void HandleFire()
		{
			pointLight.enabled = true;
			laserBody.enabled = true;
			hitDistance = float.PositiveInfinity;

			prevBulletPosition = bullet.transform.position;
			pointLight.transform.position = prevBulletPosition;
			UpdateLaserBody(DistanceAlongShot(prevBulletPosition));

			NetworkManager manager = NetworkManager.Singleton;
			NetworkObject playerObject = manager.ConnectedClients[bullet.OwnerClientId].PlayerObject;
			TeamOwner teamOwner = playerObject.GetComponent<PlayerAvatar>().TeamOwner;
			byte team = teamOwner.Team;

			Color color = Teams.Colors[team];

			if (team == 0)
				color = defaultColor;

			pointLight.color = color;
			propertyBlock.SetColor(ColorID, color);
			laserBody.SetPropertyBlock(propertyBlock);

			ParticleSystem.MainModule partMod = impactEffect.main;
			partMod.startColor = color;
		}

		private async void HandleCollision()
		{
			// HitRpc has already moved the bullet onto the hit point
			hitDistance = DistanceAlongShot(bullet.transform.position);
			impactEffect.Play();

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

		private void LateUpdate()
		{
			Vector3 pos = bullet.transform.position;
			Vector3 prevPos = prevBulletPosition;
			pointLight.transform.position = Vector3.Lerp(pos, prevPos, 0.5f);
			prevBulletPosition = pos;

			if (!laserBody.enabled)
				return;

			// once stopped, the tip keeps the speed it would have had in flight
			float headDistance = float.IsPositiveInfinity(hitDistance)
				? DistanceAlongShot(pos)
				: bullet.MetersPerSecond * bullet.Shot.GetFlightTime();
			UpdateLaserBody(headDistance);
		}

		private Vector3 ShotDirection()
		{
			Vector3 direction = bullet.Shot.ray.direction.normalized;

			if (direction == Vector3.zero)
				direction = bullet.transform.forward;

			return direction;
		}

		private float DistanceAlongShot(Vector3 position)
		{
			return Mathf.Max(0f, Vector3.Dot(position - bullet.Shot.ray.origin, ShotDirection()));
		}

		private void UpdateLaserBody(float headDistance)
		{
			float tailDistance = Mathf.Max(0f, headDistance - bodyLength);

			if (tailDistance >= hitDistance)
			{
				laserBody.enabled = false;
				return;
			}

			headDistance = Mathf.Min(headDistance, hitDistance);
			float visibleBodyLength = headDistance - tailDistance;

			// the shader takes the body length as Z scale minus the X-scale glow
			// diameter, centred on the cube, so the body's tip lands on the head
			Ray shotRay = bullet.Shot.ray;
			Vector3 direction = ShotDirection();
			Transform body = laserBody.transform;
			body.SetPositionAndRotation(
				shotRay.origin + direction * ((headDistance + tailDistance) * 0.5f),
				Quaternion.LookRotation(direction));
			float diameter = glowRadius * 2f;
			body.localScale = new Vector3(diameter, diameter, visibleBodyLength + diameter);
		}
	}
}
