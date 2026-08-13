using Anaglyph.LaserTag.Matches;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Weapons;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.LaserTag.NPCs
{
	public class Zombie : NetworkBehaviour, IDamageable
	{
		[SerializeField] private Transform head;
		[SerializeField] private float damageDist;

		[Header("Erratic movement")]
		[SerializeField] private float strafeDistance = 2f;
		[SerializeField] private float strafeFrequency = 0.6f;
		[SerializeField] private float speedVariation = 0.35f;

		private NavMeshAgent agent;
		private float noiseSeed;
		private float baseSpeed;

		private NetworkVariable<ulong> targetIdSync = new(ulong.MaxValue);
		private NetworkVariable<float> healthSync = new(MatchSettings.MaxHealth);
		public float Health => healthSync.Value;

		private PlayerAvatar target;

		private void Awake()
		{
			TryGetComponent(out agent);
			baseSpeed = agent.speed;
			noiseSeed = Random.Range(0f, 1000f);

			targetIdSync.OnValueChanged += delegate { PlayerAvatar.All.TryGetValue(targetIdSync.Value, out target); };
		}

		public override void OnNetworkSpawn()
		{
			UpdateAgent();
			healthSync.Value = MatchSettings.MaxHealth;
			
			MatchReferee.StateChanged += OnMatchStateChange;
		}

		public override void OnNetworkDespawn()
		{
			MatchReferee.StateChanged -= OnMatchStateChange;
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
				Vector3 targetPos = target.HeadTransform.position - Vector3.up * 1.5f;
				Vector3 toTarget = targetPos - transform.position;
				Vector3 sideways = Vector3.Cross(Vector3.up, toTarget).normalized;

				float noiseTime = Time.time * strafeFrequency;
				float strafe = Mathf.PerlinNoise(noiseTime, noiseSeed) * 2f - 1f;
				float speedNoise = Mathf.PerlinNoise(noiseSeed, noiseTime) * 2f - 1f;

				// taper the weaving as it closes in so it can still land a hit
				float strafeFalloff = Mathf.Clamp01((toTarget.magnitude - damageDist) / strafeDistance);

				agent.destination = targetPos + sideways * (strafe * strafeDistance * strafeFalloff);
				agent.speed = baseSpeed * (1f + speedNoise * speedVariation);

				if (Vector3.Distance(head.position, target.HeadTransform.position) < damageDist)
					target.DamageRpc(101, 0);
			}
		}

		private void LateUpdate()
		{
			if (target) head.LookAt(target.HeadTransform);
		}

		[Rpc(SendTo.Owner)]
		private void ShotRpc(IDamageable.Data data)
		{
			healthSync.Value -= data.damage;

			if (Health <= 0)
			{
				NetworkObject.Despawn(true);
				
				if(PlayerAvatar.All.TryGetValue(data.playerID, out PlayerAvatar killer))
					MatchReferee.Instance.Score(killer.Team, MatchReferee.Settings.pointsPerZombieKill);
			}
		}

		public void Damage(IDamageable.Data data)
		{
			ShotRpc(data);
		}
	}
}