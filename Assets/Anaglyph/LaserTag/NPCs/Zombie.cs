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
		private PlayerAvatar target;
	
		private void Awake()
		{
			TryGetComponent(out agent);
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

			target = null;
			float maxDist = float.MaxValue;
			foreach (PlayerAvatar avatar in PlayerAvatar.All.Values)
			{
				if(!avatar.IsAlive) continue;
				
				float dist = Vector3.Distance(head.position, avatar.HeadTransform.position);
				
				if (dist < maxDist)
				{
					target = avatar;
					maxDist = dist;
				}
			}
		}

		private void LateUpdate()
		{
			if (!IsOwner)
				return;
			
			if (target)
			{
				head.LookAt(target.HeadTransform);
				agent.destination = target.HeadTransform.position - Vector3.up * 1.5f;
				
				if (Vector3.Distance(head.position, target.HeadTransform.position) < damageDist)
				{
					target.DamageRpc(101, 0);
				}
			}
		}
	}
}
