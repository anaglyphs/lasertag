using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.XR.DepthKit.EnvScanning.Slam
{
	/// <summary>
	/// 4DoF (position + yaw) pose graph optimizer. Gauss-Newton over relative-pose edges and
	/// absolute priors, with Huber reweighting on edges marked robust.
	///
	/// The normal equations are solved with an envelope (skyline) Cholesky in node order.
	/// A trajectory graph is a chain plus a handful of loop closures, so each row's envelope
	/// only reaches back to its earliest neighbour and fill-in stays inside it — far cheaper
	/// than a dense solve and with none of the slow convergence an iterative solver shows
	/// on long chains.
	/// </summary>
	public static class PoseGraphOptimizer
	{
		public const int Dof = 4;

		public const int ResultInitialCost = 0;
		public const int ResultFinalCost = 1;
		public const int ResultIterations = 2;
		public const int ResultSucceeded = 3;
		public const int ResultCount = 4;

		/// <summary>
		/// Owns every native array an optimization needs. Built on the main thread from a
		/// graph snapshot, handed to <see cref="OptimizeJob"/>, then read back and disposed.
		/// </summary>
		public struct Problem : IDisposable
		{
			public NativeArray<float4> states;
			public NativeArray<float4> priors;
			public NativeArray<float2> priorInformation;
			public NativeArray<int2> edgeNodes;
			public NativeArray<float4> edgeMeasurements;
			public NativeArray<float2> edgeInformation;
			public NativeArray<byte> edgeRobust;
			public NativeArray<float> edgeChi2;
			public NativeArray<int> rowStart;
			public NativeArray<int> rowFirstColumn;
			public NativeArray<double> envelope;
			public NativeArray<double> rhs;
			public NativeArray<float> result;

			public int NodeCount => states.Length;

			public Problem(int nodeCount, int edgeCount, IReadOnlyList<int2> edgeNodeList)
			{
				states = new NativeArray<float4>(nodeCount, Allocator.Persistent);
				priors = new NativeArray<float4>(nodeCount, Allocator.Persistent);
				priorInformation = new NativeArray<float2>(nodeCount, Allocator.Persistent);
				edgeNodes = new NativeArray<int2>(edgeCount, Allocator.Persistent);
				edgeMeasurements = new NativeArray<float4>(edgeCount, Allocator.Persistent);
				edgeInformation = new NativeArray<float2>(edgeCount, Allocator.Persistent);
				edgeRobust = new NativeArray<byte>(edgeCount, Allocator.Persistent);
				edgeChi2 = new NativeArray<float>(edgeCount, Allocator.Persistent);
				result = new NativeArray<float>(ResultCount, Allocator.Persistent);

				// each node's envelope starts at its earliest neighbour
				int[] firstBlock = new int[nodeCount];
				for (int i = 0; i < nodeCount; i++)
					firstBlock[i] = i;

				for (int e = 0; e < edgeNodeList.Count; e++)
				{
					int2 ij = edgeNodeList[e];
					int lo = math.min(ij.x, ij.y);
					int hi = math.max(ij.x, ij.y);
					if (lo < 0 || hi >= nodeCount) continue;
					firstBlock[hi] = math.min(firstBlock[hi], lo);
				}

				int n = nodeCount * Dof;
				rowStart = new NativeArray<int>(n + 1, Allocator.Persistent);
				rowFirstColumn = new NativeArray<int>(n, Allocator.Persistent);
				rhs = new NativeArray<double>(n, Allocator.Persistent);

				int total = 0;
				for (int r = 0; r < n; r++)
				{
					int first = firstBlock[r / Dof] * Dof;
					rowFirstColumn[r] = first;
					rowStart[r] = total;
					total += r - first + 1;
				}

				rowStart[n] = total;
				envelope = new NativeArray<double>(total, Allocator.Persistent);
			}

			public void Dispose()
			{
				states.Dispose();
				priors.Dispose();
				priorInformation.Dispose();
				edgeNodes.Dispose();
				edgeMeasurements.Dispose();
				edgeInformation.Dispose();
				edgeRobust.Dispose();
				edgeChi2.Dispose();
				rowStart.Dispose();
				rowFirstColumn.Dispose();
				envelope.Dispose();
				rhs.Dispose();
				result.Dispose();
			}

			public OptimizeJob CreateJob(int iterations, float huberThreshold) => new()
			{
				states = states,
				priors = priors,
				priorInformation = priorInformation,
				edgeNodes = edgeNodes,
				edgeMeasurements = edgeMeasurements,
				edgeInformation = edgeInformation,
				edgeRobust = edgeRobust,
				edgeChi2 = edgeChi2,
				rowStart = rowStart,
				rowFirstColumn = rowFirstColumn,
				envelope = envelope,
				rhs = rhs,
				result = result,
				iterations = iterations,
				huberThreshold = huberThreshold
			};
		}

		[BurstCompile]
		public struct OptimizeJob : IJob
		{
			// xyz: world position, w: yaw (radians)
			public NativeArray<float4> states;

			[ReadOnly] public NativeArray<float4> priors;
			// x: position information (1/σ²), y: yaw information. zero = no prior
			[ReadOnly] public NativeArray<float2> priorInformation;

			// x: from, y: to. measurement is `to` expressed in `from`'s frame
			[ReadOnly] public NativeArray<int2> edgeNodes;
			[ReadOnly] public NativeArray<float4> edgeMeasurements;
			[ReadOnly] public NativeArray<float2> edgeInformation;
			[ReadOnly] public NativeArray<byte> edgeRobust;
			[WriteOnly] public NativeArray<float> edgeChi2;

			[ReadOnly] public NativeArray<int> rowStart;
			[ReadOnly] public NativeArray<int> rowFirstColumn;
			public NativeArray<double> envelope;
			public NativeArray<double> rhs;

			[WriteOnly] public NativeArray<float> result;

			public int iterations;

			// Mahalanobis distance past which a robust edge's influence stops growing
			public float huberThreshold;

			// keeps the gauge (a graph with no priors can slide freely) and any island the
			// trajectory left disconnected well-posed without measurably pulling anything
			private const double GaugeInformation = 1e-6;

			public void Execute()
			{
				result[ResultSucceeded] = 0f;
				int nodeCount = states.Length;
				int n = nodeCount * Dof;

				double initialCost = 0;
				double cost = 0;
				int iter = 0;
				bool solved = true;

				for (; iter < iterations; iter++)
				{
					for (int i = 0; i < envelope.Length; i++) envelope[i] = 0;
					for (int i = 0; i < n; i++) rhs[i] = 0;

					cost = Linearize();
					if (iter == 0) initialCost = cost;

					if (!Factorize())
					{
						solved = false;
						break;
					}

					Solve();

					double maxStep = 0;
					for (int i = 0; i < nodeCount; i++)
					{
						int r = i * Dof;
						float4 s = states[i];
						s.x -= (float)rhs[r];
						s.y -= (float)rhs[r + 1];
						s.z -= (float)rhs[r + 2];
						s.w = Pose4.WrapAngle(s.w - (float)rhs[r + 3]);
						states[i] = s;

						maxStep = math.max(maxStep, math.abs(rhs[r]));
						maxStep = math.max(maxStep, math.abs(rhs[r + 1]));
						maxStep = math.max(maxStep, math.abs(rhs[r + 2]));
						maxStep = math.max(maxStep, math.abs(rhs[r + 3]));
					}

					if (maxStep < 1e-6)
					{
						iter++;
						break;
					}
				}

				// final unweighted chi² per edge, for outlier rejection by the caller
				for (int e = 0; e < edgeNodes.Length; e++)
				{
					EdgeResidual(e, out double4 err, out _, out _);
					double4 info = Information(edgeInformation[e]);
					edgeChi2[e] = (float)math.dot(err * err, info);
				}

				result[ResultInitialCost] = (float)initialCost;
				result[ResultFinalCost] = (float)cost;
				result[ResultIterations] = iter;
				result[ResultSucceeded] = solved ? 1f : 0f;
			}

			private static double4 Information(float2 info) => new(info.x, info.x, info.x, info.y);

			private double Linearize()
			{
				double cost = 0;
				int nodeCount = states.Length;

				for (int i = 0; i < nodeCount; i++)
				{
					double4x4 gauge = double4x4.identity * GaugeInformation;
					AddBlock(i, i, gauge);

					float2 priorInfo = priorInformation[i];
					if (priorInfo.x <= 0f && priorInfo.y <= 0f) continue;

					float4 s = states[i];
					float4 p = priors[i];
					double4 err = new(s.x - p.x, s.y - p.y, s.z - p.z, Pose4.WrapAngle(s.w - p.w));
					double4 info = Information(priorInfo);

					AddBlock(i, i, Diagonal(info));
					AddRhs(i, err * info);
					cost += math.dot(err * err, info);
				}

				for (int e = 0; e < edgeNodes.Length; e++)
				{
					EdgeResidual(e, out double4 err, out double4x4 jFrom, out double4x4 jTo);

					double4 info = Information(edgeInformation[e]);
					double chi2 = math.dot(err * err, info);

					double weight = 1;
					if (edgeRobust[e] != 0)
					{
						double chi = math.sqrt(chi2);
						double k = huberThreshold;
						if (chi > k)
						{
							weight = k / chi;
							cost += 2 * k * chi - k * k;
						}
						else cost += chi2;
					}
					else cost += chi2;

					double4 wInfo = info * weight;
					double4x4 omega = Diagonal(wInfo);

					int2 ij = edgeNodes[e];
					int from = ij.x;
					int to = ij.y;

					double4x4 jFromT = math.transpose(jFrom);
					double4x4 jToT = math.transpose(jTo);

					AddBlock(from, from, math.mul(jFromT, math.mul(omega, jFrom)));
					AddBlock(to, to, math.mul(jToT, math.mul(omega, jTo)));

					// only the lower triangle is stored: block (row, col) with row > col
					if (to > from)
						AddBlock(to, from, math.mul(jToT, math.mul(omega, jFrom)));
					else
						AddBlock(from, to, math.mul(jFromT, math.mul(omega, jTo)));

					AddRhs(from, math.mul(jFromT, err * wInfo));
					AddRhs(to, math.mul(jToT, err * wInfo));
				}

				return cost;
			}

			/// <summary>
			/// e_t = R(-ψ_from)(p_to - p_from) - t̂,  e_ψ = ψ_to - ψ_from - ψ̂
			/// Jacobians are laid out math-style: element (residual r, variable v) at [v][r]
			/// </summary>
			private void EdgeResidual(int e, out double4 err, out double4x4 jFrom, out double4x4 jTo)
			{
				int2 ij = edgeNodes[e];
				float4 a = states[ij.x];
				float4 b = states[ij.y];
				float4 z = edgeMeasurements[e];

				double c = math.cos((double)a.w);
				double s = math.sin((double)a.w);
				double3 d = new(b.x - a.x, b.y - a.y, b.z - a.z);

				// R(-ψ) d
				double3 local = new(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
				err = new double4(local - new double3(z.x, z.y, z.z),
					Pose4.WrapAngle(b.w - a.w - z.w));

				// ∂(R(-ψ) d)/∂ψ
				double3 dYaw = new(-s * d.x - c * d.z, 0, c * d.x - s * d.z);

				jFrom = new double4x4(
					-c, 0, s, dYaw.x,
					0, -1, 0, dYaw.y,
					-s, 0, -c, dYaw.z,
					0, 0, 0, -1);

				jTo = new double4x4(
					c, 0, -s, 0,
					0, 1, 0, 0,
					s, 0, c, 0,
					0, 0, 0, 1);
			}

			private static double4x4 Diagonal(double4 d) => new(
				d.x, 0, 0, 0,
				0, d.y, 0, 0,
				0, 0, d.z, 0,
				0, 0, 0, d.w);

			private void AddRhs(int node, double4 v)
			{
				int r = node * Dof;
				rhs[r] += v.x;
				rhs[r + 1] += v.y;
				rhs[r + 2] += v.z;
				rhs[r + 3] += v.w;
			}

			/// <summary>Accumulates block (rowNode, colNode), rowNode ≥ colNode, into the envelope</summary>
			private void AddBlock(int rowNode, int colNode, double4x4 block)
			{
				for (int k = 0; k < Dof; k++)
				{
					int r = rowNode * Dof + k;
					int first = rowFirstColumn[r];
					int baseIndex = rowStart[r];

					for (int l = 0; l < Dof; l++)
					{
						if (rowNode == colNode && l > k) break;

						int c = colNode * Dof + l;
						envelope[baseIndex + c - first] += block[l][k];
					}
				}
			}

			/// <summary>In-place envelope Cholesky, A = L Lᵀ</summary>
			private bool Factorize()
			{
				int n = rhs.Length;

				for (int r = 0; r < n; r++)
				{
					int firstR = rowFirstColumn[r];
					int baseR = rowStart[r];

					for (int c = firstR; c <= r; c++)
					{
						int firstC = rowFirstColumn[c];
						int baseC = rowStart[c];
						int kStart = math.max(firstR, firstC);

						double sum = envelope[baseR + c - firstR];
						for (int k = kStart; k < c; k++)
							sum -= envelope[baseR + k - firstR] * envelope[baseC + k - firstC];

						if (c < r)
						{
							envelope[baseR + c - firstR] = sum / envelope[baseC + c - firstC];
						}
						else
						{
							if (sum <= 1e-12 || double.IsNaN(sum)) return false;
							envelope[baseR + c - firstR] = math.sqrt(sum);
						}
					}
				}

				return true;
			}

			/// <summary>Solves L Lᵀ x = rhs in place</summary>
			private void Solve()
			{
				int n = rhs.Length;

				for (int r = 0; r < n; r++)
				{
					int first = rowFirstColumn[r];
					int baseR = rowStart[r];
					double sum = rhs[r];
					for (int k = first; k < r; k++)
						sum -= envelope[baseR + k - first] * rhs[k];
					rhs[r] = sum / envelope[baseR + r - first];
				}

				for (int r = n - 1; r >= 0; r--)
				{
					int first = rowFirstColumn[r];
					int baseR = rowStart[r];
					double x = rhs[r] / envelope[baseR + r - first];
					rhs[r] = x;
					for (int k = first; k < r; k++)
						rhs[k] -= envelope[baseR + k - first] * x;
				}
			}
		}
	}
}
