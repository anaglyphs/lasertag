using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRLine : MonoBehaviour
    {
        [SerializeField] private Transform end;

        [SerializeField] private Color color = Color.green;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] [Range(0.01f, 2.0f)] private float scale = 1.0f;

        [SerializeField] private Color sphereColor = Color.blue;

        [SerializeField] private float sphereRadius = 0.05f;

        [SerializeField] private bool showSpheres = true;

        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            var from = _transform.position;
            var to = end.position;

            XRGizmos.DrawLine(from, to, color, thickness);

            if (showSpheres)
            {
                XRGizmos.DrawSphere(from, sphereRadius, sphereColor);
                XRGizmos.DrawSphere(to, sphereRadius, sphereColor);
            }
        }
    }
}