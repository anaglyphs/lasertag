using Anaglyph.LaserTag.Networking;
using UnityEngine;
using UnityEngine.Events;
using Anaglyph.LaserTag.Weapons;
using System;

namespace Anaglyph.LaserTag
{
	public class MainPlayer : SingletonBehavior<MainPlayer>
	{
		public Role currentRole = Role.Standard;
		public int Team => currentRole.TeamNumber;
		
		public float health = Role.Standard.MaxHealth;

		public float respawnTimer = 0;

		public bool alive { get; private set; } = true;

		public bool inBase = false;

		public UnityEvent onDie = new();
		public UnityEvent onRespawn = new();
		public UnityEvent<bool> onAliveChange = new();
		public UnityEvent onTakeDamage = new();

		private OVRPassthroughLayer passthroughLayer;

		[NonSerialized] public Player networkPlayer;

		[SerializeField] private Transform localHeadTransform;
		[SerializeField] private Transform localLeftHandTransform;
		[SerializeField] private Transform localRightHandTransform;

		public Transform LocalHeadTransform => localHeadTransform;

		//[SerializeField] private Transform localChestTransform;

		protected override void SingletonAwake()
		{
			passthroughLayer = FindObjectOfType<OVRPassthroughLayer>(true);
			passthroughLayer.edgeRenderingEnabled = true;
			passthroughLayer.edgeColor = Color.clear;
		}

		public void TakeDamage(float damage)
		{
			if (!alive)
				return;

			onTakeDamage.Invoke();

			health -= damage;

			if (health <= 0)
			{
				Kill();
			}
		}

		private void Update()
		{
			//if (networkPlayer != null)
			//	currentRole = GameManager.Instance.GetRoleByUuid(networkPlayer.role.Value);
			//else 
			//	currentRole = Role.Default;

			inBase = false;
			foreach (Base b in Base.AllBases)
			{
				if (b.TeamNumber != currentRole.TeamNumber)
					continue;

				if (Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, localHeadTransform.position))
				{
					inBase = true;
				}
			}

			if (alive)
			{
				respawnTimer = 0;

				passthroughLayer.edgeColor = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(health / Role.Standard.MaxHealth));

				health += currentRole.HealthRegenerationPerSecond * Time.deltaTime;
				health = Mathf.Clamp(health, 0, currentRole.MaxHealth);
			} else
			{
				if ((currentRole.ReturnToBaseOnDie && inBase) || !currentRole.ReturnToBaseOnDie)
				{
					respawnTimer += Time.deltaTime;
				}
				else
				{
					respawnTimer -= Time.deltaTime;
				}

				if (respawnTimer >= currentRole.RespawnTimeSeconds)
				{
					Respawn();
				}
				else if (respawnTimer <= 0.0)
				{
					respawnTimer = 0.0f;
				}

				health = Mathf.Lerp(0, currentRole.MaxHealth, Mathf.Clamp01(respawnTimer / currentRole.RespawnTimeSeconds));

				passthroughLayer.edgeColor = Color.Lerp(Color.clear, Color.red, Mathf.Clamp01(currentRole.RespawnTimeSeconds - respawnTimer));
			}

			if (networkPlayer != null)
			{
				networkPlayer.HeadTransform.SetPositionAndRotation(localHeadTransform.position, localHeadTransform.rotation);
				networkPlayer.LeftHandTransform.SetPositionAndRotation(localLeftHandTransform.position, localLeftHandTransform.rotation);
				networkPlayer.RightHandTransform.SetPositionAndRotation(localRightHandTransform.position, localRightHandTransform.rotation);
			}
		}

		public void Kill()
		{
			passthroughLayer.edgeColor = Color.red;

			WeaponsManagement.canFire = false;

			networkPlayer.isAliveSync.Value = false;

			alive = false;

			onAliveChange.Invoke(false);
			onDie.Invoke();
		}

		public void Respawn()
		{
			passthroughLayer.edgeColor = Color.clear;

			WeaponsManagement.canFire = true;

			networkPlayer.isAliveSync.Value = true;

			alive = true;

			health = currentRole.MaxHealth;

			onAliveChange.Invoke(true);
			onRespawn.Invoke();
		}

		protected override void OnSingletonDestroy()
		{
			
		}
	}
}
