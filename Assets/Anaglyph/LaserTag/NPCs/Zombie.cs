using Anaglyph.Lasertag;
using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.LaserTag.NPCs
{
	public class Zombie : NetworkBehaviour, IDamageable
	{
		[SerializeField] private Transform head;
		[SerializeField] private float damageDist;

		private NavMeshAgent agent;

		private NetworkVariable<ulong> targetIdSync = new(ulong.MaxValue);
		private NetworkVariable<float> healthSync = new(MatchSettings.MaxHealth);
		public float Health => healthSync.Value;

		private PlayerAvatar target;

		private void Awake()
		{
			TryGetComponent(out agent);

			targetIdSync.OnValueChanged += delegate { PlayerAvatar.All.TryGetValue(targetIdSync.Value, out target); };
		}

		public override void OnNetworkSpawn()
		{
			UpdateAgent();
			healthSync.Value = MatchSettings.MaxHealth;
			
			MatchReferee.StateChanged += OnMatchStateChange;
		}

		private void OnMatchStateChange(MatchState state)
		{
			if (!IsOwner)
				return;
			
			if (state != MatchState.Playing)
				NetworkObject.Despawn(true);
		}

		public override void OnGainedOwnership()
		{
			UpdateAgent();
		}

		private void UpdateAgent()
		{
			agent.enabled = IsOwner;
		}

		private void FixedUpdate()
		{
			if (!IsOwner)
				return;

			float maxDist = float.MaxValue;
			foreach (PlayerAvatar avatar in PlayerAvatar.All.Values)
			{
				if (!avatar.IsAlive) continue;

				float dist = Vector3.Distance(head.position, avatar.HeadTransform.position);

				if (dist < maxDist)
				{
					targetIdSync.Value = avatar.OwnerClientId;
					maxDist = dist;
				}
			}

			if (target && target.IsAlive)
			{
				agent.destination = target.HeadTransform.position - Vector3.up * 1.5f;

				if (Vector3.Distance(head.position, target.HeadTransform.position) < damageDist)
					target.DamageRpc(101, 0);
			}
		}

		private void LateUpdate()
		{
			if (target) head.LookAt(target.HeadTransform);
		}

		[Rpc(SendTo.Owner)]
		private void ShotRpc(float damage)
		{
			healthSync.Value -= damage;

			if (IsOwner && Health <= 0) NetworkObject.Despawn(true);
		}

		public void Damage(IDamageable.Data data)
		{
			ShotRpc(data.damage);
		}
	}
}