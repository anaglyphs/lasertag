using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;

public class Test : MonoBehaviour
{
    void Start()
    {
		Unity.Mathematics.Random rand = new(999);

		float3[] points = new float3[100];
		for (int i = 0; i < points.Length; i++)
		{
			points[i] = new float3((int)(rand.NextFloat() * 10), rand.NextFloat() * 100, rand.NextFloat() * 100);
		}

		NativeArray<float3> natPoints = new NativeArray<float3>(points, Allocator.Temp);

		PointTreeBurst.QuicksortPoints(natPoints, 0);

		foreach (float3 point in natPoints)
			Debug.Log(point[0]);
	}
}
