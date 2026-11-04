using System.Collections;
using Anaglyph.XR.VisualLocalization;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.TestTools;

namespace Anaglyph.LaserTag.Tests
{
	// play mode: the matcher waits on frames, which an edit mode test gets only now and then
	public class MutualNearestMatcherTests
	{
		private const int D = FeatureSet.DescriptorSize;
		// past one frame's slice of rows
		private const int FeatureCount = 296;

		private static FeatureSet Features(uint seed)
		{
			Random random = new(seed);
			FeatureSet features = new(FeatureCount);
			for (int i = 0; i < FeatureCount; i++)
			{
				float3 direction = random.NextFloat3Direction();
				for (int c = 0; c < D; c++)
					features.descriptors[i * D + c] = (half)(direction[c % 3] * (c % 2 == 0 ? 0.25f : -0.25f));
			}

			return features;
		}

		[UnityTest]
		public IEnumerator FindsMutualNearestPairs_AsBruteForceDoes()
		{
			FeatureSet a = Features(21), b = Features(22);
			for (int c = 0; c < D; c++) b.descriptors[100 * D + c] = a.descriptors[80 * D + c];

			(int[] matches, float[] confidence) result = default;
			bool finished = false;
			async void Run()
			{
				result = await MutualNearestMatcher.MatchAsync(a, b, 0.1f);
				finished = true;
			}

			Run();
			for (int frames = 0; !finished && frames < 600; frames++) yield return null;
			Assert.IsTrue(finished);

			(int[] expected, float[] expectedConfidence) = BruteForce(a, b, 0.1f);
			CollectionAssert.AreEqual(expected, result.matches);
			for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expectedConfidence[i], result.confidence[i], 1e-4f);
			Assert.AreEqual(100, result.matches[80]);
		}

		private static (int[], float[]) BruteForce(FeatureSet a, FeatureSet b, float minCosine)
		{
			int[] bestForA = new int[a.Count], bestForB = new int[b.Count], matches = new int[a.Count];
			float[] simForA = new float[a.Count], simForB = new float[b.Count], confidence = new float[a.Count];
			for (int j = 0; j < b.Count; j++) simForB[j] = -2;
			for (int i = 0; i < a.Count; i++)
			{
				simForA[i] = -2;
				for (int j = 0; j < b.Count; j++)
				{
					float dot = 0;
					for (int c = 0; c < D; c++) dot += a.descriptors[i * D + c] * b.descriptors[j * D + c];
					if (dot > simForA[i])
					{
						simForA[i] = dot;
						bestForA[i] = j;
					}

					if (dot > simForB[j])
					{
						simForB[j] = dot;
						bestForB[j] = i;
					}
				}
			}

			for (int i = 0; i < a.Count; i++)
			{
				bool mutual = bestForB[bestForA[i]] == i && simForA[i] >= minCosine;
				matches[i] = mutual ? bestForA[i] : -1;
				confidence[i] = mutual ? simForA[i] : 0;
			}

			return (matches, confidence);
		}
	}
}
