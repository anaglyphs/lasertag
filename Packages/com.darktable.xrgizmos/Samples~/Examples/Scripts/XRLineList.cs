using UnityEngine;

namespace com.darktable.utility.xrgizmos.example
{
    public class XRLineList : MonoBehaviour
    {
        [SerializeField] private Transform[] points;

        [SerializeField] private Color color = Color.blue;

        [SerializeField] private bool closeLoop = true;

        [SerializeField] [Range(0.001f, 0.3f)] private float thickness = 0.01f;

        private Vector3[] _xrPoints;

        private void Update()
        {
            for (var i = 0; i < points.Length; i++) _xrPoints[i] = points[i].position;

            XRGizmos.DrawLineList(_xrPoints, color, closeLoop, thickness);
        }

        private void OnEnable()
        {
            _xrPoints = new Vector3[points.Length];
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (points == null || points.Length == 0)
            {
                int childCount = transform.childCount;
                points = new Transform[childCount];

                for (var i = 0; i < childCount; i++) points[i] = transform.GetChild(i);
            }
        }
#endif
    }
}