using McCaffrey;
using System;
using System.Collections.Generic;
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

		public bool run;

		private void OnValidate()
		{
			if(run)
			{
				run = false;

				RunICP(1);
			}
		}

		private void RunICP(int iterations)
		{
			float3[] fArray = new float3[testDepthMesh.mesh.vertices.Length];
			for (int i = 0; i < fArray.Length; i++)
			{
				fArray[i] = testDepthMesh.mesh.vertices[i];
			}

			Matrix4x4 newMat = EstimateTransform(testDepthMesh.transform.localToWorldMatrix, fArray, iterations);

			testDepthMesh.transform.SetPositionAndRotation(newMat.GetPosition(), newMat.rotation);
		}

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
		}

		
		public Matrix4x4 EstimateTransform(Matrix4x4 headsetTransform, float3[] localDepthPoints, int iterations)
		{

			Vector3 localDepthCentroid = Vector3.zero;

			foreach (Vector3 v in localDepthPoints)
				localDepthCentroid += v;
			localDepthCentroid /= localDepthPoints.Length;
			Vector3 worldDepthCentroid = headsetTransform.MultiplyPoint(localDepthCentroid);

			Vector3[] localDepthPointsMinusCentroid = new Vector3[localDepthPoints.Length];

			for (int i = 0; i < localDepthPoints.Length; i++)
				localDepthPointsMinusCentroid[i] = (Vector3)localDepthPoints[i] - localDepthCentroid;


			for (int k = 0; k < iterations; k++)
			{
				NativeArray<float3> results = new(localDepthPoints.Length, Allocator.TempJob);
				NativeArray<float3> points = new(localDepthPoints, Allocator.TempJob);

				PointTreeBurst.FindClosestPoints findJob = new()
				{
					pointTransform = headsetTransform,
					points = points,
					nodes = tree,
					found = results,
				};

				var findJobHandle = findJob.ScheduleParallelByRef(results.Length, 0, default);
				findJobHandle.Complete();


				Vector3 corrCentroid = Vector3.zero;
				foreach (Vector3 v in results)
					corrCentroid += v;
				corrCentroid /= results.Length;

				Vector3[] corrPointsMinusCentroid = new Vector3[results.Length];
				for (int i = 0; i < results.Length; i++)
					corrPointsMinusCentroid[i] = (Vector3)results[i] - corrCentroid;



				float[,] covMat = new float[3, 3];
				for (int i = 0; i < results.Length; i++)
				{
					Vector3 globalDepthPointMinusCentroid = headsetTransform.MultiplyPoint(localDepthPointsMinusCentroid[i]);
					Vector3 correspondingPointMinusCentroid = corrPointsMinusCentroid[i];

					for (int x = 0; x < 3; x++)
						for (int y = 0; y < 3; y++)
							covMat[x, y] += correspondingPointMinusCentroid[x] * globalDepthPointMinusCentroid[y];
				}

				// svd
				SVDJacobi.Decompose(covMat, out float[,] U, out float[,] Vh, out float[] s);

				// https://learnopencv.com/iterative-closest-point-icp-explained/
				float[,] Ut = SVDJacobi.MatTranspose(U);
				float[,] V = SVDJacobi.MatTranspose(Vh);
				float[,] rot3x3 = MatMultiply(U, Vh);

				Matrix4x4 rotate = ToUnityMat(rot3x3);
				Matrix4x4 translate = Matrix4x4.Translate(corrCentroid - worldDepthCentroid);

				headsetTransform *= (translate * rotate);


				points.Dispose();
				results.Dispose();
			}

			return headsetTransform;
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
