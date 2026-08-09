using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRText : MonoBehaviour
    {
        [SerializeField] [Multiline] private string text;

        [SerializeField] private Color color = Color.white;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        [SerializeField] private Vector2 scale = new(0.1f, 0.1f);

        private Transform _transform;

        private void Awake()
        {
            _transform = transform;
        }

        private void Update()
        {
            if (string.IsNullOrEmpty(text)) return;

            XRGizmos.DrawString(text, _transform.position, _transform.rotation, color, scale.x, scale.y, thickness);
        }
    }
}
