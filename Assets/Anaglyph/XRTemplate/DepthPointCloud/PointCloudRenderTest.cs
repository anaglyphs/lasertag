using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class PointCloudRenderTest : MonoBehaviour
    {
        [SerializeField] private PointCloudRenderer pointCloudRenderer;

        
        void Start()
        {
            List<Vector3> points = new List<Vector3>(512 * 512);

            for(int i = 0; i < points.Capacity; i++)
            {
                points.Add(new(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)));
            }

            pointCloudRenderer.UpdatePoints(points);
        }

		private void OnValidate()
		{
            this.SetDefaultComponent(ref pointCloudRenderer);
		}
	}
}
