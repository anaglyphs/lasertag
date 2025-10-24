using Anaglyph.Lasertag.Networking;
using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using System;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

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
		public bool redDamagedVision = true;
		public bool isParticipating { get; private set; } = true;

		private void Awake()
		{
			Instance = this;

			passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>();

			isParticipating = XRSettings.enabled;
		}

		private void Start()
		{
			NetworkManager.Singleton.OnConnectionEvent += HandleConnectionEvent;
		}

		private void OnDestroy()
		{
			if (NetworkManager.Singleton != null)
				NetworkManager.Singleton.OnConnectionEvent -= HandleConnectionEvent;
		}

		private void HandleConnectionEvent(NetworkManager manager, ConnectionEventData eventData)
		{
			if (NetcodeManagement.ThisClientConnected(eventData))
				HandleAvatar();
		}

		public void SetIsParticipating(bool isParticipating)
		{
			this.isParticipating = isParticipating;
			HandleAvatar();
			Respawn();

			if (!isParticipating)
			{
				WeaponsManagement.canFire = false;
			}
		}

		private void HandleAvatar()
		{
			if (NetworkManager.Singleton.IsConnectedClient && isParticipating && avatar == null)
			{
				SpawnAvatar();
			}
			else if (!NetworkManager.Singleton.IsConnectedClient || !isParticipating && avatar != null)
			{
				avatar?.NetworkObject.Despawn();
			}
		}

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

				if (MatchReferee.State == MatchState.Playing && killer.Team != avatar.Team)
				{
					var referee = MatchReferee.Instance;
					referee.ScoreTeamRpc(killer.Team, referee.Settings.pointsPerKill);
				}
			}
		}

		public void Respawn()
		{
			if (IsAlive) return;

			ClearPassthroughEffects();

			WeaponsManagement.canFire = true;

			if (avatar != null)
				avatar.isAliveSync.Value = true;

			IsAlive = true;
			Health = MaxHealth;

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

		private void ClearPassthroughEffects()
		{
			passthroughLayer.edgeRenderingEnabled = false;
			passthroughLayer.edgeColor = Color.clear;
		}

		private void Update()
		{
			// health
			if (avatar == null)
				return;

			if (redDamagedVision)
			{
				passthroughLayer.edgeRenderingEnabled = true;
				var color = Color.Lerp(Color.red, Color.clear, Mathf.Clamp01(Health / MaxHealth));
				passthroughLayer.edgeColor = color;
			}
			else
			{
				ClearPassthroughEffects();
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
			foreach (Base teamBase in Base.AllBases)
			{
				if (Geo.PointIsInCylinder(teamBase.transform.position, Base.Radius, 3, headTransform.position))
				{
					if (MatchReferee.State != MatchState.Playing || avatar.Team == 0)
					{
						avatar.TeamOwner.teamSync.Value = teamBase.Team;
					}
					else if (avatar.Team != teamBase.Team)
					{
						continue;
					}

					IsInFriendlyBase = true;
					break;
				}
			}

			// network player transforms
			avatar.HeadTransform.SetWorldPose(headTransform.GetWorldPose());
			avatar.LeftHandTransform.SetWorldPose(leftHandTransform.GetWorldPose());
			avatar.RightHandTransform.SetWorldPose(rightHandTransform.GetWorldPose());

			var spineMid = skeleton.Bones[(int)OVRSkeleton.BoneId.Body_SpineMiddle].Transform;
			avatar.TorsoTransform.SetWorldPose(spineMid.GetWorldPose());
			// wtf
		}
	}
}
