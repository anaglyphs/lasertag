using Anaglyph.Lasertag.Networking;
using UnityEngine;
using UnityEngine.Events;
using Anaglyph.Lasertag.Weapons;
using System;
using Unity.XR.CoreUtils;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-100)]
	public class MainPlayer : MonoBehaviour
	{
		public static MainPlayer Instance { get; private set; }

		public Role currentRole = Role.Standard;

		public float Health { get; private set; } = Role.Standard.MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; } = false;

		public UnityEvent onDie = new();
		public UnityEvent onRespawn = new();
		public UnityEvent<bool> onAliveChange = new();
		public UnityEvent onTakeDamage = new();

		[NonSerialized] public Networking.Avatar networkPlayer;

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		//[SerializeField] private Transform torsoTransform;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform;
		public Transform RightHandTransform => rightHandTransform;
		//public Transform TorsoTransform => torsoTransform;

		public float RespawnTimerSeconds { get; private set; } = 0;

		// todo move this into another component. this really doesn't belong here
		private OVRPassthroughLayer passthroughLayer;
		[SerializeField] private BoolObject redDamageVision;

		private void Awake()
		{
			Instance = this;

			passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
		}

		public void Damage(float damage, ulong damagedBy)
		{
			onTakeDamage.Invoke();
			Health -= damage;

			if (Health < damage)
			{
				Kill(damagedBy);
			}
		}

		public void Kill(ulong killedBy)
		{
			if (!IsAlive) return;

			WeaponsManagement.canFire = false;

			networkPlayer.isAliveSync.Value = false;

			IsAlive = false;
			Health = 0;
			RespawnTimerSeconds = currentRole.RespawnTimeoutSeconds;
			networkPlayer.KilledByPlayerRpc(killedBy);

			onAliveChange.Invoke(false);
			onDie.Invoke();
		}

		public void Respawn()
		{
			if (IsAlive) return;

			passthroughLayer.edgeColor = Color.clear;

			WeaponsManagement.canFire = true;

			if (networkPlayer != null)
				networkPlayer.isAliveSync.Value = true;

			IsAlive = true;
			Health = currentRole.MaxHealth;

			onAliveChange.Invoke(true);
			onRespawn.Invoke();
		}

		private void FixedUpdate()
		{
			// respawn timer
			if (IsAlive) return;

			if ((currentRole.ReturnToBaseOnDie && IsInFriendlyBase) || !currentRole.ReturnToBaseOnDie)
				RespawnTimerSeconds -= Time.fixedDeltaTime;

			if (RespawnTimerSeconds <= 0)
				Respawn();

			RespawnTimerSeconds = Mathf.Clamp(RespawnTimerSeconds, 0, currentRole.RespawnTimeoutSeconds);
		}

		private void Update()
		{
			// health

			if (redDamageVision.Value)
			{
				passthroughLayer.edgeRenderingEnabled = true;
				var color = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(Health / Role.Standard.MaxHealth));
				passthroughLayer.edgeColor = color;
			}
			else
			{
				passthroughLayer.edgeRenderingEnabled = false;
				passthroughLayer.edgeColor = Color.clear;
			}

			if (IsAlive)
			{
				if (Health < 0)
					Kill(0);
				else
					Health += currentRole.HealthRegenerationPerSecond * Time.deltaTime;
			}

			WeaponsManagement.canFire = IsAlive;

			Health = Mathf.Clamp(Health, 0, currentRole.MaxHealth);

			// bases
			IsInFriendlyBase = false;
			foreach (Base b in Base.AllBases)
			{
				if (b.Team != networkPlayer.Team)
					continue;

				if (!Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
					continue;

				IsInFriendlyBase = true;
				break;
			}

			// network player transforms
			if (networkPlayer != null)
			{
				networkPlayer.HeadTransform.SetWorldPose(headTransform.GetWorldPose());
				networkPlayer.LeftHandTransform.SetWorldPose(leftHandTransform.GetWorldPose());
				networkPlayer.RightHandTransform.SetWorldPose(rightHandTransform.GetWorldPose());
				//networkPlayer.TorsoTransform.SetFrom(torsoTransform);
			}
		}
	}
}
