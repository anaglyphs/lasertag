using McCaffrey;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph
{
	public class EnvironmentDriftCorrection : MonoBehaviour
	{
		[SerializeField] private GameObject environmentObject;
		[SerializeField] private MeshFilter testDepthMesh;
		[SerializeField] int icpIterations = 10;

		NativeArray<PointTreeBurst.Node> tree;

		private void Awake()
		{
			List<float3> vertices = new();
			MeshFilter[] meshFilters = environmentObject.GetComponentsInChildren<MeshFilter>();

			foreach (var meshFilter in meshFilters)
				foreach (var vertex in meshFilter.mesh.vertices)
					vertices.Add(vertex);

			tree = new NativeArray<PointTreeBurst.Node>(vertices.Count, Allocator.Persistent);
			NativeArray<float3> points = new NativeArray<float3>(vertices.ToArray(), Allocator.TempJob);

			PointTreeBurst.BuildTreeJob buildJob = new()
			{
				points = points,
				nodes = tree,
			};

			// buildJob.Run();
			buildJob.Schedule().Complete();

			points.Dispose();
		}

		private async void OnEnable()
		{
			float3[] points = new float3[testDepthMesh.mesh.vertices.Length];
			for (int i = 0; i < points.Length; i++)
				points[i] = testDepthMesh.mesh.vertices[i];

			NativeArray<float3> knnResults = new(points.Length, Allocator.Persistent);
			NativeArray<float3> newPoints = new(points, Allocator.Persistent);

			float4x4 trans = transform.localToWorldMatrix;
			while (enabled)
			{
				trans = await ClosestPointStep(trans, newPoints, knnResults);
				Matrix4x4 mat = trans;

				testDepthMesh.transform.SetPositionAndRotation(mat.GetPosition(), mat.rotation);
			}

			knnResults.Dispose();
			newPoints.Dispose();
		}

		public async Task<float4x4> ClosestPointStep(float4x4 newTrans, NativeArray<float3> newPoints, NativeArray<float3> knnResults)
		{
			float3 newCentroid = float3.zero;

			foreach (float3 v in newPoints)
				newCentroid += v;
			newCentroid /= newPoints.Length;

			PointTreeBurst.FindClosestPoints findJob = new()
			{
				pointTransform = newTrans,
				points = newPoints,
				nodes = tree,
				found = knnResults,
			};

			var findJobHandle = findJob.ScheduleParallelByRef(knnResults.Length, 0, default);
			while (!findJobHandle.IsCompleted)
				await Awaitable.NextFrameAsync();
			findJobHandle.Complete();

			float3 envCentroid = float3.zero;
			foreach (float3 v in newPoints)
				envCentroid += v;
			envCentroid /= knnResults.Length;

			float4 nc4 = new float4(newCentroid, 1);
			float3 newCentroidGlobal = math.mul(newTrans, nc4).xyz;

			float[,] covMat = new float[3, 3];
			for (int i = 0; i < knnResults.Length; i++)
			{
				float4 np4 = new(newPoints[i], 1);
				float3 newPointGlobal = math.mul(newTrans, np4).xyz;

				float3 newPointMinusCentGlobal = newPointGlobal - newCentroidGlobal;

				float3 envPoint = newPoints[i];
				float3 envPointMinusCent = envPoint - envCentroid;

				//if (math.distance(newPointGlobal, envPoint) > 0.05f)
				//	continue;

				for (int x = 0; x < 3; x++)
					for (int y = 0; y < 3; y++)
						covMat[y, x] += envPointMinusCent[y] * newPointMinusCentGlobal[x];
			}

			// svd
			SVDJacobi.Decompose(covMat, out float[,] Ua, out float[,] Vha, out float[] sa);

			float3x3 U = arrayToMat(Ua);
			float3x3 Vt = arrayToMat(Vha);

			// envCentroid, centroid of static environment
			// newCentroidGlobal, global position (relative to env) of new points to register

			float3x3 rot = math.mul(U, Vt);
			//rot = math.transpose(rot);

			if (math.determinant(rot) < 0)
			{
				float3x3 V = math.transpose(Vt);
				V.c2 *= -1;
				Vt = math.transpose(V);
				rot = math.mul(U, Vt);
			}

			//float rotNorm = frobNorm(rot - float3x3.identity);
			//if (math.abs(rotNorm) < 0.005f)
			//	rot = float3x3.identity;

			float3 t = envCentroid - math.mul(rot, newCentroidGlobal);
			float4x4 deltaTransform = float4x4.TRS(t, new quaternion(rot), new float3(1));
			newTrans = math.mul(deltaTransform, newTrans);

			return newTrans;
		}

		private static float frobNorm(float3x3 matrix)
		{
			float sumOfSquares = 0f;
			for (int i = 0; i < 3; i++)
				for (int j = 0; j < 3; j++)
					sumOfSquares += math.square(matrix[i][j]);
			return math.sqrt(sumOfSquares);
		}

		private static float3x3 arrayToMat(float[,] mat)
		{
			float3x3 f = new float3x3();
			for (int x = 0; x < 3; x++)
				for (int y = 0; y < 3; y++)
					f[x][y] = mat[x, y];
			return f;
		}
	}
}
