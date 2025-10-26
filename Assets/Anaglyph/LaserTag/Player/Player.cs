using Anaglyph.Lasertag.Networking;
using Anaglyph.Lasertag.Weapons;
using Anaglyph.Netcode;
using System;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.Lasertag
{
	[DefaultExecutionOrder(-100)]
	public class Player : MonoBehaviour
	{
		public static Player Instance { get; private set; }

		private const float MaxHealth = 100;

		public float Health { get; private set; } = MaxHealth;
		public bool IsAlive { get; private set; } = true;
		public bool IsInFriendlyBase { get; private set; } = false;

		public event Action Died = delegate { };
		public event Action Respawned = delegate { };
		public event Action Damaged = delegate { };
		public event Action<byte> TeamChanged = delegate { };

		[SerializeField] private GameObject avatarPrefab;

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

			NetcodeManagement.StateChanged += OnNetworkStateChange;
			MatchReferee.StateChanged += OnMatchStateChange;
		}

		private void OnDestroy()
		{
			NetcodeManagement.StateChanged -= OnNetworkStateChange;
			MatchReferee.StateChanged -= OnMatchStateChange;
		}

		private void OnNetworkStateChange(NetcodeState state)
		{
			if(state == NetcodeState.Connected)
				HandleAvatar();
		}

		private void OnMatchStateChange(MatchState state)
		{
			Respawn();
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
			if (NetcodeManagement.State == NetcodeState.Connected && isParticipating && PlayerAvatar.Local == null)
			{
				SpawnAvatar();
			}
			else if (NetcodeManagement.State != NetcodeState.Connected || !isParticipating && PlayerAvatar.Local != null)
			{
				PlayerAvatar.Local?.NetworkObject.Despawn();
			}
		}

		private void SpawnAvatar()
		{
			var manager = NetworkManager.Singleton;
			if (!manager.IsConnectedClient)
				return;

			NetworkObject.InstantiateAndSpawn(avatarPrefab,
				manager, manager.LocalClientId, destroyWithScene: true, isPlayerObject: true);
			
			PlayerAvatar.Local.TeamOwner.OnTeamChange.AddListener(TeamChanged.Invoke);
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

			PlayerAvatar.Local.isAliveSync.Value = false;

			IsAlive = false;
			Health = 0;
			RespawnTimerSeconds = MatchReferee.Instance.Settings.respawnSeconds;

			Died.Invoke();

			if (PlayerAvatar.All.TryGetValue(killerID, out var killer))
			{
				PlayerAvatar.Local.KilledByPlayerRpc(killerID);

				if (MatchReferee.State == MatchState.Playing && killer.Team != PlayerAvatar.Local.Team)
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

			if (PlayerAvatar.Local)
				PlayerAvatar.Local.isAliveSync.Value = true;

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
			if (!PlayerAvatar.Local)
				return;
			
			
			
			// health
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
					if (MatchReferee.State != MatchState.Playing || PlayerAvatar.Local.Team == 0)
					{
						PlayerAvatar.Local.TeamOwner.teamSync.Value = teamBase.Team;
					}
					else if (PlayerAvatar.Local.Team != teamBase.Team)
					{
						continue;
					}

					IsInFriendlyBase = true;
					break;
				}
			}

			// network player transforms
			PlayerAvatar.Local.HeadTransform.SetWorldPose(headTransform.GetWorldPose());
			PlayerAvatar.Local.LeftHandTransform.SetWorldPose(leftHandTransform.GetWorldPose());
			PlayerAvatar.Local.RightHandTransform.SetWorldPose(rightHandTransform.GetWorldPose());

			var spineMid = skeleton.Bones[(int)OVRSkeleton.BoneId.Body_SpineMiddle].Transform;
			PlayerAvatar.Local.TorsoTransform.SetWorldPose(spineMid.GetWorldPose());
			// wtf
		}
	}
}
