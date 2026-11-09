using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// The pockets of empty space inside a chunk that reach its faces, and exactly where
	/// each reaches them: per component, a 30×30 bitmask per face of the voxels it
	/// holds there. Two components in neighbouring chunks are joined when their masks on
	/// the shared face overlap. Faces are numbered -X, +X, -Y, +Y, -Z, +Z, so a face's
	/// opposite is <c>face ^ 1</c>. Face masks are rows of z (bits y) on X faces, rows of
	/// z (bits x) on Y faces and rows of y (bits x) on Z faces, the same on both sides
	/// of a face. Space never observed counts as empty: holes in the scan only ever make
	/// a chunk more open
	/// </summary>
	public static class ChunkPortals
	{
		public const int FaceCount = 6;

		/// <summary>Voxels a chunk owns per axis; the outer layer is the neighbours' apron.</summary>
		public const int Span = 30;

		public const int Dim = Span + 2;
		public const uint RowMask = (1u << Span) - 1;

		/// <summary>Words per component: a mask per face.</summary>
		public const int ComponentWords = FaceCount * Span;

		/// <summary>Past this many, the rest of a chunk's empty space is one component.</summary>
		public const int MaxComponents = 16;

		public static readonly int3[] FaceSteps =
		{
			new(-1, 0, 0), new(1, 0, 0),
			new(0, -1, 0), new(0, 1, 0),
			new(0, 0, -1), new(0, 0, 1)
		};

		/// <summary>Whether a face mask (<see cref="Span"/> words from <paramref name="from"/>) holds any voxel.</summary>
		public static bool Any(NativeArray<uint> masks, int from)
		{
			for (int i = 0; i < Span; i++)
				if (masks[from + i] != 0) return true;
			return false;
		}

		public static bool Overlap(NativeArray<uint> a, int aFrom, NativeArray<uint> b, int bFrom)
		{
			for (int i = 0; i < Span; i++)
				if ((a[aFrom + i] & b[bFrom + i]) != 0) return true;
			return false;
		}

		/// <summary>
		/// The mean (row, bit) of the voxels two face masks share, false when they share none
		/// </summary>
		public static bool SharedCentroid(NativeArray<uint> a, int aFrom, NativeArray<uint> b, int bFrom,
			out float2 rowAndBit)
		{
			float rowSum = 0, bitSum = 0;
			int count = 0;
			for (int row = 0; row < Span; row++)
			{
				uint shared = a[aFrom + row] & b[bFrom + row];
				if (shared == 0) continue;

				int bits = math.countbits(shared);
				count += bits;
				rowSum += row * bits;
				// sum of set bit indices: each index bit k adds 2^k for every set bit that has it
				bitSum += math.countbits(shared & 0xAAAAAAAAu)
				          + 2 * math.countbits(shared & 0xCCCCCCCCu)
				          + 4 * math.countbits(shared & 0xF0F0F0F0u)
				          + 8 * math.countbits(shared & 0xFF00FF00u)
				          + 16 * math.countbits(shared & 0xFFFF0000u);
			}

			rowAndBit = count > 0 ? new float2(rowSum, bitSum) / count : default;
			return count > 0;
		}

		/// <summary>
		/// Which of a chunk's stored components a flood (from <see cref="ContainingComponentJob"/>)
		/// is, -1 when it reaches no face. Components don't overlap, so touching one is being it
		/// </summary>
		public static int MatchComponent(NativeArray<uint> flood, NativeArray<uint> masks, int slotOffset, int count)
		{
			for (int k = 0; k < count; k++)
			for (int face = 0; face < FaceCount; face++)
				if (Overlap(flood, face * Span, masks, slotOffset + k * ComponentWords + face * Span))
					return k;
			return -1;
		}

		// the owned voxels of a row (y + z * Dim), as bits of x, for rows 1..Span on y and z
		private const uint Interior = RowMask << 1;
		private const int Last = Span;

		private static void LoadEmpty(NativeArray<uint> solidBits, NativeArray<uint> empty)
		{
			for (int r = 0; r < Dim * Dim; r++) empty[r] = 0;
			for (int z = 1; z <= Last; z++)
			for (int y = 1; y <= Last; y++)
			{
				int r = y + z * Dim;
				empty[r] = ~solidBits[r] & Interior;
			}
		}

		// 6-connected: a surface one voxel thick seals. Rows grow along x by shifting to
		// a fixed point and take from their four neighbouring rows; sweeps alternate
		// direction so long paths settle in few passes
		private static void Flood(NativeArray<uint> empty, NativeArray<uint> fill)
		{
			const int rows = Span * Span;
			bool changed = true;
			bool forward = true;
			while (changed)
			{
				changed = false;
				for (int i = 0; i < rows; i++)
				{
					int j = forward ? i : rows - 1 - i;
					int r = 1 + j % Span + (1 + j / Span) * Dim;

					uint v = fill[r];
					uint grow = (v | fill[r - 1] | fill[r + 1] | fill[r - Dim] | fill[r + Dim]) & empty[r];
					if (grow == 0) continue;

					uint prev;
					do
					{
						prev = grow;
						grow = (grow | (grow << 1) | (grow >> 1)) & empty[r];
					} while (grow != prev);

					if (grow == v) continue;
					fill[r] = grow;
					changed = true;
				}

				forward = !forward;
			}
		}

		private static void ExtractFaces(NativeArray<uint> fill, NativeArray<uint> masks, int offset)
		{
			for (int i = 0; i < Span; i++)
			{
				int a = i + 1;
				uint nx = 0, px = 0;
				for (int j = 0; j < Span; j++)
				{
					uint row = fill[j + 1 + a * Dim];
					nx |= ((row >> 1) & 1u) << j;
					px |= ((row >> Last) & 1u) << j;
				}

				masks[offset + 0 * Span + i] = nx;
				masks[offset + 1 * Span + i] = px;
				masks[offset + 2 * Span + i] = (fill[1 + a * Dim] >> 1) & RowMask;
				masks[offset + 3 * Span + i] = (fill[Last + a * Dim] >> 1) & RowMask;
				masks[offset + 4 * Span + i] = (fill[a + 1 * Dim] >> 1) & RowMask;
				masks[offset + 5 * Span + i] = (fill[a + Last * Dim] >> 1) & RowMask;
			}
		}

		private static bool IsFaceRow(int r)
		{
			int y = r % Dim, z = r / Dim;
			return y == 1 || y == Last || z == 1 || z == Last;
		}

		/// <summary>Splits a chunk's empty space into the components that reach its faces.</summary>
		[BurstCompile]
		public struct ComponentsJob : IJob
		{
			/// <summary>
			/// <see cref="Chunk.voxelSignBits"/>: a set bit is behind a surface. One word is
			/// one x row; row = y + z * dim
			/// </summary>
			[ReadOnly] public NativeArray<uint> solidBits;

			/// <summary><see cref="MaxComponents"/> × <see cref="ComponentWords"/>.</summary>
			[WriteOnly] public NativeArray<uint> masks;

			[WriteOnly] public NativeArray<int> count;

			public void Execute()
			{
				// a row must be exactly one word
				if (solidBits.Length != Dim * Dim)
				{
					for (int i = 0; i < ComponentWords; i++) masks[i] = RowMask;
					count[0] = 1;
					return;
				}

				NativeArray<uint> empty = new(Dim * Dim, Allocator.Temp);
				NativeArray<uint> fill = new(Dim * Dim, Allocator.Temp);
				LoadEmpty(solidBits, empty);

				int n = 0;
				while (n < MaxComponents)
				{
					int seedRow = -1;
					uint seedBits = 0;
					for (int r = Dim; r < Dim * (Dim - 1) && seedRow < 0; r++)
					{
						uint face = empty[r] & (IsFaceRow(r) ? Interior : (1u << 1) | (1u << Last));
						if (face == 0) continue;
						seedRow = r;
						seedBits = face & (~face + 1);
					}

					if (seedRow < 0) break;

					if (n == MaxComponents - 1)
					{
						// everything left, as one
						for (int r = 0; r < Dim * Dim; r++) fill[r] = empty[r];
					}
					else
					{
						for (int r = 0; r < Dim * Dim; r++) fill[r] = 0;
						fill[seedRow] = seedBits;
						Flood(empty, fill);
					}

					ExtractFaces(fill, masks, n * ComponentWords);
					n++;

					for (int r = 0; r < Dim * Dim; r++) empty[r] &= ~fill[r];
				}

				count[0] = n;
			}
		}

		/// <summary>The face masks of the empty component holding one voxel, if it is empty.</summary>
		[BurstCompile]
		public struct ContainingComponentJob : IJob
		{
			[ReadOnly] public NativeArray<uint> solidBits;
			public int3 voxel;

			[WriteOnly] public NativeArray<uint> masks;
			[WriteOnly] public NativeArray<int> found;

			public void Execute()
			{
				NativeArray<uint> empty = new(Dim * Dim, Allocator.Temp);
				NativeArray<uint> fill = new(Dim * Dim, Allocator.Temp);
				LoadEmpty(solidBits, empty);

				int r = voxel.y + voxel.z * Dim;
				uint bit = 1u << voxel.x;
				if ((empty[r] & bit) == 0)
				{
					found[0] = 0;
					return;
				}

				fill[r] = bit;
				Flood(empty, fill);
				ExtractFaces(fill, masks, 0);
				found[0] = 1;
			}
		}
	}

	/// <summary>
	/// Every chunk's <see cref="ChunkPortals"/> components in native memory, so jobs can read
	/// them. Each chunk holds a fixed slot of <see cref="SlotWords"/>; a chunk with nothing
	/// stored reads as <see cref="OpenSlot"/>. Jobs that read it are registered with
	/// <see cref="AddReader"/> and finish before anything changes
	/// </summary>
	public sealed class ChunkPortalStore : IDisposable
	{
		public const int SlotWords = ChunkPortals.MaxComponents * ChunkPortals.ComponentWords;

		/// <summary>One component open on every voxel of every face.</summary>
		public const int OpenSlot = 0;

		private NativeHashMap<int, int> slotOfChunk;
		private NativeList<uint> masks;
		private NativeList<int> componentCounts;
		// -1 for the open slot and free slots
		private NativeList<int> chunkOfSlot;
		private readonly Stack<int> freeSlots = new();
		private JobHandle readers;

		/// <summary>Changes whenever any chunk's components do.</summary>
		public int Revision { get; private set; }

		public NativeHashMap<int, int> SlotOfChunk => slotOfChunk;
		public NativeArray<uint> Masks => masks.AsArray();
		public NativeArray<int> ComponentCounts => componentCounts.AsArray();
		public NativeArray<int> ChunkOfSlot => chunkOfSlot.AsArray();

		public ChunkPortalStore()
		{
			slotOfChunk = new NativeHashMap<int, int>(256, Allocator.Persistent);
			masks = new NativeList<uint>(SlotWords, Allocator.Persistent);
			componentCounts = new NativeList<int>(Allocator.Persistent);
			chunkOfSlot = new NativeList<int>(Allocator.Persistent);
			AddOpenSlot();
		}

		private void AddOpenSlot()
		{
			masks.Resize(SlotWords, NativeArrayOptions.ClearMemory);
			for (int i = 0; i < ChunkPortals.ComponentWords; i++) masks[i] = ChunkPortals.RowMask;
			componentCounts.Add(1);
			chunkOfSlot.Add(-1);
		}

		public int SlotOf(int chunkIndex) => slotOfChunk.TryGetValue(chunkIndex, out int slot) ? slot : OpenSlot;

		public int ComponentCount(int slot) => componentCounts[slot];

		public static int MaskOffset(int slot, int component) =>
			slot * SlotWords + component * ChunkPortals.ComponentWords;

		/// <summary>Stores a chunk's first <paramref name="count"/> components from <paramref name="chunkMasks"/>.</summary>
		public void Set(int chunkIndex, NativeArray<uint> chunkMasks, int count)
		{
			CompleteReaders();

			if (!slotOfChunk.TryGetValue(chunkIndex, out int slot))
			{
				slot = freeSlots.Count > 0 ? freeSlots.Pop() : AddSlot();
				slotOfChunk.Add(chunkIndex, slot);
				chunkOfSlot[slot] = chunkIndex;
			}

			NativeArray<uint>.Copy(chunkMasks, 0, masks.AsArray(), slot * SlotWords, count * ChunkPortals.ComponentWords);
			componentCounts[slot] = count;
			Revision++;
		}

		/// <summary>The chunk reads as open again.</summary>
		public void Remove(int chunkIndex)
		{
			CompleteReaders();
			if (!slotOfChunk.TryGetValue(chunkIndex, out int slot)) return;

			slotOfChunk.Remove(chunkIndex);
			chunkOfSlot[slot] = -1;
			componentCounts[slot] = 0;
			freeSlots.Push(slot);
			Revision++;
		}

		public void Clear()
		{
			CompleteReaders();
			slotOfChunk.Clear();
			masks.Clear();
			componentCounts.Clear();
			chunkOfSlot.Clear();
			freeSlots.Clear();
			AddOpenSlot();
			Revision++;
		}

		private int AddSlot()
		{
			masks.Resize(masks.Length + SlotWords, NativeArrayOptions.UninitializedMemory);
			componentCounts.Add(0);
			chunkOfSlot.Add(-1);
			return componentCounts.Length - 1;
		}

		/// <summary>A job reading the store; it finishes before the store next changes.</summary>
		public void AddReader(JobHandle job) => readers = JobHandle.CombineDependencies(readers, job);

		public void CompleteReaders()
		{
			readers.Complete();
			readers = default;
		}

		public void Dispose()
		{
			CompleteReaders();
			if (slotOfChunk.IsCreated) slotOfChunk.Dispose();
			if (masks.IsCreated) masks.Dispose();
			if (componentCounts.IsCreated) componentCounts.Dispose();
			if (chunkOfSlot.IsCreated) chunkOfSlot.Dispose();
		}
	}
}
