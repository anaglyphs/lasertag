using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(-500)]
	public class Avatar : NetworkBehaviour
	{
		public const string Tag = "Player";

		[SerializeField] private Transform headTransform;
		[SerializeField] private Transform leftHandTransform;
		[SerializeField] private Transform rightHandTransform;
		//[SerializeField] private Transform torsoTransform;
		public Transform HeadTransform => headTransform;
		public Transform LeftHandTransform => leftHandTransform;
		public Transform RightHandTransform => rightHandTransform;
		//public Transform TorsoTransform => torsoTransform;

		public UnityEvent onRespawn = new();
		public UnityEvent onKilled = new();
		public UnityEvent onDamaged = new();

		public bool IsAlive => isAliveSync.Value;
		public NetworkVariable<bool> isAliveSync = new();

		public static Dictionary<ulong, Avatar> AllPlayers { get; private set; } = new();
		public static List<Avatar> OtherPlayers { get; private set; } = new();

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		public bool IsInFriendlyBase { get; private set; }
		public bool IsInBase { get; private set; }
		public Base InBase { get; private set; }

		public static event Action<Avatar, Avatar> OnPlayerKilledPlayer = delegate { };

		public NetworkVariable<int> scoreSync;
		public int Score => scoreSync.Value;

		private void Awake()
		{
			isAliveSync.OnValueChanged += delegate (bool wasAlive, bool isAlive)
			{
				if (wasAlive && !isAlive)
					onKilled.Invoke();
				else if (!wasAlive && isAlive)
					onRespawn.Invoke();
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
				MainPlayer.Instance.networkPlayer = this;
			}

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
			
			foreach (Base b in Base.AllBases)
			{
				if (Geo.PointIsInCylinder(b.transform.position, Base.Radius, 3, headTransform.position))
				{
					IsInBase = true;
					InBase = b;

					if (Team == b.Team)
						IsInFriendlyBase = true;

					return;
				}
			}

			InBase = null;
			IsInBase = false;
			IsInFriendlyBase = false;
		}

		private void Update()
		{
			HandleBases();
		}

		[Rpc(SendTo.Everyone)]
		public void DamageRpc(float damage, ulong damagedBy)
		{
			if(IsOwner)
				MainPlayer.Instance.Damage(damage, damagedBy);

			onDamaged.Invoke();
		}

		[Rpc(SendTo.Everyone)]
		public void KilledByPlayerRpc(ulong killerId) {

			if(AllPlayers.TryGetValue(killerId, out Avatar killer))
				OnPlayerKilledPlayer.Invoke(killer, this);
		}

		[Rpc(SendTo.Owner)]
		public void ResetScoreRpc()
		{
			scoreSync.Value = 0;
		}
	}
}