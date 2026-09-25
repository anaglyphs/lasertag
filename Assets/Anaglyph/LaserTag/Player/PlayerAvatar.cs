using System;
using System.Collections.Generic;
using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Objects.Gameplay.Base;
using Anaglyph.LaserTag.Objects.Gameplay.Control_Point;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.LaserTag.Weapons;
using Anaglyph.LaserTag.Weapons.Bullets;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Player
{
	[DefaultExecutionOrder(-500)]
	[RequireComponent(typeof(PlayerHeadsetStatus))]
	public class PlayerAvatar : NetworkBehaviour, IDamageable, IProjectileDamageReceiver
	{
		public const string Tag = "Player";

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;

		// [SerializeField] private Transform torsoTransform;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform;
		public Transform RightHandTransform => rightHandTransform;
		// public Transform TorsoTransform => torsoTransform;

		public UnityEvent OnRespawned = new();
		public event Action Respawned = delegate { };

		public UnityEvent OnKilled = new();
		public event Action Killed = delegate { };

		public UnityEvent OnDamaged = new();
		public event Action<float, ulong> Damaged = delegate { };

		public bool IsAlive => isAliveSync.Value;
		private readonly NetworkVariable<bool> isAliveSync = new(true);
		private struct ParticipationState : INetworkSerializeByMemcpy
		{
			public bool participating;
			public bool enteredPlay;
		}
		private readonly NetworkVariable<ParticipationState> participationSync = new();
		public PlayerHeadsetStatus HeadsetStatus { get; private set; }
		public bool IsAligned => HeadsetStatus != null && HeadsetStatus.IsAligned;
		public bool IsParticipating => IsSpawned && (IsOwner
			? PlayerAvatarSpawner.Instance != null && PlayerAvatarSpawner.Instance.IsParticipating
			: participationSync.Value.participating);
		/// <summary>World-space features are valid even when dead, for base-based respawning.</summary>
		public bool HasSpatialPresence => IsParticipating && IsAligned;
		public bool CanInteract => HasSpatialPresence && IsAlive;
		/// <summary>Alignment loss does not remove an established player from elimination accounting.</summary>
		public bool IsRoundParticipant => IsParticipating && participationSync.Value.enteredPlay && Team != 0;
		public event Action<bool> SpatialPresenceChanged = delegate { };
		private bool hadSpatialPresence;

		/// <summary>Owner only - the local player's life is the source of truth.</summary>
		internal void SetAlive(bool isAlive) => isAliveSync.Value = isAlive;

		private readonly NetworkVariable<float> healthSync = new(MatchSettings.MaxHealth);

		/// <summary>
		/// Follows the owner's authoritative health, but drops the instant a hit lands here so
		/// hit feedback doesn't wait a round trip. Weapons fire faster than the owner can answer,
		/// so reading the synced value alone would report several bullets in a row as the same hit.
		/// </summary>
		public float Health { get; private set; } = MatchSettings.MaxHealth;

		/// <summary>Owner only - the local player's health is the source of truth.</summary>
		internal void SetHealth(float health) => healthSync.Value = health;

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		public bool IsInBase => OccupiedBase != null;
		public bool IsInFriendlyBase { get; private set; }
		public Action<bool> InFriendlyBaseChanged = delegate { };
		public Base OccupiedBase { get; private set; }

		public NetworkVariable<int> scoreSync;
		public int Score => scoreSync.Value;

		/// <summary>Device diagnostics for the operator panel. Sampled by the owner.</summary>
		public HeadsetTelemetry Telemetry => HeadsetStatus != null ? HeadsetStatus.Telemetry : HeadsetTelemetry.Unknown;

		public static PlayerAvatar Local { get; private set; }
		public static Dictionary<ulong, PlayerAvatar> All { get; private set; } = new();
		public static List<PlayerAvatar> OtherPlayers { get; private set; } = new();
		public static event Action<PlayerAvatar, PlayerAvatar> OnPlayerKilledPlayer = delegate { };

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			All = new Dictionary<ulong, PlayerAvatar>();
			OtherPlayers = new List<PlayerAvatar>();
			Local = null;
			OnPlayerKilledPlayer = delegate { };
		}

		private void Awake()
		{
			HeadsetStatus = GetComponent<PlayerHeadsetStatus>();
			Killed += delegate { if (HasSpatialPresence) OnKilled.Invoke(); };
			Damaged += delegate { if (HasSpatialPresence) OnDamaged.Invoke(); };
			Respawned += delegate { if (HasSpatialPresence) OnRespawned.Invoke(); };

			healthSync.OnValueChanged += delegate(float _, float health) { Health = health; };

			isAliveSync.OnValueChanged += delegate(bool wasAlive, bool isAlive)
			{
				if (wasAlive && !isAlive)
					Killed.Invoke();
				else if (!wasAlive && isAlive)
					Respawned.Invoke();
			};
		}

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				ProjectileBarrierHistory.Local.Clear();
				Local = this;
			}
			else
				OtherPlayers.Add(this);

			All[OwnerClientId] = this;

			// synced values arrive before spawn, so nothing raises OnValueChanged for them
			Health = healthSync.Value;
			RefreshParticipation();
		}

		public override void OnNetworkDespawn()
		{
			if (IsOwner) ProjectileBarrierHistory.Local.Clear();
			SetSpatialPresence(false);
			OtherPlayers.Remove(this);
			All.Remove(OwnerClientId);

			// leaving this pointing at a destroyed avatar makes every `Local?.` call throw
			if (Local == this)
				Local = null;

			// A ControlPoint won't reliably get OnTriggerExit if this player despawns
			// (e.g. disconnects) while standing inside its trigger, so proactively
			// remove this player from any that might still be holding a reference.
			foreach (ControlPoint cp in ControlPoint.AllControlPoints)
				cp.RemovePlayer(this);
		}

		private void Update()
		{
			if (!IsSpawned)
				return;

			RefreshParticipation();
			RefreshBaseState();
		}

		internal void RefreshParticipation()
		{
			if (!IsSpawned) return;
			if (IsOwner) participationSync.Value = new ParticipationState
			{
				participating = IsParticipating,
				enteredPlay = participationSync.Value.enteredPlay || HasSpatialPresence
			};
			SetSpatialPresence(HasSpatialPresence);
		}

		private void SetSpatialPresence(bool present)
		{
			if (hadSpatialPresence == present) return;
			hadSpatialPresence = present;
			if (!present)
			{
				OccupiedBase = null;
				if (IsInFriendlyBase)
				{
					IsInFriendlyBase = false;
					InFriendlyBaseChanged.Invoke(false);
				}
				foreach (ControlPoint cp in ControlPoint.AllControlPoints) cp.RemovePlayer(this);
			}
			SpatialPresenceChanged.Invoke(present);
		}

		private void RefreshBaseState()
		{
			bool inFriendly = false;
			Base occupied = null;

			foreach (Base b in Base.AllBases)
			{
				if (!HasSpatialPresence) break;
				if (!b.Contains(headTransform.position))
					continue;

				if (b.Team == Team)
				{
					occupied = b;
					inFriendly = true;
					break;
				}

				occupied ??= b;
			}

			Base previous = OccupiedBase;
			OccupiedBase = occupied;

			bool notPlaying = MatchReferee.State != MatchState.Playing;
			if (IsOwner && occupied != null && occupied != previous && (notPlaying || Team == 0))
				TeamOwner.teamSync.Value = occupied.Team;

			if (IsInFriendlyBase == inFriendly)
				return;

			IsInFriendlyBase = inFriendly;
			InFriendlyBaseChanged.Invoke(inFriendly);
		}

		public void Damage(IDamageable.Data data)
		{
			if (!CanInteract) return;
			DamageRpc(data.damage, data.playerID);
		}

		[Rpc(SendTo.Everyone)]
		public void DamageRpc(float damage, ulong damagedBy)
			=> ApplyDamage(damage, damagedBy);

		public void ReceiveProjectileHit(ProjectileHit hit)
		{
			if (IsSpawned) RequestProjectileDamageRpc(hit);
		}

		[Rpc(SendTo.Owner)]
		private void RequestProjectileDamageRpc(ProjectileHit hit, RpcParams rpc = default)
		{
			if (!IsOwner || !CanInteract || rpc.Receive.SenderClientId != hit.damage.playerID) return;
			if (hit.canBeBlocked && ProjectileBarrierHistory.Local.Blocked(OwnerClientId, hit.ray,
				hit.speed, hit.shotTime, hit.distance, hit.radius, Anaglyph.Netcode.SharedNetworkTime.TimeAsFloat))
				return;
			ApplyProjectileDamageRpc(hit.damage, hit.position);
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void ApplyProjectileDamageRpc(IDamageable.Data data, Vector3 position)
		{
			if (!ApplyDamage(data.damage, data.playerID)) return;
			if (NetworkManager.LocalClientId == data.playerID)
				IDamageable.NotifyDamageDealt(position, this, data);
		}

		private bool ApplyDamage(float damage, ulong damagedBy)
		{
			if (!CanInteract) return false;
			Health = Mathf.Max(0, Health - MatchReferee.Settings.ApplyDamageMultiplier(damage));

			Damaged.Invoke(damage, damagedBy);
			return true;
		}

		[Rpc(SendTo.Everyone)]
		public void KilledByPlayerRpc(ulong killerId)
		{
			if (All.TryGetValue(killerId, out PlayerAvatar killer))
				OnPlayerKilledPlayer.Invoke(killer, this);
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoreRpc()
		{
			scoreSync.Value = 0;
		}

		public void ResetScoreLocally()
		{
			scoreSync.Value = 0;
		}
	}
}
