using Anaglyph.Lasertag;
using System.Collections.Generic;
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

		public static List<Player> AllPlayers { get; private set; } = new();
		public static List<Player> OtherPlayers { get; private set; } = new();

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;

		public byte Team => teamOwner.Team;

		public bool IsInFriendlyBase { get; private set; }
		public bool IsInBase { get; private set; }

		private void OnValidate()
		{
			this.SetComponent(ref teamOwner);
		}

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