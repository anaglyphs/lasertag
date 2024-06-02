using Anaglyph.LaserTag.Networking;
using UnityEngine;
using UnityEngine.Events;
using Anaglyph.LaserTag.Weapons;
using System;

namespace Anaglyph.LaserTag
{
	public class PlayerLocal : SingletonBehavior<PlayerLocal>
	{
		public Role currentRole = Role.Default;
		
		public float health = Role.Default.MaxHealth;

		public float respawnTimer = 0;

		public bool alive { get; private set; } = true;

		public bool nearBase = false;

		public UnityEvent onDie = new();
		public UnityEvent onRespawn = new();
		public UnityEvent<bool> onAliveChange = new();
		public UnityEvent onTakeDamage = new();

		private OVRPassthroughLayer passthroughLayer;


		[NonSerialized] public Player networkPlayer;

		[SerializeField] private Transform localHeadTransform;
		[SerializeField] private Transform localLeftHandTransform;
		[SerializeField] private Transform localRightHandTransform;
		[SerializeField] private Transform localChestTransform;

		protected override void SuperAwake()
		{
			base.SuperAwake();

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

			bool closeToBase = false;

			foreach (Base _base in Base.AllBases)
			{
				if (_base.TeamNumber != currentRole.TeamNumber)
					continue;

				Vector3 headPosFlat = localHeadTransform.position;
				headPosFlat.y = 0;

				Vector3 basePosFlat = _base.transform.position;
				basePosFlat.y = 0;

				if (Vector3.Distance(basePosFlat, headPosFlat) < currentRole.BaseRespawnDistance)
				{
					closeToBase = true;
				}
			}

			nearBase = closeToBase;

			if (alive)
			{
				respawnTimer = 0;

				passthroughLayer.edgeColor = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(health / Role.Default.MaxHealth));

				health += currentRole.HealthRegenerationPerSecond * Time.deltaTime;
				health = Mathf.Clamp(health, 0, currentRole.MaxHealth);
			} else
			{
				if ((currentRole.ReturnToBaseOnDie && nearBase) || !currentRole.ReturnToBaseOnDie)
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
				networkPlayer.ChestTransform.SetPositionAndRotation(localChestTransform.position, localChestTransform.rotation);
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
	}
}
