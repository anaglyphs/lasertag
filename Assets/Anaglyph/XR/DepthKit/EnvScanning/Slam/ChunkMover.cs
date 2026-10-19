using System;
using System.Collections.Generic;
using Anaglyph.GPU;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning.Slam
{
	/// <summary>
	/// Applies the map half of a pose-graph solve. Each group of chunks (those anchored to
	/// one node) moves by one rigid delta. The meshes move at once, as transforms; the TSDF
	/// data is resampled on the GPU back onto the axis-aligned grid, a few destination
	/// chunks per frame, and every touched chunk is re-meshed in place once the queue is
	/// empty. Until a chunk's data has settled it is reported as pending, and integration
	/// must leave it alone. Every headset applies the same groups in the same order, so
	/// their maps move identically
	/// </summary>
	public sealed class ChunkMover : IDisposable
	{
		public readonly struct Group
		{
			public readonly Pose4 delta;
			public readonly int[] chunks;

			public Group(Pose4 delta, int[] chunks)
			{
				this.delta = delta;
				this.chunks = chunks;
			}
		}

		private sealed class Job
		{
			public Group[] groups;
			public List<int> sources;
			public List<int> destinations;
			public List<(int group, int chunk)> fills;
			public int nextFill;
			public HashSet<int> touched;
			public RenderTexture snapshot;
			public ComputeBuffer sourceList, destinationList, fillList, slotGroups;
		}

		/// <summary>Destination chunks resampled per frame.</summary>
		public int FillChunksPerFrame = 24;

		private readonly EnvScanner scanner;
		private readonly ComputeShader compute;
		private readonly ComputeKernel snapshotKernel, clearKernel, reserveKernel, fillKernel;
		private readonly Queue<Job> queue = new();
		private Job active;
		private readonly HashSet<int> awaitingRemesh = new();

		private readonly Dictionary<int, int> pendingCount = new();
		private int[] slotOfChunk;
		private ComputeBuffer slotOfChunkBuffer;

		/// <summary>One bit per chunk table entry: chunks whose data is still moving.</summary>
		public uint[] PendingBits { get; }
		public int PendingRevision { get; private set; }
		public bool HasPending => pendingCount.Count > 0;
		public bool IsBusy => active != null || queue.Count > 0;

		/// <summary>Every queued move has been resampled and its chunks re-queued for meshing.</summary>
		public event Action Settled = delegate { };

		private static readonly int listOffsetID = Shader.PropertyToID("listOffset");
		private static readonly int chunkCountID = Shader.PropertyToID("chunkCount");
		private static readonly int snapshotDimsID = Shader.PropertyToID("snapshotDims");
		private static readonly int groupIdID = Shader.PropertyToID("groupId");
		private static readonly int inverseTranslationID = Shader.PropertyToID("inverseTranslation");
		private static readonly int inverseYawID = Shader.PropertyToID("inverseYaw");

		public ChunkMover(EnvScanner scanner)
		{
			this.scanner = scanner;
			compute = Resources.Load<ComputeShader>("ChunkResample");
			snapshotKernel = new ComputeKernel(compute, "SnapshotChunks");
			clearKernel = new ComputeKernel(compute, "ClearChunks");
			reserveKernel = new ComputeKernel(compute, "ReserveChunks");
			fillKernel = new ComputeKernel(compute, "FillGroup");
			PendingBits = new uint[scanner.LockWordCount];
		}

		public bool IsPending(int chunk) => pendingCount.ContainsKey(chunk);

		/// <summary>
		/// Moves these groups: their meshes now, their data over the next frames. Groups of
		/// one call must not share chunks
		/// </summary>
		public void Enqueue(IReadOnlyList<Group> groups)
		{
			List<Group> kept = new();
			foreach (Group g in groups)
				if (g.chunks != null && g.chunks.Length > 0)
					kept.Add(g);
			if (kept.Count == 0) return;

			// the meshes show the move immediately; each keeps its transform until it is
			// re-meshed from the resampled data
			EnvMesher mesher = EnvMesher.Instance;
			if (mesher != null)
			{
				foreach (Group g in kept)
				foreach (int chunkIndex in g.chunks)
				{
					if (!mesher.TryGetChunk(chunkIndex, out Chunk chunk) || !chunk) continue;
					Transform t = chunk.transform;
					t.SetPositionAndRotation(g.delta.Apply((float3)t.position), (Quaternion)g.delta.Rotation * t.rotation);
				}
			}

			Job job = new() { groups = kept.ToArray() };
			PlanJob(job);
			foreach (int chunk in job.touched) AddPending(chunk);
			queue.Enqueue(job);
		}

		/// <summary>Advances the resampling. Call once per frame.</summary>
		public void Tick()
		{
			if (scanner.LiveVolume == null || !scanner.LiveVolume.IsCreated) return;

			if (active == null)
			{
				if (queue.Count == 0) return;
				active = queue.Dequeue();
				Begin(active);
			}

			FillSome(active);

			if (active.nextFill < active.fills.Count) return;

			Finish(active);
			active = null;

			if (queue.Count == 0)
			{
				// re-meshed only now: a chunk re-meshed while a later move is still queued
				// would drop that move's transform before its data has followed
				if (EnvMesher.Instance != null)
					foreach (int chunk in awaitingRemesh)
						EnvMesher.Instance.ForceRemesh(chunk);
				awaitingRemesh.Clear();
				Settled.Invoke();
			}
		}

		/// <summary>Drops everything queued: the scan was cleared, so there is nothing to move.</summary>
		public void Clear()
		{
			if (active != null) Release(active);
			active = null;
			while (queue.Count > 0) Release(queue.Dequeue());
			awaitingRemesh.Clear();
			pendingCount.Clear();
			Array.Clear(PendingBits, 0, PendingBits.Length);
			PendingRevision++;
		}

		public void Dispose()
		{
			Clear();
			slotOfChunkBuffer?.Release();
			slotOfChunkBuffer = null;
		}

		// ---- planning ----------------------------------------------------------

		private void PlanJob(Job job)
		{
			job.sources = new List<int>();
			job.destinations = new List<int>();
			job.fills = new List<(int, int)>();
			job.touched = new HashSet<int>();

			HashSet<int> destinationSet = new();
			HashSet<int> groupDestinations = new();
			float vox = scanner.VoxSize;
			float extent = vox * (scanner.VoxPerChunkDim - 1);
			float3[] corners = new float3[8];

			for (int gi = 0; gi < job.groups.Length; gi++)
			{
				Group g = job.groups[gi];
				groupDestinations.Clear();

				foreach (int chunkIndex in g.chunks)
				{
					job.sources.Add(chunkIndex);
					job.touched.Add(chunkIndex);

					// every grid chunk the moved chunk's voxels can land in
					float3 corner = scanner.ChunkCoordToCornerWorldPos(scanner.ChunkIndexToChunkCoord(chunkIndex));
					for (int c = 0; c < 8; c++)
						corners[c] = g.delta.Apply(corner + new float3((c & 1) * extent, (c >> 1 & 1) * extent, (c >> 2 & 1) * extent));

					float3 min = corners[0], max = corners[0];
					for (int c = 1; c < 8; c++) { min = math.min(min, corners[c]); max = math.max(max, corners[c]); }

					int3 cMin = scanner.WorldPosToChunkCoord(min - vox);
					int3 cMax = scanner.WorldPosToChunkCoord(max + vox);
					for (int x = cMin.x; x <= cMax.x; x++)
					for (int y = cMin.y; y <= cMax.y; y++)
					for (int z = cMin.z; z <= cMax.z; z++)
					{
						int dest = scanner.ChunkCoordToChunkIndex(new int3(x, y, z));
						if (dest < 0 || !groupDestinations.Add(dest)) continue;
						job.fills.Add((gi, dest));
						job.touched.Add(dest);
						if (destinationSet.Add(dest)) job.destinations.Add(dest);
					}
				}
			}

			// fills sorted by group, so each frame's batch dispatches per group contiguously
			job.fills.Sort((a, b) => a.group != b.group ? a.group.CompareTo(b.group) : a.chunk.CompareTo(b.chunk));
		}

		// ---- GPU ---------------------------------------------------------------

		// each kernel gets exactly the resources it reads or writes
		private void BindAtlas(ComputeKernel kernel, bool data, bool counter, bool changeSums)
		{
			EnvVolume live = scanner.LiveVolume;
			kernel.Bind("chunkTable", live.ChunkTable);
			if (data) kernel.Bind("chunkData", live.ChunkData);
			if (counter) kernel.Bind("reservedChunkCounter", live.ReservedChunkCounter);
			if (changeSums) kernel.Bind("chunkChangeSums", live.ChunkChangeSums);
		}

		private void Begin(Job job)
		{
			EnvVolume live = scanner.LiveVolume;
			scanner.ConfigureAtlasReader(compute, live);
			compute.SetInt("maxNumChunks", live.MaxNumChunks);

			int count = job.sources.Count;
			int vpcd = scanner.VoxPerChunkDim;
			int3 dims = new(math.min(count, 8), math.min((count + 7) / 8, 8), (count + 63) / 64);
			job.snapshot = new RenderTexture(new RenderTextureDescriptor
			{
				width = dims.x * vpcd, height = dims.y * vpcd, volumeDepth = dims.z * vpcd,
				msaaSamples = 1, useMipMap = false, graphicsFormat = GraphicsFormat.R8G8_SNorm,
				dimension = TextureDimension.Tex3D, enableRandomWrite = true
			}) { name = "Chunk Resample Snapshot" };
			job.snapshot.Create();
			compute.SetInts(snapshotDimsID, dims.x, dims.y, dims.z);

			// slots: one per source, in group order
			int tableLength = scanner.ChunkTableLength;
			if (slotOfChunk == null || slotOfChunk.Length != tableLength)
			{
				slotOfChunk = new int[tableLength];
				for (int i = 0; i < tableLength; i++) slotOfChunk[i] = -1;
				slotOfChunkBuffer?.Release();
				slotOfChunkBuffer = new ComputeBuffer(tableLength, sizeof(int));
			}

			int[] slotGroups = new int[count];
			int slot = 0;
			for (int gi = 0; gi < job.groups.Length; gi++)
			foreach (int chunk in job.groups[gi].chunks)
			{
				slotOfChunk[chunk] = slot;
				slotGroups[slot++] = gi;
			}

			slotOfChunkBuffer.SetData(slotOfChunk);
			job.slotGroups = Upload(slotGroups);
			job.sourceList = Upload(job.sources.ToArray());
			job.destinationList = Upload(job.destinations.ToArray());
			int[] fillChunks = new int[job.fills.Count];
			for (int i = 0; i < fillChunks.Length; i++) fillChunks[i] = job.fills[i].chunk;
			job.fillList = Upload(fillChunks);

			// the slot table is only read by this job's fills; restore the CPU copy now
			foreach (int chunk in job.sources) slotOfChunk[chunk] = -1;

			// 1. snapshot every source before anything is written, 2. empty them
			BindAtlas(snapshotKernel, true, false, false);
			snapshotKernel.Bind("snapshot", job.snapshot);
			BindAtlas(clearKernel, true, false, false);
			BindAtlas(reserveKernel, false, true, false);

			compute.SetInt(listOffsetID, 0);
			compute.SetInt(chunkCountID, count);
			snapshotKernel.Bind("chunkList", job.sourceList);
			snapshotKernel.DispatchFit(vpcd, vpcd, vpcd * count);
			clearKernel.Bind("chunkList", job.sourceList);
			clearKernel.DispatchFit(vpcd, vpcd, vpcd * count);

			// 3. atlas space for every destination
			compute.SetInt(chunkCountID, job.destinations.Count);
			reserveKernel.Bind("chunkList", job.destinationList);
			reserveKernel.DispatchFit(job.destinations.Count, 1, 1);

		}

		private static ComputeBuffer Upload(int[] data)
		{
			ComputeBuffer buffer = new(math.max(data.Length, 1), sizeof(int));
			if (data.Length > 0) buffer.SetData(data);
			return buffer;
		}

		// 4. resample a batch of destinations, one dispatch per group run in the batch
		private void FillSome(Job job)
		{
			int end = math.min(job.fills.Count, job.nextFill + math.max(FillChunksPerFrame, 1));
			if (job.nextFill >= end) return;

			EnvVolume live = scanner.LiveVolume;
			scanner.ConfigureAtlasReader(compute, live);
			compute.SetInt("maxNumChunks", live.MaxNumChunks);
			int vpcd = scanner.VoxPerChunkDim;
			int3 sd = new(math.min(job.sources.Count, 8), math.min((job.sources.Count + 7) / 8, 8), (job.sources.Count + 63) / 64);
			compute.SetInts(snapshotDimsID, sd.x, sd.y, sd.z);
			BindAtlas(fillKernel, true, false, true);
			fillKernel.Bind("snapshot", job.snapshot);
			fillKernel.Bind("chunkList", job.fillList);
			fillKernel.Bind("slotOfChunk", slotOfChunkBuffer);
			fillKernel.Bind("slotGroup", job.slotGroups);

			// the GPU slot table was uploaded by this job's Begin; only one job is ever in flight
			int start = job.nextFill;
			while (start < end)
			{
				int group = job.fills[start].group;
				int stop = start;
				while (stop < end && job.fills[stop].group == group) stop++;

				Pose4 inverse = job.groups[group].delta.Inverse;
				compute.SetInt(groupIdID, group);
				compute.SetVector(inverseTranslationID, new Vector4(inverse.translation.x, inverse.translation.y, inverse.translation.z, 0));
				compute.SetFloat(inverseYawID, inverse.yaw);
				compute.SetInt(listOffsetID, start);
				compute.SetInt(chunkCountID, stop - start);
				fillKernel.DispatchFit(vpcd, vpcd, vpcd * (stop - start));
				start = stop;
			}

			job.nextFill = end;
		}

		private void Finish(Job job)
		{
			foreach (int chunk in job.touched)
			{
				awaitingRemesh.Add(chunk);
				RemovePending(chunk);
			}

			Release(job);
		}

		private static void Release(Job job)
		{
			if (job.snapshot) job.snapshot.Release();
			job.snapshot = null;
			job.sourceList?.Release();
			job.destinationList?.Release();
			job.fillList?.Release();
			job.slotGroups?.Release();
			job.sourceList = job.destinationList = job.fillList = job.slotGroups = null;
		}

		private void AddPending(int chunk)
		{
			pendingCount.TryGetValue(chunk, out int n);
			pendingCount[chunk] = n + 1;
			PendingBits[chunk >> 5] |= 1u << (chunk & 31);
			PendingRevision++;
		}

		private void RemovePending(int chunk)
		{
			if (!pendingCount.TryGetValue(chunk, out int n)) return;
			if (n > 1)
			{
				pendingCount[chunk] = n - 1;
				return;
			}

			pendingCount.Remove(chunk);
			PendingBits[chunk >> 5] &= ~(1u << (chunk & 31));
			PendingRevision++;
		}
	}
}
