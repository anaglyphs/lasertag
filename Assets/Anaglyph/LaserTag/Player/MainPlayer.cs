using Anaglyph.LaserTag.Networking;
using UnityEngine;
using UnityEngine.Events;
using Anaglyph.LaserTag.Weapons;
using System;

namespace Anaglyph.LaserTag
{
	[DefaultExecutionOrder(-100)]
	public class MainPlayer : SingletonBehavior<MainPlayer>
	{
		public Role currentRole = Role.Standard;

		public float Health { get; private set; } =  Role.Standard.MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; } = false;

		public UnityEvent onDie = new();
		public UnityEvent onRespawn = new();
		public UnityEvent<bool> onAliveChange = new();
		public UnityEvent onTakeDamage = new();

		[NonSerialized] public Player networkPlayer;

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform; 
		public Transform RightHandTransform => rightHandTransform;

		public float RespawnTimerSeconds { get; private set; } = 0;

		// todo move this into another component
		private OVRPassthroughLayer passthroughLayer;

		protected override void SingletonAwake()
		{
			passthroughLayer = FindObjectOfType<OVRPassthroughLayer>(true);
			passthroughLayer.edgeRenderingEnabled = true;
			passthroughLayer.edgeColor = Color.clear;
		}

		public void Damage(float damage, ulong damagedBy)
		{
			onTakeDamage.Invoke();
			Health -= damage;

			if(Health < damage)
			{
				Kill(damagedBy);
			}
		}

		private void HandleBases()
		{
			IsInFriendlyBase = false;
			foreach (Base b in Base.AllBases)
			{
				if (b.Team != currentRole.TeamNumber)
					continue;

				if (Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
					continue;

				IsInFriendlyBase = true;
				break;
			}
		}

		private void HandleHealth()
		{
			passthroughLayer.edgeColor = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(Health / Role.Standard.MaxHealth));

			if (IsAlive)
			{
				if (Health < 0)
					Kill(0);
				else
					Health += currentRole.HealthRegenerationPerSecond * Time.deltaTime;
			}

			WeaponsManagement.canFire = IsAlive;

			Health = Mathf.Clamp(Health, 0, currentRole.MaxHealth);
		}

		private void CountdownToRespawn()
		{
			if (IsAlive)
				return;

			if ((currentRole.ReturnToBaseOnDie && IsInFriendlyBase) || !currentRole.ReturnToBaseOnDie)
				RespawnTimerSeconds -= Time.fixedDeltaTime;

			if (RespawnTimerSeconds <= 0)
				Respawn();

			RespawnTimerSeconds = Mathf.Clamp(RespawnTimerSeconds, 0, currentRole.RespawnTimeoutSeconds);
		}

		private void FixedUpdate()
		{
			if(!IsAlive)
				CountdownToRespawn();
		}

		private void UpdateNetworkedPlayerTransforms()
		{
			if (networkPlayer == null)
				return;
				
			networkPlayer.HeadTransform.SetFrom(headTransform);
			networkPlayer.LeftHandTransform.SetFrom(leftHandTransform);
			networkPlayer.RightHandTransform.SetFrom(rightHandTransform);
		}

		public void Kill(ulong killedBy)
		{
			WeaponsManagement.canFire = false;

			networkPlayer.isAliveSync.Value = false;

			IsAlive = false;
			Health = 0;
			RespawnTimerSeconds = currentRole.RespawnTimeoutSeconds;
			networkPlayer.KilledRpc(killedBy);

			onAliveChange.Invoke(false);
			onDie.Invoke();
		}

		public void Respawn()
		{
			passthroughLayer.edgeColor = Color.clear;

			WeaponsManagement.canFire = true;

			networkPlayer.isAliveSync.Value = true;
			networkPlayer.RespawnRpc();

			IsAlive = true;
			Health = currentRole.MaxHealth;

			onAliveChange.Invoke(true);
			onRespawn.Invoke();
		}

		private void Update()
		{
			HandleHealth();
			HandleBases();
			UpdateNetworkedPlayerTransforms();
		}

		protected override void OnSingletonDestroy()
		{
			
		}
	}
}
