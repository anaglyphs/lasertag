using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Networking
{
	[DefaultExecutionOrder(500)]
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

		public int Team => teamSync.Value;
		public NetworkVariable<int> teamSync = new();

		public static List<Player> AllPlayers { get; private set; } = new();
		public static List<Player> OtherPlayers { get; private set; } = new();

		private void Awake()
		{
            isAliveSync.OnValueChanged += (wasAlive, isAlive) =>
			{
				if (isAlive)
					onRespawn.Invoke();
				else
					onKilled.Invoke();
			};

			AllPlayers.Add(this);
			OtherPlayers.Add(this);
		}

		public override void OnNetworkSpawn()
        {
			isAliveSync.Value = true;
			isAliveSync.OnValueChanged.Invoke(isAliveSync.Value, isAliveSync.Value);

			if (IsOwner)
				MainPlayer.Instance.activeNetworkPlayer = this;
			else
				OtherPlayers.Add(this);

            AllPlayers.Add(this);
        }

		[Rpc(SendTo.Everyone)]
		public void HitRpc(float damage)
		{
			onDamaged.Invoke();

			if (IsOwner)
				MainPlayer.Instance.Damage(damage);
        }

		public override void OnDestroy()
		{
			base.OnDestroy();

			AllPlayers.Remove(this);
			OtherPlayers.Remove(this);
		}
	}
}