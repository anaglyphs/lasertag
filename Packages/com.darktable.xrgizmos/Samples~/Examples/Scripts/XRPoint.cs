using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRPoint : MonoBehaviour
    {
        [SerializeField] private Color color = Color.red;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] [Range(0.01f, 2.0f)] private float size = 0.1f;

        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            XRGizmos.DrawPoint(_transform.position, color, size, thickness);
        }
    }
}