using UnityEngine;
using UnityEngine.AI;

namespace Anaglyph.Zombies
{
    public class Skeleton : MonoBehaviour
    {
        private NavMeshAgent agent;
		public Transform playerHead;

		private void Awake()
		{
			agent = GetComponent<NavMeshAgent>();
		}

		private void FixedUpdate()
		{
			if (GetTargetPos(out Vector3 target))
				agent.SetDestination(target);
		}

		public void Respawn()
		{
			if(GetTargetPos(out Vector3 target))
				agent.nextPosition = target;
		}

		private bool GetTargetPos(out Vector3 target)
		{
			bool didHit = Physics.Raycast(playerHead.position, Vector3.down, out RaycastHit hitInfo, 1.7f);
			target = hitInfo.point;
			return didHit;
		}
	}
}
