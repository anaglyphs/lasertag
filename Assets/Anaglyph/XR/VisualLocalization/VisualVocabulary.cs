using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Anaglyph.XR.VisualLocalization
{
	/// <summary>
	/// A vocabulary of XFeat descriptors: hierarchical k-means, each leaf a word, the shape
	/// bag-of-words place recognition has used since DBoW. A frame's features quantized to
	/// words, kept as a bitset of the words a view shows, is a 512-byte place descriptor:
	/// which saved view, of which space, shows what the camera sees now can be ranked from
	/// thousands of them in well under a millisecond. Built once, from the first map saved,
	/// on a worker thread; shared by every space after.
	/// </summary>
	public sealed class VisualVocabulary
	{
		public const int Branching = 8, Depth = 4;
		/// <summary>Leaves: Branching^Depth.</summary>
		public const int Words = 4096;
		/// <summary>ulongs in a word bitset.</summary>
		public const int BitsetLength = Words / 64;
		private const int D = FeatureSet.DescriptorSize;
		private const int NodeCount = (Words * Branching - 1) / (Branching - 1);
		private const int FirstLeaf = NodeCount - Words;
		private const uint Magic = 0x434F5656; // VVOC
		private const int Version = 1;

		// a complete tree in level order: node n's children are n·Branching + 1 … + Branching.
		// A child that got no samples is never descended into
		private readonly float[] centroids = new float[NodeCount * D];
		private readonly bool[] valid = new bool[NodeCount];

		/// <summary>A hash of the centroids, kept with every index built from them: an index built with another vocabulary says nothing.</summary>
		public ulong Id { get; private set; }

		private VisualVocabulary() { }

		// ---- building ----------------------------------------------------------

		/// <summary>
		/// Builds from a sample of descriptors (rows of <see cref="FeatureSet.DescriptorSize"/>
		/// halves): seconds of plain arithmetic, for a worker thread.
		/// </summary>
		public static VisualVocabulary Build(IReadOnlyList<half[]> sample, int seed = 1)
		{
			int n = sample.Count;
			float[] data = new float[n * D];
			for (int i = 0; i < n; i++)
				for (int c = 0; c < D; c++)
					data[i * D + c] = sample[i][c];

			VisualVocabulary v = new();
			int[] all = new int[n];
			for (int i = 0; i < n; i++) all[i] = i;
			Unity.Mathematics.Random random = new((uint)math.max(1, seed));
			v.BuildNode(0, 0, data, all, n, ref random);
			v.Id = v.Hash();
			return v;
		}

		private void BuildNode(int node, int level, float[] data, int[] samples, int count, ref Unity.Mathematics.Random random)
		{
			if (count == 0) return;
			valid[node] = true;
			Mean(data, samples, count, centroids, node * D);
			if (level == Depth) return;

			// k-means over the samples, k children at most, seeded from distinct samples
			int k = math.min(Branching, count);
			float[] centers = new float[k * D];
			int[] assignment = new int[count];
			int[] order = new int[count];
			for (int i = 0; i < count; i++) order[i] = i;
			for (int i = 0; i < k; i++)
			{
				int j = i + random.NextInt(count - i);
				(order[i], order[j]) = (order[j], order[i]);
				Array.Copy(data, samples[order[i]] * D, centers, i * D, D);
			}

			int[] sizes = new int[k];
			for (int iteration = 0; iteration < 8; iteration++)
			{
				bool changed = false;
				for (int i = 0; i < count; i++)
				{
					int best = Nearest(data, samples[i] * D, centers, k);
					if (assignment[i] != best) changed = true;
					assignment[i] = best;
				}

				if (!changed && iteration > 0) break;
				Array.Clear(centers, 0, centers.Length);
				Array.Clear(sizes, 0, k);
				for (int i = 0; i < count; i++)
				{
					int a = assignment[i], s = samples[i] * D;
					sizes[a]++;
					for (int c = 0; c < D; c++) centers[a * D + c] += data[s + c];
				}

				for (int a = 0; a < k; a++)
				{
					if (sizes[a] == 0)
					{
						// an empty cluster takes a sample of its own
						Array.Copy(data, samples[random.NextInt(count)] * D, centers, a * D, D);
						continue;
					}

					Normalize(centers, a * D);
				}
			}

			// the children's samples
			int[][] children = new int[k][];
			for (int a = 0; a < k; a++) children[a] = new int[sizes[a]];
			int[] fill = new int[k];
			for (int i = 0; i < count; i++)
			{
				int a = assignment[i];
				children[a][fill[a]++] = samples[i];
			}

			for (int a = 0; a < k; a++)
				BuildNode(node * Branching + 1 + a, level + 1, data, children[a], children[a].Length, ref random);
		}

		private static void Mean(float[] data, int[] samples, int count, float[] into, int at)
		{
			Array.Clear(into, at, D);
			for (int i = 0; i < count; i++)
			{
				int s = samples[i] * D;
				for (int c = 0; c < D; c++) into[at + c] += data[s + c];
			}

			Normalize(into, at);
		}

		private static void Normalize(float[] v, int at)
		{
			float sum = 0;
			for (int c = 0; c < D; c++) sum += v[at + c] * v[at + c];
			float inv = sum > 0 ? 1f / math.sqrt(sum) : 0;
			for (int c = 0; c < D; c++) v[at + c] *= inv;
		}

		private static int Nearest(float[] data, int at, float[] centers, int k)
		{
			int best = 0;
			float bestDot = float.NegativeInfinity;
			for (int a = 0; a < k; a++)
			{
				float dot = 0;
				for (int c = 0; c < D; c++) dot += data[at + c] * centers[a * D + c];
				if (dot <= bestDot) continue;
				bestDot = dot;
				best = a;
			}

			return best;
		}

		// ---- quantizing --------------------------------------------------------

		/// <summary>The word of each of the first <paramref name="count"/> descriptors (rows of halves).</summary>
		public int[] Quantize(half[] descriptors, int count)
		{
			int[] words = new int[count];
			float[] row = new float[D];
			for (int i = 0; i < count; i++)
			{
				for (int c = 0; c < D; c++) row[c] = descriptors[i * D + c];
				words[i] = Word(row);
			}

			return words;
		}

		private int Word(float[] row)
		{
			int node = 0;
			for (int level = 0; level < Depth; level++)
			{
				int first = node * Branching + 1, best = -1;
				float bestDot = float.NegativeInfinity;
				for (int a = 0; a < Branching; a++)
				{
					int child = first + a;
					if (!valid[child]) continue;
					float dot = 0;
					int at = child * D;
					for (int c = 0; c < D; c++) dot += row[c] * centroids[at + c];
					if (dot <= bestDot) continue;
					bestDot = dot;
					best = child;
				}

				if (best < 0) break; // a node without children: the word is the deepest reached, below
				node = best;
			}

			// a word is a leaf; a shallower node stands in for its first leaf
			while (node < FirstLeaf) node = node * Branching + 1;
			return node - FirstLeaf;
		}

		/// <summary>The bitset of the words the descriptors show.</summary>
		public ulong[] Bitset(half[] descriptors, int count)
		{
			ulong[] bits = new ulong[BitsetLength];
			foreach (int word in Quantize(descriptors, count)) bits[word >> 6] |= 1ul << (word & 63);
			return bits;
		}

		// ---- files -------------------------------------------------------------

		public void Write(Stream stream)
		{
			using BinaryWriter w = new(stream, System.Text.Encoding.UTF8, true);
			w.Write(Magic);
			w.Write(Version);
			w.Write(Branching);
			w.Write(Depth);
			w.Write(Id);
			for (int i = 0; i < NodeCount; i++) w.Write(valid[i]);
			for (int i = 0; i < centroids.Length; i++) w.Write(((half)centroids[i]).value);
		}

		public static VisualVocabulary Read(Stream stream)
		{
			using BinaryReader r = new(stream, System.Text.Encoding.UTF8, true);
			if (r.ReadUInt32() != Magic || r.ReadInt32() != Version || r.ReadInt32() != Branching || r.ReadInt32() != Depth) return null;
			VisualVocabulary v = new() { Id = r.ReadUInt64() };
			for (int i = 0; i < NodeCount; i++) v.valid[i] = r.ReadBoolean();
			for (int i = 0; i < v.centroids.Length; i++) v.centroids[i] = new half { value = r.ReadUInt16() };
			return v.Hash() == v.Id ? v : null;
		}

		private ulong Hash()
		{
			// FNV-1a over the centroids as the file holds them
			ulong h = 14695981039346656037ul;
			for (int i = 0; i < centroids.Length; i++)
			{
				h ^= ((half)centroids[i]).value;
				h *= 1099511628211ul;
			}

			for (int i = 0; i < NodeCount; i++)
			{
				h ^= valid[i] ? 1ul : 0ul;
				h *= 1099511628211ul;
			}

			return h;
		}
	}

	/// <summary>
	/// Saved views' word bitsets across spaces, ranked against a query's: each word
	/// weighted by how rare it is among the saved views, and a view's score the share of
	/// the query's weight it shows. A space is scored by its best few views.
	/// </summary>
	public sealed class VisualPlaceIndex
	{
		public struct View
		{
			public string space;
			public ulong[] bits;
		}

		private readonly List<View> views = new();
		private float[] idf;

		public int Count => views.Count;

		public void Add(string space, ulong[] bits)
		{
			views.Add(new View { space = space, bits = bits });
			idf = null;
		}

		private void Prepare()
		{
			int[] df = new int[VisualVocabulary.Words];
			foreach (View v in views)
				for (int w = 0; w < VisualVocabulary.Words; w++)
					if ((v.bits[w >> 6] & (1ul << (w & 63))) != 0) df[w]++;
			idf = new float[VisualVocabulary.Words];
			for (int w = 0; w < VisualVocabulary.Words; w++) idf[w] = math.log((views.Count + 1f) / (df[w] + 1f));
		}

		/// <summary>The share of the query's word weight this view shows: 0 to 1.</summary>
		public float Score(ulong[] query, ulong[] view)
		{
			if (idf == null) Prepare();
			float shared = 0, total = 0;
			for (int i = 0; i < VisualVocabulary.BitsetLength; i++)
			{
				ulong q = query[i];
				if (q == 0) continue;
				ulong both = q & view[i];
				for (int b = 0; b < 64; b++)
				{
					ulong bit = 1ul << b;
					if ((q & bit) == 0) continue;
					float weight = idf[i * 64 + b];
					total += weight;
					if ((both & bit) != 0) shared += weight;
				}
			}

			return total > 0 ? shared / total : 0;
		}

		/// <summary>Each space's score: the sum of its <paramref name="top"/> best views' scores.</summary>
		public Dictionary<string, float> ScoreSpaces(ulong[] query, int top = 3)
		{
			Dictionary<string, List<float>> perSpace = new();
			foreach (View v in views)
			{
				if (!perSpace.TryGetValue(v.space, out List<float> scores)) perSpace[v.space] = scores = new List<float>();
				scores.Add(Score(query, v.bits));
			}

			Dictionary<string, float> result = new();
			foreach ((string space, List<float> scores) in perSpace)
			{
				scores.Sort((a, b) => b.CompareTo(a));
				float sum = 0;
				for (int i = 0; i < scores.Count && i < top; i++) sum += scores[i];
				result[space] = sum;
			}

			return result;
		}
	}
}
