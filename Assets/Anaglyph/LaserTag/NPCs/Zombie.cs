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
		[SerializeField] private CapsuleCollider damageTrigger;

		[Header("Navigation")]
		[SerializeField, Min(0f)] private float targetRefreshInterval = 0.5f;

		[SerializeField] private float immediateUpdateWithinDist = 5f;

		private NavMeshAgent agent;
		private float nextTargetRefreshTime;
		private Vector3 lastRepathStartPosition;
		private ulong lastPathTargetId = ulong.MaxValue;

		private readonly NetworkVariable<ulong> targetIdSync = new(ulong.MaxValue);
		private readonly NetworkVariable<float> healthSync = new(MatchSettings.MaxHealth);
		public float Health => healthSync.Value;

		private PlayerAvatar cachedTarget;
		private ulong cachedTargetId = ulong.MaxValue;

		// re-resolves only when the synced id changes or the cached avatar despawns
		private PlayerAvatar Target
		{
			get
			{
				if (cachedTargetId == targetIdSync.Value && cachedTarget)
					return cachedTarget;

				cachedTargetId = targetIdSync.Value;
				PlayerAvatar.All.TryGetValue(cachedTargetId, out cachedTarget);
				return cachedTarget;
			}
		}

		private void Awake()
		{
			TryGetComponent(out agent);

			if (!damageTrigger)
				TryGetComponent(out damageTrigger);

			damageTrigger.enabled = false;
		}

		public override void OnNetworkSpawn()
		{
			UpdateAgent();

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

		public override void OnLostOwnership()
		{
			UpdateAgent();
		}

		private void UpdateAgent()
		{
			agent.enabled = IsOwner;
			damageTrigger.enabled = IsOwner;

			if (IsOwner)
			{
				nextTargetRefreshTime = 0f;
				lastPathTargetId = ulong.MaxValue;
			}
		}

		private void Update()
		{
			if (!IsOwner)
				return;

			if (Time.time >= nextTargetRefreshTime)
				RefreshTarget();
		}

		private void RefreshTarget()
		{
			nextTargetRefreshTime = Time.time + targetRefreshInterval;

			ulong nearestId = ulong.MaxValue;
			float nearestDistSqr = float.MaxValue;
			foreach (PlayerAvatar avatar in PlayerAvatar.All.Values)
			{
				if (!avatar.IsAlive) continue;

				float distSqr = (head.position - avatar.HeadTransform.position).sqrMagnitude;

				if (distSqr < nearestDistSqr)
				{
					nearestId = avatar.OwnerClientId;
					nearestDistSqr = distSqr;
				}
			}

			if (targetIdSync.Value != nearestId)
				targetIdSync.Value = nearestId;
		}

		private void UpdatePath(PlayerAvatar target)
		{
			if (!agent.enabled || !agent.isOnNavMesh)
				return;

			Vector3 targetPos = target.HeadTransform.position - Vector3.up * 1.5f;
			bool targetChanged = lastPathTargetId != target.OwnerClientId;
			bool pathNeedsRefresh = !agent.hasPath || agent.isPathStale;

			if (!targetChanged && !pathNeedsRefresh)
			{
				float distanceTraveled = Vector3.Distance(lastRepathStartPosition, transform.position);
				float distanceToTarget = Vector3.Distance(lastRepathStartPosition, targetPos);
				if (distanceTraveled < distanceToTarget * 0.5f || distanceTraveled < immediateUpdateWithinDist)
					return;
			}

			if (agent.pathPending)
				return;

			if (agent.SetDestination(targetPos))
			{
				lastRepathStartPosition = transform.position;
				lastPathTargetId = target.OwnerClientId;
			}
		}

		private void ClearPath()
		{
			if (agent.enabled && agent.isOnNavMesh && agent.hasPath)
				agent.ResetPath();

			lastPathTargetId = ulong.MaxValue;
		}

		private void OnTriggerEnter(Collider other)
		{
			if (!IsOwner || !other.CompareTag(PlayerAvatar.Tag))
				return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player && player.IsAlive)
				player.DamageRpc(101, 0);
		}

		private void LateUpdate()
		{
			PlayerAvatar target = Target;
			if (target) head.LookAt(target.HeadTransform);

			if (!IsOwner)
				return;

			if (target && target.IsAlive)
				UpdatePath(target);
			else
				ClearPath();
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
