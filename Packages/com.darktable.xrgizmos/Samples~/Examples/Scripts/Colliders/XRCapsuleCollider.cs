using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRCapsuleCollider : MonoBehaviour
    {
        [SerializeField] private new CapsuleCollider collider;

        [SerializeField] private Color color = Color.red;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] private bool drawBounds = false;
        [SerializeField] private Color boundsColor = Color.grey;

        private void Update()
        {
            XRGizmos.DrawCollider(collider, color, thickness);

            if (drawBounds)
            {
                XRGizmos.DrawColliderBounds(collider, boundsColor, thickness);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (collider == null)
            {
                collider = GetComponent<CapsuleCollider>();
            }
        }
#endif
    }
}
