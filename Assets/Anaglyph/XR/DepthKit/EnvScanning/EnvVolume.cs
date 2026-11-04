using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// The chunked TSDF volume on the GPU: the chunk table (dense over the playspace),
	/// the chunk data atlas (a 3D texture of reserved chunks), the reservation counter
	/// and the per-chunk change sums. <see cref="EnvScanner"/> owns the kernels
	/// </summary>
	public sealed class EnvVolume : IDisposable
	{
		private readonly EnvScanner scanner;

		public ComputeBuffer ReservedChunkCounter { get; private set; }
		public ComputeBuffer ChunkTable { get; private set; }
		public ComputeBuffer ChunkChangeSums { get; private set; }
		public ComputeBuffer VisibleChunks { get; private set; }
		public RenderTexture ChunkData { get; private set; }

		/// <summary>Chunks per axis in the data atlas: powers of two, rounded up from what was asked for.</summary>
		public int3 DataDims { get; }
		public int MaxNumChunks { get; }

		public bool IsCreated => ChunkData != null;

		internal EnvVolume(EnvScanner scanner, int3 dataDims, int voxPerChunkDim, int chunkTableLength, int maxNumVisibleChunks, string name)
		{
			this.scanner = scanner;
			DataDims = dataDims = math.ceilpow2(math.max(dataDims, 1));
			MaxNumChunks = dataDims.x * dataDims.y * dataDims.z;

			ReservedChunkCounter = new ComputeBuffer(1, sizeof(int));
			ChunkTable = new ComputeBuffer(chunkTableLength, sizeof(int));
			ChunkChangeSums = new ComputeBuffer(chunkTableLength, sizeof(uint));
			VisibleChunks = new ComputeBuffer(maxNumVisibleChunks, sizeof(int), ComputeBufferType.Append);

			// R -> TSDF value, G -> integration count (as an unsigned byte)
			ChunkData = new RenderTexture(new RenderTextureDescriptor
			{
				width = dataDims.x * voxPerChunkDim,
				height = dataDims.y * voxPerChunkDim,
				volumeDepth = dataDims.z * voxPerChunkDim,
				msaaSamples = 1,
				useMipMap = false,
				graphicsFormat = GraphicsFormat.R8G8_SNorm,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true
			}) { name = name };
		}

		/// <summary>Empties the table, counters and every voxel.</summary>
		public void Clear()
		{
			if (IsCreated) scanner.ClearVolume(this);
		}

		public void Dispose()
		{
			ReservedChunkCounter?.Release();
			ChunkTable?.Release();
			ChunkChangeSums?.Release();
			VisibleChunks?.Release();
			if (ChunkData) ChunkData.Release();
			ReservedChunkCounter = null;
			ChunkTable = null;
			ChunkChangeSums = null;
			VisibleChunks = null;
			ChunkData = null;
		}
	}
}
