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
		public event Action Damaged = delegate { };

		public bool IsAlive => isAliveSync.Value;
		public NetworkVariable<bool> isAliveSync = new();

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		private readonly HashSet<Base> basesInside = new();

		public bool IsInBase => basesInside.Count > 0;
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
			Damaged += OnDamaged.Invoke;
			Respawned += OnRespawned.Invoke;

			isAliveSync.OnValueChanged += delegate(bool wasAlive, bool isAlive)
			{
				if (wasAlive && !isAlive)
					Killed.Invoke();
				else if (!wasAlive && isAlive)
					Respawned.Invoke();

				foreach (GameObject g in deactivatedWhenDead) g.SetActive(isAlive);
			};

			teamOwner.TeamChanged += delegate { RefreshBaseState(); };
		}

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				isAliveSync.Value = true;
				Local = this;
			}
			else
			{
				OtherPlayers.Add(this);
			}

			All.Add(OwnerClientId, this);
		}

		public override void OnNetworkDespawn()
		{
			Killed.Invoke();
			OtherPlayers.Remove(this);
			All.Remove(OwnerClientId);

			// A ControlPoint won't reliably get OnTriggerExit if this player despawns
			// (e.g. disconnects) while standing inside its trigger, so proactively
			// remove this player from any that might still be holding a reference.
			foreach (ControlPoint cp in ControlPoint.AllControlPoints)
				cp.RemovePlayer(this);
		}

		internal void EnterBase(Base b)
		{
			basesInside.Add(b);
			RefreshBaseState();

			bool notPlaying = MatchReferee.State != MatchState.Playing;
			if (IsOwner && IsInBase && (notPlaying || Team == 0))
				TeamOwner.teamSync.Value = OccupiedBase.Team;
		}

		internal void ExitBase(Base b)
		{
			basesInside.Remove(b);
			RefreshBaseState();
		}

		private void RefreshBaseState()
		{
			bool inFriendly = false;
			Base occupied = null;


			foreach (Base b in basesInside)
			{
				if (b.Team == Team)
					inFriendly = true;

				occupied = b;
				break;
			}

			OccupiedBase = occupied;
			IsInFriendlyBase = inFriendly;
		}

		public void Damage(IDamageable.Data data)
		{
			DamageRpc(data.damage, data.playerID);
		}

		[Rpc(SendTo.Everyone)]
		public void DamageRpc(float damage, ulong damagedBy)
		{
			if (IsOwner)
				MainPlayer.Instance.Damage(damage, damagedBy);

			Damaged.Invoke();
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