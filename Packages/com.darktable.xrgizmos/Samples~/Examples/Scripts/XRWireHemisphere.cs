using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRWireHemisphere : MonoBehaviour
    {
        [SerializeField] private Color color = Color.red;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] private Color cubeColor = Color.gray;
        [SerializeField] private Vector3 cubeSize = new(0.1f, 0.1f, 0.1f);

        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            var pos = _transform.position;
            var rot = _transform.rotation;
            XRGizmos.DrawWireHemisphere(pos, rot, _transform.lossyScale.magnitude, color, thickness);
            XRGizmos.DrawCube(pos, rot, cubeSize, cubeColor);
        }
    }
}
