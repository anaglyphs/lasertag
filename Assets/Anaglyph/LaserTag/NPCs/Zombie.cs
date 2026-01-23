using Anaglyph.Lasertag;
using Anaglyph.Lasertag.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.LaserTag.NPCs
{
	public class Zombie : NetworkBehaviour
	{
		[SerializeField] private Transform head;
		[SerializeField] private float damageDist;

		private NavMeshAgent agent;

		private NetworkVariable<ulong> targetIdSync = new(ulong.MaxValue);
		private float health = 100;
		
		private PlayerAvatar target;
	
		private void Awake()
		{
			TryGetComponent(out agent);

			targetIdSync.OnValueChanged += delegate
			{
				PlayerAvatar.All.TryGetValue(targetIdSync.Value, out target);
			};
		}

		public override void OnNetworkSpawn()
		{
			UpdateAgent();
			health = 100;
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
				if(!avatar.IsAlive) continue;
				
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
				{
					target.DamageRpc(101, 0);
				}
			}
		}

		private void LateUpdate()
		{
			if (target)
			{
				head.LookAt(target.HeadTransform);
			}
		}

		public void OnShot(Bullet.DamageData damageData)
		{
			ShotRpc(damageData.damage);
		}

		[Rpc(SendTo.Everyone)]
		private void ShotRpc(float damage)
		{
			health -= damage;

			if (IsOwner && health <= 0)
			{
				NetworkObject.Despawn(true);
			}
		}
	}
}
