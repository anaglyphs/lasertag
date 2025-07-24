using System;
using UnityEngine;

// github.com/ampl/gsl/blob/master/linalg/svd.c
// note: A = U S Vt, Ainv = V Sinv Ut

// https://jamesmccaffrey.wordpress.com/2023/11/24/singular-value-decomposition-svd-from-scratch-with-csharp-using-the-jacobi-algorithm/

namespace McCaffrey
{
	public static class SVDJacobi
	{
		/*
		static void Main(string[] args)
		{
			Console.WriteLine("\nBegin SVD decomp via Jacobi" +
			  " algorithm using C# ");

			float[,] A = new float[4][];
			A[0] = new float[] { 1, 2, 3 };
			A[1] = new float[] { 5, 0, 2 };
			A[2] = new float[] { 8, 5, 4 };
			A[3] = new float[] { 6, 9, 7 };

			Console.WriteLine("\nSource matrix: ");
			MatShow(A, 1, 5);

			float[,] U;
			float[,] Vh;
			float[] s;

			Console.WriteLine("\nPerforming SVD decomposition ");
			SVD_Jacobi(A, out U, out Vh, out s);

			Console.WriteLine("\nResult U = "); MatShow(U, 4, 9);
			Console.WriteLine("\nResult s = "); VecShow(s, 4, 9);
			Console.WriteLine("\nResult Vh = "); MatShow(Vh, 4, 9);

			float[,] S = MatDiag(s);
			float[,] US = MatProduct(U, S);
			float[,] USVh = MatProduct(US, Vh);
			Console.WriteLine("\nU * S * Vh = ");
			MatShow(USVh, 4, 9);

			Console.WriteLine("\nEnd demo ");
			Console.ReadLine();
		} // Main
		*/

		public static void Decompose(float[,] M, out float[,] U,
		  out float[,] Vh, out float[] s)
		{
			float DBL_EPSILON = 1.0e-15f;

			float[,] A = MatCopy(M); // working U
			int m = A.GetLength(0);
			int n = A.GetLength(1);
			float[,] Q = MatIdentity(n); // working V
			float[] t = new float[n];  // working s

			// initialize counters
			int count = 1;
			int sweep = 0;
			//int sweepmax = 5 * n;

			float tolerance = 10 * m * DBL_EPSILON; // heuristic

			// Always do at least 12 sweeps
			int sweepmax = Math.Max(5 * n, 12); // heuristic

			// store the column error estimates in St for use
			// during orthogonalization

			for (int j = 0; j < n; ++j)
			{
				float[] cj = MatGetColumn(A, j);
				float sj = VecNorm(cj);
				t[j] = DBL_EPSILON * sj;
			}

			// orthogonalize A by plane rotations
			while (count > 0 && sweep <= sweepmax)
			{
				// initialize rotation counter
				count = n * (n - 1) / 2;

				for (int j = 0; j < n - 1; ++j)
				{
					for (int k = j + 1; k < n; ++k)
					{
						float cosine, sine;

						float[] cj = MatGetColumn(A, j);
						float[] ck = MatGetColumn(A, k);

						float p = 2.0f * VecDot(cj, ck);
						float a = VecNorm(cj);
						float b = VecNorm(ck);

						float q = a * a - b * b;
						float v = Hypot(p, q);

						// test for columns j,k orthogonal,
						// or dominant errors 
						float abserr_a = t[j];
						float abserr_b = t[k];

						bool sorted = (a >= b);
						bool orthog = (Math.Abs(p) <=

					  tolerance * (a * b));
						bool noisya = (a < abserr_a);
						bool noisyb = (b < abserr_b);

						if (sorted && (orthog ||
						  noisya || noisyb))
						{
							--count;
							continue;
						}

						// calculate rotation angles
						if (v == 0 || !sorted)
						{
							cosine = 0.0f;
							sine = 1.0f;
						}
						else
						{
							cosine = Mathf.Sqrt((v + q) / (2.0f * v));
							sine = p / (2.0f * v * cosine);
						}

						// apply rotation to A (U)
						for (int i = 0; i < m; ++i)
						{
							float Aik = A[i,k];
							float Aij = A[i,j];
							A[i, j] = Aij * cosine + Aik * sine;
							A[i, k] = -Aij * sine + Aik * cosine;
						}

						// update singular values
						t[j] = Math.Abs(cosine) * abserr_a +
						  Math.Abs(sine) * abserr_b;
						t[k] = Math.Abs(sine) * abserr_a +
						  Math.Abs(cosine) * abserr_b;

						// apply rotation to Q (V)
						for (int i = 0; i < n; ++i)
						{
							float Qij = Q[i, j];
							float Qik = Q[i, k];
							Q[i, j] = Qij * cosine + Qik * sine;
							Q[i, k] = -Qij * sine + Qik * cosine;
						} // i
					} // k
				} // j

				++sweep;
			} // while

			//  compute singular values
			float prevNorm = -1.0f;

			for (int j = 0; j < n; ++j)
			{
				float[] column = MatGetColumn(A, j);
				float norm = VecNorm(column);

				// determine if singular value is zero
				if (norm == 0.0 || prevNorm == 0.0
				  || (j > 0 &&
					norm <= tolerance * prevNorm))
				{
					t[j] = 0.0f;
					for (int i = 0; i < m; ++i)
						A[i, j] = 0.0f;
					prevNorm = 0.0f;
				}

				else
				{
					t[j] = norm;
					for (int i = 0; i < m; ++i)
						A[i, j] = A[i, j] * 1.0f / norm;
					prevNorm = norm;
				}
			}

			if (count > 0)
			{
				Console.WriteLine("Jacobi iterations did not" +
				  " converge");
			}

			U = A;
			Vh = MatTranspose(Q);
			s = t;

			// to sync with default np.linalg.svd() shapes:
			// if m < n, extract 1st m columns of U
			//   extract 1st m values of s
			//   extract 1st m rows of Vh

			if (m < n)
			{
				U = MatExtractFirstColumns(U, m);
				s = VecExtractFirst(s, m);
				Vh = MatExtractFirstRows(Vh, m);
			}

		} // SVD_Jacobi()

		// === helper functions =================================
		//
		// MatMake, MatCopy, MatIdentity, MatGetColumn,
		// MatExtractFirstColumns, MatExtractFirstRows,
		// MatTranspose, MatDiag, MatProduct, VecNorm, VecDot,
		// Hypot, VecExtractFirst, MatShow, VecShow
		//
		// ======================================================

		public static float[,] MatMake(int r, int c)
		{
			float[,] result = new float[r,c];
			//for (int i = 0; i < r; ++i)
			//	result[i] = new float[c];
			return result;
		}

		public static float[,] MatCopy(float[,] m)
		{
			int r = m.GetLength(0); int c = m.GetLength(1);
			float[,] result = MatMake(r, c);
			for (int i = 0; i < r; ++i)
				for (int j = 0; j < c; ++j)
					result[i, j] = m[i, j];
			return result;
		}

		public static float[,] MatIdentity(int n)
		{
			float[,] result = MatMake(n, n);
			for (int i = 0; i < n; ++i)
				result[i, i] = 1.0f;
			return result;
		}

		public static float[] MatGetColumn(float[,] m, int j)
		{
			int rows = m.GetLength(0);
			float[] result = new float[rows];
			for (int i = 0; i < rows; ++i)
				result[i] = m[i,j];
			return result;
		}

		public static float[,] MatExtractFirstColumns(float[,] src,
		  int n)
		{
			int nRows = src.GetLength(0);
			// int nCols = src[0].Length;
			float[,] result = MatMake(nRows, n);
			for (int i = 0; i < nRows; ++i)
				for (int j = 0; j < n; ++j)
					result[i, j] = src[i, j];
			return result;
		}

		public static float[,] MatExtractFirstRows(float[,] src,
		  int n)
		{
			// int nRows = src.Length;
			int nCols = src.GetLength(1);
			float[,] result = MatMake(n, nCols);
			for (int i = 0; i < n; ++i)
				for (int j = 0; j < nCols; ++j)
					result[i, j] = src[i, j];
			return result;
		}

		public static float[,] MatTranspose(float[,] m)
		{
			int r = m.GetLength(0);
			int c = m.GetLength(1);
			float[,] result = MatMake(c, r);
			for (int i = 0; i < r; ++i)
				for (int j = 0; j < c; ++j)
					result[j, i] = m[i, j];
			return result;
		}

		public static float[,] MatDiag(float[] vec)
		{
			int n = vec.GetLength(0);
			float[,] result = MatMake(n, n);
			for (int i = 0; i < n; ++i)
				result[i, i] = vec[i];
			return result;
		}

		public static float[,] MatProduct(float[,] matA,
		  float[,] matB)
		{
			int aRows = matA.GetLength(0);
			int aCols = matA.GetLength(1);
			int bRows = matB.GetLength(0);
			int bCols = matB.GetLength(1);
			if (aCols != bRows)
				throw new Exception("Non-conformable matrices");

			float[,] result = MatMake(aRows, bCols);

			for (int i = 0; i < aRows; ++i)
				for (int j = 0; j < bCols; ++j)
					for (int k = 0; k < aCols; ++k)
						result[i, j] += matA[i, k] * matB[k, j];

			return result;
		}

		public static float VecNorm(float[] vec)
		{
			float sum = 0.0f;
			int n = vec.GetLength(0);
			for (int i = 0; i < n; ++i)
				sum += vec[i] * vec[i];
			return Mathf.Sqrt(sum);
		}

		public static float VecDot(float[] v1, float[] v2)
		{
			int n = v1.GetLength(0);
			float sum = 0.0f;
			for (int i = 0; i < n; ++i)
				sum += v1[i] * v2[i];
			return sum;
		}

		public static float Hypot(float x, float y)
		{
			// fancy sqrt(x^2 + y^2)
			float xabs = Math.Abs(x);
			float yabs = Math.Abs(y);
			float min, max;

			if (xabs < yabs)
			{
				min = xabs; max = yabs;
			}

			else
			{
				min = yabs; max = xabs;
			}

			if (min == 0)
				return max;

			float u = min / max;
			return max * Mathf.Sqrt(1 + u * u);
		}

		public static float[] VecExtractFirst(float[] vec, int n)
		{
			float[] result = new float[n];
			for (int i = 0; i < n; ++i)
				result[i] = vec[i];
			return result;
		}

		// ------------------------------------------------------

		public static void MatShow(float[,] m,
		  int dec, int wid)
		{
			for (int i = 0; i < m.GetLength(0); ++i)
			{
				for (int j = 0; j < m.GetLength(1); ++j)
				{
					float v = m[i, j];
					if (Math.Abs(v) < 1.0e-8) v = 0.0f;  // hack
					Console.Write(v.ToString("F" + dec).
					  PadLeft(wid));
				}
				Console.WriteLine("");
			}
		}

		// ------------------------------------------------------

		public static void VecShow(float[] vec,
		  int dec, int wid)
		{
			for (int i = 0; i < vec.GetLength(0); ++i)
			{
				float x = vec[i];
				if (Math.Abs(x) < 1.0e-8) x = 0.0f;
				Console.Write(x.ToString("F" + dec).
				  PadLeft(wid));
			}
			Console.WriteLine("");
		}
	} // Program
} // ns