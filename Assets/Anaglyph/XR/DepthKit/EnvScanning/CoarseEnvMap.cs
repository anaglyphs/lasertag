using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// A coarse grid of what depth rays have shown of the space: the cells rays passed
	/// through are empty, the cells they stopped in hold something solid, and the cells
	/// none has reached are unknown. Rays of any length feed it, so it reaches far past the
	/// fine scan at a fraction of its precision: for questions of extent, such as how large
	/// a room is
	/// </summary>
	public sealed class CoarseEnvMap : IDisposable
	{
		public enum Cell : byte
		{
			Unknown,
			Empty,
			Solid
		}

		// a cell is solid once this many rays stopped in it, and one in this many of the rays
		// that reached it did: a noisy reading or a passer-by doesn't make a wall
		private const int FewestStopsForSolid = 3;
		private const int RaysPerStopForSolid = 4;

		private Cells cells;

		public float CellMeters => cells.cellMeters;

		/// <param name="corner">World position of the grid's lowest corner.</param>
		public CoarseEnvMap(float3 corner, float3 extentMeters, float cellMeters)
		{
			int3 size = (int3)math.ceil(extentMeters / cellMeters);
			int cellCount = size.x * size.y * size.z;
			cells = new Cells
			{
				stops = new NativeArray<byte>(cellCount, Allocator.Persistent),
				passes = new NativeArray<byte>(cellCount, Allocator.Persistent),
				size = size,
				corner = corner,
				cellMeters = cellMeters,
			};
		}

		public void Dispose()
		{
			if (cells.stops.IsCreated) cells.stops.Dispose();
			if (cells.passes.IsCreated) cells.passes.Dispose();
		}

		/// <summary>Everything is unknown again.</summary>
		public void Clear()
		{
			cells.stops.AsSpan().Clear();
			cells.passes.AsSpan().Clear();
		}

		/// <summary>
		/// Depth rays from one eye. A ray passes every cell up to where it ends and stops in
		/// that one; a ray longer than <paramref name="farthestTrusted"/> only passes, as far
		/// as that. Rays shorter than <paramref name="nearestTrusted"/> say nothing
		/// </summary>
		/// <param name="rayEnds">World position each ray ends at, w = 0 where the sensor had no reading.</param>
		public void AddRays(float3 eye, NativeArray<float4> rayEnds, float nearestTrusted, float farthestTrusted)
		{
			new AddRaysJob
			{
				cells = cells,
				eye = eye,
				rayEnds = rayEnds,
				nearestTrusted = nearestTrusted,
				farthestTrusted = farthestTrusted,
			}.Run();
		}

		/// <summary>Nothing solid stays in the cells a box touches: someone is standing there.</summary>
		public void MarkEmpty(float3 boxMin, float3 boxMax)
		{
			int3 from = math.max((int3)math.floor((boxMin - cells.corner) / cells.cellMeters), 0);
			int3 to = math.min((int3)math.floor((boxMax - cells.corner) / cells.cellMeters), cells.size - 1);
			for (int z = from.z; z <= to.z; z++)
			for (int y = from.y; y <= to.y; y++)
			for (int x = from.x; x <= to.x; x++)
			{
				int index = cells.Index(new int3(x, y, z));
				cells.stops[index] = 0;
				cells.passes[index] = (byte)math.max((int)cells.passes[index], 1);
			}
		}

		public Cell StateAt(float3 world)
		{
			int3 cell = (int3)math.floor((world - cells.corner) / cells.cellMeters);
			return cells.Contains(cell) ? cells.StateAt(cells.Index(cell)) : Cell.Unknown;
		}

		/// <summary>
		/// Follows a ray until something solid or unknown stops it. Returns which, or
		/// <see cref="Cell.Empty"/> when it ran <paramref name="maxMeters"/> through space
		/// known to be empty, and in <paramref name="meters"/> how far it got
		/// </summary>
		public Cell Cast(float3 origin, float3 direction, float maxMeters, out float meters)
		{
			CellWalk walk = new((origin - cells.corner) / cells.cellMeters, math.normalize(direction), cells.cellMeters);
			meters = 0;
			if (!cells.Contains(walk.cell)) return Cell.Unknown;

			// the ray starts inside its own cell, so something solid there doesn't stop it
			Cell originState = cells.StateAt(cells.Index(walk.cell));
			if (originState == Cell.Unknown) return Cell.Unknown;
			bool leavingSolidOrigin = originState == Cell.Solid;

			for (walk.Advance(); walk.entered < maxMeters; walk.Advance())
			{
				meters = walk.entered;
				Cell state = cells.Contains(walk.cell) ? cells.StateAt(cells.Index(walk.cell)) : Cell.Unknown;
				if (state == Cell.Solid)
				{
					// somewhere inside the cell
					meters += cells.cellMeters * 0.5f;
					return Cell.Solid;
				}

				// straight from a solid cell into the unknown: what the cell holds lies that way
				if (state == Cell.Unknown) return leavingSolidOrigin ? Cell.Solid : Cell.Unknown;
				leavingSolidOrigin = false;
			}

			meters = maxMeters;
			return Cell.Empty;
		}

		/// <summary>How many cells are known to be empty and solid. Walks the whole grid: for diagnosis.</summary>
		public void CountKnownCells(out int empty, out int solid)
		{
			empty = solid = 0;
			for (int i = 0; i < cells.stops.Length; i++)
			{
				Cell state = cells.StateAt(i);
				if (state == Cell.Empty) empty++;
				else if (state == Cell.Solid) solid++;
			}
		}

		private struct Cells
		{
			// rays that ended in each cell, and rays that went on through it
			public NativeArray<byte> stops, passes;
			public int3 size;
			public float3 corner;
			public float cellMeters;

			public bool Contains(int3 cell) => math.all(cell >= 0) && math.all(cell < size);
			public int Index(int3 cell) => cell.x + size.x * (cell.y + size.y * cell.z);

			public Cell StateAt(int index)
			{
				int stopped = stops[index], passed = passes[index];
				if (stopped + passed == 0) return Cell.Unknown;
				bool solid = stopped >= FewestStopsForSolid && stopped * RaysPerStopForSolid >= stopped + passed;
				return solid ? Cell.Solid : Cell.Empty;
			}

			public void AddStop(int index) => Count(stops, passes, index);
			public void AddPass(int index) => Count(passes, stops, index);

			// both halve when one is full: their ratio holds, and recent rays keep counting
			private static void Count(NativeArray<byte> counted, NativeArray<byte> other, int index)
			{
				if (counted[index] == byte.MaxValue)
				{
					counted[index] = byte.MaxValue / 2;
					other[index] = (byte)(other[index] / 2);
				}

				counted[index]++;
			}
		}

		// steps a ray through the grid one cell at a time
		private struct CellWalk
		{
			public int3 cell;
			/// <summary>How far along the ray it entered <see cref="cell"/>, meters.</summary>
			public float entered;

			private readonly int3 step;
			// along the ray: to cross one cell on each axis, and to the next boundary on each
			private readonly float3 metersPerCell;
			private float3 metersToNextBoundary;

			public CellWalk(float3 originInCells, float3 direction, float cellMeters)
			{
				cell = (int3)math.floor(originInCells);
				entered = 0;
				step = (int3)math.sign(direction);
				// an axis the ray doesn't move along is never the next to be crossed
				metersPerCell = cellMeters / math.max(math.abs(direction), 1e-6f);
				float3 cellsToBoundary = math.select(originInCells - cell, cell + 1 - originInCells, direction > 0);
				metersToNextBoundary = cellsToBoundary * metersPerCell;
			}

			public void Advance()
			{
				float3 next = metersToNextBoundary;
				int axis = next.x <= next.y ? (next.x <= next.z ? 0 : 2) : (next.y <= next.z ? 1 : 2);
				entered = next[axis];
				cell[axis] += step[axis];
				metersToNextBoundary[axis] += metersPerCell[axis];
			}
		}

		[BurstCompile]
		private struct AddRaysJob : IJob
		{
			public Cells cells;
			public float3 eye;
			[ReadOnly] public NativeArray<float4> rayEnds;
			public float nearestTrusted, farthestTrusted;

			public void Execute()
			{
				float3 eyeInCells = (eye - cells.corner) / cells.cellMeters;
				for (int ray = 0; ray < rayEnds.Length; ray++)
				{
					if (rayEnds[ray].w == 0) continue;

					float3 toEnd = rayEnds[ray].xyz - eye;
					float length = math.length(toEnd);
					// written so NaN fails the test
					if (!(length >= nearestTrusted)) continue;

					bool stops = length <= farthestTrusted;
					float reach = math.min(length, farthestTrusted);
					CellWalk walk = new(eyeInCells, toEnd / length, cells.cellMeters);
					while (cells.Contains(walk.cell))
					{
						int index = cells.Index(walk.cell);
						walk.Advance();
						if (walk.entered >= reach)
						{
							if (stops) cells.AddStop(index);
							else cells.AddPass(index);
							break;
						}

						cells.AddPass(index);
					}
				}
			}
		}
	}
}
