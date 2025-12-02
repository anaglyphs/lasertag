using System;
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

		private NetworkVariable<ulong> targetId = new(ulong.MaxValue);
		
		private PlayerAvatar target;
	
		private void Awake()
		{
			TryGetComponent(out agent);

			targetId.OnValueChanged += delegate
			{
				PlayerAvatar.All.TryGetValue(targetId.Value, out target);
			};
		}

		public override void OnNetworkSpawn()
		{
			UpdateAgent();
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
					targetId.Value = avatar.OwnerClientId;
					maxDist = dist;
				}
			}

			if (target)
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
	}
}
