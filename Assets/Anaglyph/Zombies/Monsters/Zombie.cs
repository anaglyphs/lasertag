using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.Zombies
{
    public class Zombie : MonoBehaviour
    {
		[SerializeField] private float health = 1;

		public static List<ZombieTarget> targets = new();

        private NavMeshAgent agent;

		private ZombieTarget target;

		private void Awake()
		{
			agent = GetComponent<NavMeshAgent>();
			target = targets[0];
		}

		private void FixedUpdate()
		{
			if (Physics.Raycast(target.transform.position, Vector3.down, out RaycastHit hitInfo, 1.7f))
				agent.SetDestination(hitInfo.point);
		}

		public void BulletHit(float damage)
		{
			health -= damage;

			if (health < 0)
				Kill();
		}

		public void Kill()
		{
			Destroy(gameObject);
		}
	}
}
