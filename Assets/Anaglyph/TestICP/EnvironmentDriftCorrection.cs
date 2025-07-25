using McCaffrey;
using System;
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

		private void Start()
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

			Test(icpIterations);
		}

		private async void Test(int iterations)
		{
			float3[] points = new float3[testDepthMesh.mesh.vertices.Length];
			for (int i = 0; i < points.Length; i++)
				points[i] = testDepthMesh.mesh.vertices[i];

			float3 newCentroid = float3.zero;

			foreach (float3 v in points)
				newCentroid += v;
			newCentroid /= points.Length;

			NativeArray<float3> knnResults = new(points.Length, Allocator.Persistent);
			NativeArray<float3> newPoints = new(points, Allocator.Persistent);

			for (int i = 0; i < iterations; i++)
			{
				float4x4 trans = testDepthMesh.transform.localToWorldMatrix;
				trans = await ClosestPointStep(trans, newPoints, knnResults,  newCentroid);
				Matrix4x4 mat = trans;

				testDepthMesh.transform.SetPositionAndRotation(mat.GetPosition(), mat.rotation);
			}

			knnResults.Dispose();
			newPoints.Dispose();
		}

		public async Task<float4x4> ClosestPointStep(float4x4 newTrans, NativeArray<float3> newPoints, NativeArray<float3> knnResults, float3 newCentroid)
		{
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
			foreach (float3 v in knnResults)
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

				float3 envPoint = knnResults[i];
				float3 envPointMinusCent = envPoint - envCentroid;

				for (int x = 0; x < 3; x++)
					for (int y = 0; y < 3; y++)
						covMat[x, y] += envPointMinusCent[x] * newPointMinusCentGlobal[y];
			}

			// svd
			SVDJacobi.Decompose(covMat, out float[,] U, out float[,] Vh, out float[] s);

			// https://learnopencv.com/iterative-closest-point-icp-explained/
			float[,] rot3x3 = MatMultiply(U, Vh);

			float4x4 rotate = ToUnityMat(rot3x3);
			
			if(math.determinant(rotate) < 0)
			{
				Vh[2, 0] *= -1;
				Vh[2, 1] *= -1;
				Vh[2, 2] *= -1;

				rot3x3 = MatMultiply(U, Vh);
				rotate = ToUnityMat(rot3x3);
			}

			float4x4 translate = float4x4.Translate(envCentroid - newCentroidGlobal);
			newTrans = math.mul(newTrans, math.mul(translate, rotate));

			return newTrans;
		}



		public static float[,] MatMultiply(float[,] a, float[,] b)
		{
			int aRows = a.GetLength(0);
			int aCols = a.GetLength(1);
			int bRows = b.GetLength(0);
			int bCols = b.GetLength(1);

			if (aCols != bRows)
				throw new ArgumentException("Number of columns in the first matrix must equal the number of rows in the second.");

			float[,] result = new float[aRows, bCols];

			for (int i = 0; i < aRows; i++)
			{
				for (int j = 0; j < bCols; j++)
				{
					float sum = 0;
					for (int k = 0; k < aCols; k++)
					{
						sum += a[i, k] * b[k, j];
					}
					result[i, j] = sum;
				}
			}

			return result;
		}

		public static Matrix4x4 ToUnityMat(float[,] rotation3x3)
		{
			if (rotation3x3.GetLength(0) != 3 || rotation3x3.GetLength(1) != 3)
			{
				throw new ArgumentException("Input must be a 3x3 matrix.");
			}

			Matrix4x4 matrix = Matrix4x4.identity;

			// Fill in the rotation part
			matrix.m00 = rotation3x3[0, 0];
			matrix.m01 = rotation3x3[0, 1];
			matrix.m02 = rotation3x3[0, 2];

			matrix.m10 = rotation3x3[1, 0];
			matrix.m11 = rotation3x3[1, 1];
			matrix.m12 = rotation3x3[1, 2];

			matrix.m20 = rotation3x3[2, 0];
			matrix.m21 = rotation3x3[2, 1];
			matrix.m22 = rotation3x3[2, 2];

			// The rest (translation and perspective terms) remain as in the identity matrix
			return matrix;
		}
	}
}
