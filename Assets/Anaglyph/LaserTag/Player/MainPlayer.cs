using Anaglyph.Lasertag.Networking;
using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using System;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using VariableObjects;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-100)]
	public class MainPlayer : MonoBehaviour
	{
		public static MainPlayer Instance { get; private set; }

		private const float MaxHealth = 100;

		public float Health { get; private set; } = MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; } = false;

		public event Action Died = delegate { };
		public event Action Respawned = delegate { };
		public event Action Damaged = delegate { };
		public event Action<byte> TeamChanged = delegate { };

		[SerializeField] private GameObject avatarPrefab;
		private PlayerAvatar avatar;
		public PlayerAvatar Avatar => avatar;

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		[SerializeField] private OVRSkeleton skeleton;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform;
		public Transform RightHandTransform => rightHandTransform;
		public OVRSkeleton Skeleton => skeleton;

		public float RespawnTimerSeconds { get; private set; } = 0;

		// todo move this into another component. this really doesn't belong here
		private OVRPassthroughLayer passthroughLayer;
		[SerializeField] private BoolObject redDamageVision;
		//[SerializeField] private BoolObject participatingInGames;

		private void Awake()
		{
			Instance = this;

			passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();
		}

		private void Start()
		{
			NetworkManager.Singleton.OnConnectionEvent += HandleConnectionEvent;
			//participatingInGames.onChange += HandleParticipatingChange;
		}

		private void OnDestroy()
		{
			if (NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= HandleConnectionEvent;

			//participatingInGames.onChange -= HandleParticipatingChange;
		}

		private void HandleConnectionEvent(NetworkManager manager, ConnectionEventData eventData)
		{
			if (NetcodeManagement.ThisClientConnected(eventData))
				SpawnAvatar();
		}

		//private void HandleParticipatingChange(bool b) => HandleAvatar();

		//private void HandleAvatar()
		//{
		//	if (participatingInGames && avatar == null)
		//	{
		//		SpawnAvatar();
		//	}
		//	else if (avatar != null)
		//	{
		//		avatar.NetworkObject.Despawn();
		//	}
		//}

		private void SpawnAvatar()
		{
			var manager = NetworkManager.Singleton;
			if (!manager.IsConnectedClient)
				return;

			var avatarObject = NetworkObject.InstantiateAndSpawn(avatarPrefab,
				manager, manager.LocalClientId, destroyWithScene: true, isPlayerObject: true);

			avatar = avatarObject.GetComponent<PlayerAvatar>();
			avatar.TeamOwner.OnTeamChange.AddListener(TeamChanged.Invoke);
		}

		public void Damage(float damage, ulong damagedBy)
		{
			Damaged.Invoke();
			Health -= damage;

			if (Health < damage)
			{
				Kill(damagedBy);
			}
		}

		public void Kill(ulong killerID)
		{
			if (!IsAlive) return;

			WeaponsManagement.canFire = false;

			avatar.isAliveSync.Value = false;

			IsAlive = false;
			Health = 0;
			RespawnTimerSeconds = MatchReferee.Instance.Settings.respawnSeconds;

			Died.Invoke();

			if (PlayerAvatar.All.TryGetValue(killerID, out var killer))
			{
				avatar.KilledByPlayerRpc(killerID);

				var referee = MatchReferee.Instance;
				if (referee.State == MatchState.Playing && killer.Team != avatar.Team)
				{
					referee.ScoreTeamRpc(killer.Team, referee.Settings.pointsPerKill);
				}
			}
		}

		public void Respawn()
		{
			if (IsAlive) return;

			passthroughLayer.edgeColor = Color.clear;

			WeaponsManagement.canFire = true;

			if (avatar != null)
				avatar.isAliveSync.Value = true;

			IsAlive = true;
			Health = MaxHealth;

			//onAliveChange.Invoke(true);
			Respawned.Invoke();
		}

		private void FixedUpdate()
		{
			// respawn timer
			if (IsAlive) return;

			if ((MatchReferee.Instance.Settings.respawnInBases && IsInFriendlyBase) || !MatchReferee.Instance.Settings.respawnInBases)
				RespawnTimerSeconds -= Time.fixedDeltaTime;

			if (RespawnTimerSeconds <= 0)
				Respawn();

			RespawnTimerSeconds = Mathf.Clamp(RespawnTimerSeconds, 0, MatchReferee.Instance.Settings.respawnSeconds);
		}

		private void Update()
		{
			// health

			if (redDamageVision.Value)
			{
				passthroughLayer.edgeRenderingEnabled = true;
				var color = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(Health / MaxHealth));
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
					Health += MatchReferee.Instance.Settings.healthRegenPerSecond * Time.deltaTime;
			}

			WeaponsManagement.canFire = IsAlive;

			Health = Mathf.Clamp(Health, 0, MaxHealth);

			// bases
			IsInFriendlyBase = false;
			foreach (Base b in Base.AllBases)
			{
				if (b.Team != avatar.Team)
					continue;

				if (!Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
					continue;

				IsInFriendlyBase = true;
				break;
			}

			// network player transforms
			if (avatar != null)
			{
				avatar.HeadTransform.SetWorldPose(headTransform.GetWorldPose());
				avatar.LeftHandTransform.SetWorldPose(leftHandTransform.GetWorldPose());
				avatar.RightHandTransform.SetWorldPose(rightHandTransform.GetWorldPose());

				var spineMid = skeleton.Bones[(int)OVRSkeleton.BoneId.Body_SpineMiddle].Transform;
				avatar.TorsoTransform.SetWorldPose(spineMid.GetWorldPose());

				
			}
		}
	}
}