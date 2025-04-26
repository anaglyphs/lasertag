using UnityEngine;
using UnityEngine.AI;

namespace Unity.AI.Navigation.Samples
{
    /// <summary>
    /// Use physics raycast hit from mouse click to set agent destination
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class NavigationLoop : MonoBehaviour
    {
        NavMeshAgent m_Agent;
        public Transform[] goals = new Transform[3];
        private int m_NextGoal = 1;

        void Start()
        {
            m_Agent = GetComponent<NavMeshAgent>();
        }

        void Update()
        {
            float distance = Vector3.Distance(m_Agent.transform.position, goals[m_NextGoal].position);
            if (distance < 0.5f)
            {
                m_NextGoal = m_NextGoal != 2 ? m_NextGoal + 1 : 0;
            }
            m_Agent.destination = goals[m_NextGoal].position;
        }
    }
}