using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRMeshCollider : MonoBehaviour
    {
        [SerializeField] private new MeshCollider collider;

        [SerializeField] private Color color = Color.red;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        private void Update()
        {
            XRGizmos.DrawCollider(collider, color, thickness);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (collider == null)
            {
                collider = GetComponent<MeshCollider>();
            }
        }
#endif
    }
}
