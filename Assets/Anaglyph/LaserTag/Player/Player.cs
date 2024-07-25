using Anaglyph.Lasertag;
using OVR.OpenVR;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Networking
{
	[DefaultExecutionOrder(-500)]
	public class Player : NetworkBehaviour
	{
		public const string Tag = "Player";

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform;
		public Transform RightHandTransform => rightHandTransform;

		public UnityEvent onRespawn = new();
		public UnityEvent onKilled = new();
		public UnityEvent onDamaged = new();

		public bool IsAlive => isAliveSync.Value;
		public NetworkVariable<bool> isAliveSync = new();

		public string GetNickname() => nicknameSync.Value.ToString();
		public NetworkVariable<FixedString32Bytes> nicknameSync;

		public static Dictionary<ulong, Player> AllPlayers { get; private set; } = new();
		public static List<Player> OtherPlayers { get; private set; } = new();

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		public bool IsInFriendlyBase { get; private set; }
		public bool IsInBase { get; private set; }

		public static event Action<Player, Player> OnPlayerKilledPlayer = delegate { };
		public static void InvokePlayerKilledPlayer(Player killer, Player victim) => OnPlayerKilledPlayer.Invoke(killer, victim);

		private void OnValidate()
		{
			this.SetComponent(ref teamOwner);
		}

		public override void OnNetworkSpawn()
        {
			isAliveSync.Value = true;

			if (IsOwner)
				MainPlayer.Instance.networkPlayer = this;

            AllPlayers.Add(OwnerClientId, this);
			OtherPlayers.Add(this);
		}

		public override void OnNetworkDespawn()
		{
			OtherPlayers.Remove(this);
			AllPlayers.Remove(OwnerClientId);
		}

		private void HandleBases()
		{
			IsInBase = false;
			IsInFriendlyBase = false;
			foreach (Base b in Base.AllBases)
			{
				if (Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
				{
					IsInBase = true;
					if (Team == b.Team)
						IsInFriendlyBase = true;
				}
			}
		}

		private void Update()
		{
			HandleBases();
		}

		[Rpc(SendTo.Everyone)]
		public void DamageRpc(float damage, ulong damagedBy)
		{
			onDamaged.Invoke();

			if (IsOwner)
				MainPlayer.Instance.Damage(damage, damagedBy);
        }

		[Rpc(SendTo.Everyone)]
		public void KilledRpc(ulong killerId) {
			onKilled.Invoke();

			if(AllPlayers.TryGetValue(killerId, out Player killer))
				OnPlayerKilledPlayer.Invoke(killer, this);
		}

		[Rpc(SendTo.Everyone)]
		public void RespawnRpc() => onRespawn.Invoke();
	}
}