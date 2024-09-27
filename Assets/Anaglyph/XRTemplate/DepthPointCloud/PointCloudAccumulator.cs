//using Anaglyph.XRTemplate.PointCloud;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.Events;

//namespace Anaglyph.XRTemplate
//{
//    public class PointCloudAccumulator : MonoBehaviour
//    {
//		public List<Vector3> points = new List<Vector3>(999999);
//		public UnityEvent<List<Vector3>> onAccumulate;
//		public int maxPoints = 1024 * 1024;

//		private Camera mainCamera;

//		private void Start()
//		{
//			mainCamera = Camera.main;
//			InvokeRepeating("Accumulate", 1f, 0.5f);
//		}

//		public void Accumulate()
//		{
//			if (!enabled) return;

//			//for(int i = 0; i < points.Count; i++)
//			//{
//			//	Vector3 v = mainCamera.WorldToViewportPoint(points[i], Camera.MonoOrStereoscopicEye.Left);

//			//	if (v.z > 0 && v.z < 4f && v.x > 0.1f && v.x < 0.9f && v.y > 0.1f && v.y < 0.9f)
//			//	{
//			//		points.RemoveAt(i);
//			//		i--;
//			//	}
//			//}

//			bool success = DepthGridSampler.Sample(out DepthCastResult[] results, new Vector2Int(100, 100), false);
//			if (!success) return;

//			for (int i = 0; i < results.Length; i++)
//			{
//				if (points.Count >= maxPoints)
//					break;

//				if (results[i].ZDepthDiff < 4f)
//					points.Add(results[i].Position);
//			}

//			onAccumulate.Invoke(points);
//		}
//	}
//}
