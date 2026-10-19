using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Anaglyph.XR.DepthKit.EnvScanning.Registration;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// A TSDF scan on the CPU: every populated chunk of an <see cref="EnvVolume"/> plus the
	/// grid it was made on and where the rig was, saved to and loaded from a gzipped file.
	/// Made for testing registration offline: capture a scan, move the tracking space,
	/// capture another, and the two files carry their own ground truth
	/// (<see cref="GroundTruth"/>) wherever the room is fixed to the tracking space (XR
	/// Simulation, or one headset session without a recenter)
	/// </summary>
	public sealed class EnvScan : IDisposable
	{
		private const uint Magic = 0x534E5645; // "EVNS"
		private const int Version = 1;
		public const string Extension = ".envscan";

		public float VoxSize { get; private set; }
		public float DistanceTruncationBand { get; private set; }
		public int VoxPerChunkDim { get; private set; }
		public int3 ChunkTableDims { get; private set; }
		public float3 OriginOffset { get; private set; }

		public string Label { get; set; } = "";
		public DateTime SavedAtUtc { get; private set; }

		/// <summary>World pose of the tracking space when captured.</summary>
		public Pose TrackingSpace { get; set; } = Pose.identity;
		/// <summary>World pose of the head (camera) when captured.</summary>
		public Pose Head { get; set; } = Pose.identity;
		/// <summary><see cref="EnvScanner.SensorCorrection"/> when captured: the scan was made in K · world.</summary>
		public Matrix4x4 SensorCorrection { get; set; } = Matrix4x4.identity;

		/// <summary>Chunk index → voxels. Owned by the scan.</summary>
		public Dictionary<int, NativeArray<EnvScanner.Voxel>> Chunks { get; } = new();

		public int ChunkCount => Chunks.Count;

		/// <summary>
		/// Where the room's points land in this scan's world frame: the corrected tracking
		/// space, as a gravity-aligned transform
		/// </summary>
		public Pose4 ScanFrame =>
			Pose4.FromMatrix(SensorCorrection) * Pose4.FromPose(TrackingSpace.position, TrackingSpace.rotation);

		/// <summary>
		/// The transform registering <paramref name="source"/> onto <paramref name="target"/>
		/// should find (p_target = T p_source), assuming the room sat still in tracking space
		/// between the two captures
		/// </summary>
		public static Pose4 GroundTruth(EnvScan source, EnvScan target) => target.ScanFrame * source.ScanFrame.Inverse;

		/// <summary>Min and max chunk coordinates, inclusive.</summary>
		public (int3 min, int3 max) ChunkBounds
		{
			get
			{
				int3 min = int.MaxValue, max = int.MinValue;
				foreach (int index in Chunks.Keys)
				{
					int3 c = IndexToCoord(index);
					min = math.min(min, c);
					max = math.max(max, c);
				}

				return (min, max);
			}
		}

		private EnvScan() { }

		private int3 IndexToCoord(int index) =>
			new(index % ChunkTableDims.x, index / ChunkTableDims.x % ChunkTableDims.y, index / (ChunkTableDims.x * ChunkTableDims.y));

		private int CoordToIndex(int3 c) =>
			math.any(c < 0) || math.any(c >= ChunkTableDims) ? -1 : c.x + c.y * ChunkTableDims.x + c.z * ChunkTableDims.x * ChunkTableDims.y;

		/// <summary>Null when this scan was made on <paramref name="scanner"/>'s chunk grid, else why not.</summary>
		public string Incompatibility(EnvScanner scanner)
		{
			if (scanner.VoxPerChunkDim != VoxPerChunkDim) return $"voxPerChunkDim {VoxPerChunkDim} ≠ scanner {scanner.VoxPerChunkDim}";
			if (math.any(scanner.ChunkTableDims != ChunkTableDims)) return $"chunkTableDims {ChunkTableDims} ≠ scanner {scanner.ChunkTableDims}";
			if (math.abs(scanner.VoxSize - VoxSize) > 1e-6f) return $"voxSize {VoxSize} ≠ scanner {scanner.VoxSize}";
			if (math.abs(scanner.DistanceTruncationBand - DistanceTruncationBand) > 1e-6f) return $"truncation band {DistanceTruncationBand} ≠ scanner {scanner.DistanceTruncationBand}";
			if (math.any(math.abs(scanner.OriginOffset - OriginOffset) > 1e-6f)) return $"origin offset {OriginOffset} ≠ scanner {scanner.OriginOffset}";
			return null;
		}

		// ---- capture / upload --------------------------------------------------

		/// <summary>
		/// Reads every reserved chunk with at least one integrated voxel out of
		/// <paramref name="volume"/> (the live map unless given). Poses come from the main
		/// rig when it exists
		/// </summary>
		public static async Awaitable<EnvScan> CaptureAsync(EnvScanner scanner, EnvVolume volume = null, string label = "")
		{
			scanner.EnsureGpuResources();
			volume ??= scanner.LiveVolume;
			if (volume == null || !volume.IsCreated) throw new InvalidOperationException("volume is not created");

			EnvScan scan = NewFor(scanner);
			scan.Label = label ?? "";
			scan.SensorCorrection = scanner.SensorCorrection;
			if (MainXRRig.Instance)
			{
				Transform space = MainXRRig.TrackingSpace;
				scan.TrackingSpace = new Pose(space.position, space.rotation);
				if (MainXRRig.Camera)
				{
					Transform head = MainXRRig.Camera.transform;
					scan.Head = new Pose(head.position, head.rotation);
				}
			}

			TableReadback table = await ReadTable(volume);
			if (table.hasError) throw new InvalidOperationException("chunk table readback failed");

			EnvScanner.ChunkReadbackBuffer readback = scanner.CreateChunkReadbackBuffer();
			try
			{
				foreach (int index in table.reserved)
				{
					if (!await scanner.ReadbackChunkInto(index, readback, volume)) continue;
					NativeArray<EnvScanner.Voxel> voxels = readback.data;
					bool any = false;
					for (int i = 0; i < voxels.Length && !any; i++) any = !voxels[i].IsEmpty;
					if (!any) continue;
					scan.Chunks[index] = new NativeArray<EnvScanner.Voxel>(voxels, Allocator.Persistent);
				}
			}
			catch
			{
				scan.Dispose();
				throw;
			}
			finally
			{
				readback.Dispose();
			}

			return scan;
		}

		private readonly struct TableReadback
		{
			public readonly bool hasError;
			public readonly List<int> reserved;

			public TableReadback(bool hasError, List<int> reserved)
			{
				this.hasError = hasError;
				this.reserved = reserved;
			}
		}

		// reserved, valid chunk table entries; bits as in EnvScanner.compute
		private static async Awaitable<TableReadback> ReadTable(EnvVolume volume)
		{
			const int reservedBit = 0x20000000;
			UnityEngine.Rendering.AsyncGPUReadbackRequest req =
				await UnityEngine.Rendering.AsyncGPUReadback.RequestAsync(volume.ChunkTable);
			if (req.hasError) return new TableReadback(true, null);

			NativeArray<int> entries = req.GetData<int>();
			List<int> reserved = new();
			for (int i = 0; i < entries.Length; i++)
				if ((entries[i] & reservedBit) != 0 && entries[i] >= 0) reserved.Add(i);
			return new TableReadback(false, reserved);
		}

		/// <summary>
		/// Fuses every chunk into <paramref name="volume"/> (the live map unless given),
		/// optionally moved by whole chunks — an exact translation of
		/// <paramref name="chunkShift"/> × <see cref="EnvScanner.ChunkWorldSizeDim"/>.
		/// Returns how many chunks went in. Clear the volume first for an exact copy
		/// </summary>
		public int UploadInto(EnvScanner scanner, EnvVolume volume = null, int3 chunkShift = default)
		{
			string why = Incompatibility(scanner);
			if (why != null) throw new InvalidOperationException($"scan not made on this scanner's grid: {why}");

			int uploaded = 0;
			foreach ((int index, NativeArray<EnvScanner.Voxel> voxels) in Chunks)
			{
				int shifted = CoordToIndex(IndexToCoord(index) + chunkShift);
				if (shifted >= 0 && scanner.UploadChunk(shifted, voxels, volume)) uploaded++;
			}

			return uploaded;
		}

		/// <summary>
		/// Allocates <paramref name="target"/> over this scan's chunks and writes them in.
		/// False when the scan spans more than a registration volume holds
		/// </summary>
		public bool FillRegistrationVolume(RegistrationVolume target)
		{
			if (Chunks.Count == 0) return false;
			(int3 min, int3 max) = ChunkBounds;
			if (!target.Allocate(min, max)) return false;
			foreach ((int index, NativeArray<EnvScanner.Voxel> voxels) in Chunks)
				target.FillFromVoxels(index, voxels);
			return true;
		}

		/// <summary>An empty scan on <paramref name="scanner"/>'s grid.</summary>
		public static EnvScan NewFor(EnvScanner scanner) => new()
		{
			VoxSize = scanner.VoxSize,
			DistanceTruncationBand = scanner.DistanceTruncationBand,
			VoxPerChunkDim = scanner.VoxPerChunkDim,
			ChunkTableDims = scanner.ChunkTableDims,
			OriginOffset = scanner.OriginOffset
		};

		// ---- files ---------------------------------------------------------------

		/// <summary>
		/// Where scans are kept by default: EnvScans/ beside Assets in the editor (outside
		/// the asset database), persistent data elsewhere
		/// </summary>
		public static string DefaultDirectory =>
			Application.isEditor
				? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "EnvScans"))
				: Path.Combine(Application.persistentDataPath, "EnvScans");

		/// <summary>A bare name resolves into <see cref="DefaultDirectory"/> with the extension added.</summary>
		public static string ResolvePath(string pathOrName)
		{
			string path = Path.IsPathRooted(pathOrName) || pathOrName.Contains('/') || pathOrName.Contains('\\')
				? pathOrName
				: Path.Combine(DefaultDirectory, pathOrName);
			return Path.HasExtension(path) ? path : path + Extension;
		}

		/// <summary>Writes the scan; returns the full path written.</summary>
		public string Save(string pathOrName)
		{
			string path = Path.GetFullPath(ResolvePath(pathOrName));
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			SavedAtUtc = DateTime.UtcNow;

			using FileStream file = File.Create(path);
			using GZipStream gzip = new(file, System.IO.Compression.CompressionLevel.Optimal);
			using BinaryWriter w = new(gzip);

			w.Write(Magic);
			w.Write(Version);
			w.Write(VoxSize);
			w.Write(DistanceTruncationBand);
			w.Write(VoxPerChunkDim);
			Write(w, ChunkTableDims);
			Write(w, OriginOffset);
			w.Write(Label ?? "");
			w.Write(SavedAtUtc.Ticks);
			Write(w, TrackingSpace);
			Write(w, Head);
			for (int i = 0; i < 16; i++) w.Write(SensorCorrection[i]);

			int voxelCount = VoxPerChunkDim * VoxPerChunkDim * VoxPerChunkDim;
			byte[] bytes = new byte[voxelCount * EnvScanner.Voxel.stride];
			// chunk table order, so the same scan always writes the same file
			List<int> indices = new(Chunks.Keys);
			indices.Sort();
			w.Write(indices.Count);
			foreach (int index in indices)
			{
				NativeArray<EnvScanner.Voxel> voxels = Chunks[index];
				w.Write(index);
				voxels.Reinterpret<byte>(EnvScanner.Voxel.stride).CopyTo(bytes);
				w.Write(bytes);
			}

			return path;
		}

		public static EnvScan Load(string pathOrName)
		{
			string path = ResolvePath(pathOrName);
			using FileStream file = File.OpenRead(path);
			using GZipStream gzip = new(file, CompressionMode.Decompress);
			using BinaryReader r = new(gzip);

			if (r.ReadUInt32() != Magic) throw new InvalidDataException($"{path} is not an env scan");
			int version = r.ReadInt32();
			if (version != Version) throw new InvalidDataException($"{path}: unsupported env scan version {version}");

			EnvScan scan = new()
			{
				VoxSize = r.ReadSingle(),
				DistanceTruncationBand = r.ReadSingle(),
				VoxPerChunkDim = r.ReadInt32(),
				ChunkTableDims = ReadInt3(r),
				OriginOffset = ReadFloat3(r),
				Label = r.ReadString(),
				SavedAtUtc = new DateTime(r.ReadInt64(), DateTimeKind.Utc),
				TrackingSpace = ReadPose(r),
				Head = ReadPose(r)
			};

			try
			{
				Matrix4x4 k = default;
				for (int i = 0; i < 16; i++) k[i] = r.ReadSingle();
				scan.SensorCorrection = k;

				int voxelCount = scan.VoxPerChunkDim * scan.VoxPerChunkDim * scan.VoxPerChunkDim;
				int byteCount = voxelCount * EnvScanner.Voxel.stride;
				int chunks = r.ReadInt32();
				for (int c = 0; c < chunks; c++)
				{
					int index = r.ReadInt32();
					byte[] bytes = r.ReadBytes(byteCount);
					if (bytes.Length != byteCount) throw new EndOfStreamException($"{path}: truncated at chunk {c}/{chunks}");
					NativeArray<EnvScanner.Voxel> voxels = new(voxelCount, Allocator.Persistent);
					voxels.Reinterpret<byte>(EnvScanner.Voxel.stride).CopyFrom(bytes);
					scan.Chunks[index] = voxels;
				}
			}
			catch
			{
				scan.Dispose();
				throw;
			}

			return scan;
		}

		public void Dispose()
		{
			foreach (NativeArray<EnvScanner.Voxel> voxels in Chunks.Values)
				if (voxels.IsCreated) voxels.Dispose();
			Chunks.Clear();
		}

		public override string ToString()
		{
			(int3 min, int3 max) = ChunkBounds;
			Pose4 frame = ScanFrame;
			return $"'{Label}' {Chunks.Count} chunks {(Chunks.Count > 0 ? $"{min}..{max}" : "")} frame {frame} head ({Head.position.x:F2}, {Head.position.y:F2}, {Head.position.z:F2}) saved {SavedAtUtc:u}";
		}

		private static void Write(BinaryWriter w, int3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
		private static void Write(BinaryWriter w, float3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }

		private static void Write(BinaryWriter w, Pose p)
		{
			Write(w, (float3)p.position);
			w.Write(p.rotation.x); w.Write(p.rotation.y); w.Write(p.rotation.z); w.Write(p.rotation.w);
		}

		private static int3 ReadInt3(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
		private static float3 ReadFloat3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

		private static Pose ReadPose(BinaryReader r) =>
			new(ReadFloat3(r), new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()));
	}
}
