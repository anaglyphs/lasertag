using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(-500)]
	public class PlayerAvatar : NetworkBehaviour, IDamageable
	{
		public const string Tag = "Player";

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		[SerializeField] private GameObject[] deactivatedWhenDead = Array.Empty<GameObject>();

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

		public static PlayerAvatar Local { get; private set; }
		public static Dictionary<ulong, PlayerAvatar> All { get; private set; } = new();
		public static List<PlayerAvatar> OtherPlayers { get; private set; } = new();
		public static event Action<PlayerAvatar, PlayerAvatar> OnPlayerKilledPlayer = delegate { };

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init()
		{
			All = new Dictionary<ulong, PlayerAvatar>();
			OtherPlayers = new List<PlayerAvatar>();
			OnPlayerKilledPlayer = delegate { };
		}

		private void Awake()
		{
			Killed += OnKilled.Invoke;
			Damaged += delegate { OnDamaged.Invoke(); };
			Respawned += OnRespawned.Invoke;

			healthSync.OnValueChanged += delegate(float _, float health) { Health = health; };

			isAliveSync.OnValueChanged += delegate(bool wasAlive, bool isAlive)
			{
				if (wasAlive && !isAlive)
					Killed.Invoke();
				else if (!wasAlive && isAlive)
					Respawned.Invoke();

				ApplyAliveState();
			};
		}

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				Local = this;
			else
				OtherPlayers.Add(this);

			All[OwnerClientId] = this;

			// synced values arrive before spawn, so nothing raises OnValueChanged for them
			Health = healthSync.Value;
			ApplyAliveState();
		}

		public override void OnNetworkDespawn()
		{
			Killed.Invoke();
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
			if (IsSpawned)
				RefreshBaseState();
		}

		private void RefreshBaseState()
		{
			bool inFriendly = false;
			Base occupied = null;

			foreach (Base b in Base.AllBases)
			{
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

		private void ApplyAliveState()
		{
			foreach (GameObject g in deactivatedWhenDead) g.SetActive(IsAlive);
		}

		public void Damage(IDamageable.Data data)
		{
			DamageRpc(data.damage, data.playerID);
		}

		[Rpc(SendTo.Everyone)]
		public void DamageRpc(float damage, ulong damagedBy)
		{
			Health = Mathf.Max(0, Health - MatchReferee.Settings.ApplyDamageMultiplier(damage));

			Damaged.Invoke(damage, damagedBy);
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