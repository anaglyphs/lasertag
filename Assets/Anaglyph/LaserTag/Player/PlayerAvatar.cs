using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(-500)]
	public class PlayerAvatar : NetworkBehaviour
	{
		public const string Tag = "Player";

		public static PlayerAvatar Local { get; private set; }

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

		public static Dictionary<ulong, PlayerAvatar> All { get; private set; } = new();
		public static List<PlayerAvatar> OtherPlayers { get; private set; } = new();

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		public bool IsInFriendlyBase { get; private set; }
		public bool IsInBase { get; private set; }
		public Base InBase { get; private set; }

		public static event Action<PlayerAvatar, PlayerAvatar> OnPlayerKilledPlayer = delegate { };

		public NetworkVariable<int> scoreSync;
		public int Score => scoreSync.Value;

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

				foreach (var g in deactivatedWhenDead) g.SetActive(isAlive);
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
				isAliveSync.Value = true;
				Local = this;
			}

			All.Add(OwnerClientId, this);
			OtherPlayers.Add(this);
		}

		public override void OnNetworkDespawn()
		{
			Killed.Invoke();
			OtherPlayers.Remove(this);
			All.Remove(OwnerClientId);
		}

		private void HandleBases()
		{
			foreach (var b in Base.AllBases)
				if (Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
				{
					IsInBase = true;
					InBase = b;

					if (Team == b.Team)
						IsInFriendlyBase = true;

					return;
				}

			InBase = null;
			IsInBase = false;
			IsInFriendlyBase = false;
		}

		private void Update()
		{
			HandleBases();
		}

		public void OnShot(Bullet.DamageData damageData)
		{
			DamageRpc(damageData.damage, damageData.playerID);
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
			if (All.TryGetValue(killerId, out var killer))
				OnPlayerKilledPlayer.Invoke(killer, this);
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoreRpc()
		{
			scoreSync.Value = 0;
		}
	}
}