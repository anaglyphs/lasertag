using System;
using UnityEngine;
using UnityEngine.AI;

namespace Unity.AI.Navigation.Samples
{
    /// <summary>
    /// Update a NavMeshSurface with Volume object collection
    /// </summary>
    [DefaultExecutionOrder(-102)]
    public class NavMeshSurfaceVolumeUpdater : MonoBehaviour
    {
        [SerializeField]
        NavMeshAgent trackedAgent;

        NavMeshSurface m_Surface;
        Vector3 m_VolumeSize;

        void Awake()
        {
            m_Surface = GetComponent<NavMeshSurface>();
        }

        void OnEnable()
        {
            m_VolumeSize = m_Surface.size;
            m_Surface.center = QuantizedCenter();
            m_Surface.BuildNavMesh();
        }

        void Update()
        {
            UpdateNavMeshOnCenterOrSizeChange();
        }

        void UpdateNavMeshOnCenterOrSizeChange()
        {
            var updatedCenter = QuantizedCenter();
            var updateNavMesh = false;

            if (m_Surface.center != updatedCenter)
            {
                m_Surface.center = updatedCenter;
                updateNavMesh = true;
            }

            if (m_Surface.size != m_VolumeSize)
            {
                m_VolumeSize = m_Surface.size;
                updateNavMesh = true;
            }

            if (updateNavMesh)
                m_Surface.UpdateNavMesh(m_Surface.navMeshData);
        }

        static Vector3 Quantize(Vector3 v, Vector3 quant)
        {
            float x = quant.x * Mathf.Floor(v.x / quant.x);
            float y = quant.y * Mathf.Floor(v.y / quant.y);
            float z = quant.z * Mathf.Floor(v.z / quant.z);
            return new Vector3(x, y, z);
        }

        Vector3 QuantizedCenter()
        {
            // Quantize the center position to update only when there's a 10% change in position relative to size
            var center = trackedAgent.transform.position;
            return Quantize(center, 0.1f * m_Surface.size);
        }
    }
}