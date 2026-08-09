using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRRay : MonoBehaviour
    {
        [SerializeField] private Color color = Color.red;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] [Range(0.01f, 2.0f)] private float scale = 1.0f;

        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            var ray = new Ray(_transform.position, _transform.forward);

            XRGizmos.DrawRay(ray, color, scale, thickness);
        }
    }
}