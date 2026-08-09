using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Utilities.XR;
using Action = System.Action;

namespace Anaglyph.DepthKit.EnvScanning
{
	public class EnvScanner : MonoBehaviour
	{
		public static EnvScanner Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private float updateFrequency = 15.0f;
		[SerializeField] private float2 updateRange = new(1.0f, 6.0f);
		[SerializeField] private float minDot = 0.5f;
		private float minDist => updateRange.x;
		private float maxDist => updateRange.y;

		[SerializeField] private float voxSize = 0.1f;
		[SerializeField] private float distanceTruncationBand = 0.2f;
		[SerializeField] private int voxPerChunkDim = 32;
		private float chunkWorldSizeDim;

		// world-space offset applied to the whole chunk grid.
		// shifts where chunk boundaries land in the world so they can be kept
		// away from features like the floor (y = 0)
		[SerializeField] private float3 originOffset = float3.zero;

		// cylinders encompassing player heads and extending down.
		// depth samples inside them are ignored so players
		// aren't integrated into the scan. radius is padded
		// to absorb network interpolation lag on remote heads
		[SerializeField] private float playerMaskRadius = 1.2f;
		[SerializeField] private float playerMaskAbove = 0.5f;
		[SerializeField] private float playerMaskBelow = 2.0f;

		public static List<Transform> PlayerHeads { get; } = new();
		private const int MaxPlayerHeads = 64;
		private readonly Vector4[] playerHeads = new Vector4[MaxPlayerHeads];

		[SerializeField] private int3 chunkTableDims = new(64, 16, 64);

		private int chunkTableLength;

		private int maxNumChunks;
		[SerializeField] private int3 chunkDataDims = new(8, 8, 8);
		[SerializeField] private int maxNumVisibleChunks = 256;

		public float VoxSize => voxSize;
		public float DistanceTruncationBand => distanceTruncationBand;
		public int VoxPerChunkDim => voxPerChunkDim;
		public float3 OriginOffset => originOffset;
		public int3 ChunkTableDims => chunkTableDims;
		public int ChunkTableLength => chunkTableLength;
		public int MaxNumChunks => maxNumChunks;
		public float ChunkWorldSizeDim => chunkWorldSizeDim;

		private ComputeBuffer reservedChunkCounter;
		private ComputeBuffer chunkTable;
		private ComputeBuffer chunkChangeSums;
		private ComputeBuffer visibleChunks;
		private ComputeBuffer visibleChangeSumsReadback;
		private ComputeBuffer integrateDispatchDims;

		private RenderTexture chunkData;
		public RenderTexture ChunkData => chunkData;
		public int3 ChunkDataDims => chunkDataDims;

		private ComputeKernel clearKernel;
		private ComputeKernel markKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel visChangeSumsReadbackKernel;
		private ComputeKernel dataReadbackKernel;

		private CancellationTokenSource updateLoopTknSrc;

		public event Action Updated = delegate { };
		public event Action Cleared = delegate { };

		public struct ChunkReadbackBuffer : IDisposable
		{
			public ComputeBuffer gpuBuffer { get; private set; }
			public NativeArray<Voxel> data { get; private set; }
			public readonly int voxPerChunkDim;

			public ChunkReadbackBuffer(ComputeBuffer gpuBuffer, NativeArray<Voxel> data, int voxPerChunkDim)
			{
				this.gpuBuffer = gpuBuffer;
				this.data = data;
				this.voxPerChunkDim = voxPerChunkDim;
			}

			public void Dispose()
			{
				gpuBuffer?.Dispose();
				data.Dispose();
			}
		}

		/// <summary>
		/// Readback result from environment scanner.
		/// `visibleChunks` and `changeSums` MUST be used FRAME OF READBACK!
		/// </summary>
		public struct VisibleChunksReadbackResult
		{
			public int count { get; private set; }
			public NativeArray<int> visibleChunks { get; private set; }
			public NativeArray<uint> changeSums { get; private set; }
			public bool valid { get; private set; }

			public VisibleChunksReadbackResult(int count, NativeArray<int> visibleChunks, NativeArray<uint> changeSums)
			{
				this.count = count;
				this.visibleChunks = visibleChunks;
				this.changeSums = changeSums;
				valid = true;
			}
		}

		private void Awake()
		{
			Setup();
			Instance = this;
		}

		private void OnValidate()
		{
			updateFrequency = Mathf.Max(updateFrequency, 1.0f);
		}

		private void Setup()
		{
			// helpful values
			chunkWorldSizeDim = voxSize * (voxPerChunkDim - 2);
			int3 cdd = chunkDataDims;
			maxNumChunks = cdd.x * cdd.y * cdd.z;

			// buffers
			reservedChunkCounter = new ComputeBuffer(1, sizeof(int));
			visibleChunks = new ComputeBuffer(maxNumVisibleChunks, sizeof(int), ComputeBufferType.Append);
			visibleChangeSumsReadback = new ComputeBuffer(maxNumVisibleChunks, sizeof(uint));
			int3 ctd = chunkTableDims;
			chunkTableLength = ctd.x * ctd.y * ctd.z;
			chunkTable = new ComputeBuffer(chunkTableLength, sizeof(int));
			chunkChangeSums = new ComputeBuffer(chunkTableLength, sizeof(uint));

			// chunkData should be a R8G8_SNorm.
			// R -> TSDF value
			// G -> number of times this voxel has been integrated (up to 255)...
			// en/decoded as an unsigned byte and used for scan filtering over time
			RenderTextureDescriptor dataDesc = new()
			{
				width = chunkDataDims.x * voxPerChunkDim,
				height = chunkDataDims.y * voxPerChunkDim,
				volumeDepth = chunkDataDims.z * voxPerChunkDim,
				msaaSamples = 1,
				useMipMap = false,
				graphicsFormat = GraphicsFormat.R8G8_SNorm,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true
			};
			chunkData = new RenderTexture(dataDesc);

			// uniform values
			compute.SetFloat(nameof(minDist), minDist);
			compute.SetFloat(nameof(maxDist), maxDist);
			compute.SetFloat(nameof(minDot), minDot);
			compute.SetFloat(nameof(voxSize), voxSize);
			compute.SetFloat(nameof(chunkWorldSizeDim), chunkWorldSizeDim);
			compute.SetVector(nameof(originOffset), new Vector4(originOffset.x, originOffset.y, originOffset.z, 0f));
			compute.SetFloat(nameof(distanceTruncationBand), distanceTruncationBand);
			compute.SetInt(nameof(voxPerChunkDim), voxPerChunkDim);
			compute.SetInts(nameof(chunkTableDims), ctd.x, ctd.y, ctd.z);
			compute.SetInt(nameof(chunkTableLength), chunkTableLength);
			compute.SetInts(nameof(chunkDataDims), cdd.x, cdd.y, cdd.z);
			compute.SetInt(nameof(maxNumVisibleChunks), maxNumVisibleChunks);
			compute.SetInt(nameof(maxNumChunks), maxNumChunks);
			compute.SetFloat(nameof(playerMaskRadius), playerMaskRadius);
			compute.SetFloat(nameof(playerMaskAbove), playerMaskAbove);
			compute.SetFloat(nameof(playerMaskBelow), playerMaskBelow);

			// mark kernel
			markKernel = new ComputeKernel(compute, "Mark");
			markKernel.Bind(nameof(reservedChunkCounter), reservedChunkCounter);
			markKernel.Bind(nameof(chunkTable), chunkTable);
			markKernel.Bind(nameof(chunkData), chunkData);
			markKernel.Bind("visibleChunksAppend", visibleChunks);

			// integrate kernel
			integrateKernel = new ComputeKernel(compute, "Integrate");
			integrateKernel.Bind(nameof(chunkTable), chunkTable);
			integrateKernel.Bind(nameof(chunkData), chunkData);
			integrateKernel.Bind(nameof(chunkChangeSums), chunkChangeSums);
			integrateKernel.Bind(nameof(visibleChunks), visibleChunks);

			integrateDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			int groupsPerChunkDim = voxPerChunkDim / integrateKernel.groupSize.x;
			compute.SetInt(nameof(groupsPerChunkDim), groupsPerChunkDim);
			int groupsPerChunk = groupsPerChunkDim * groupsPerChunkDim * groupsPerChunkDim;
			integrateDispatchDims.SetData(new uint[] { 0, (uint)groupsPerChunk, 1 });

			// clear
			clearKernel = new ComputeKernel(compute, "Clear");
			clearKernel.Bind(nameof(chunkData), chunkData);

			// data readback
			dataReadbackKernel = new ComputeKernel(compute, "ChunkDataReadback");
			dataReadbackKernel.Bind(nameof(chunkTable), chunkTable);
			dataReadbackKernel.Bind(nameof(chunkData), chunkData);

			// visible chunk change sums readback
			visChangeSumsReadbackKernel = new ComputeKernel(compute, "VisibleChunkChangeSumsReadback");
			visChangeSumsReadbackKernel.Bind(nameof(visibleChunks), visibleChunks);
			visChangeSumsReadbackKernel.Bind(nameof(chunkChangeSums), chunkChangeSums);
			visChangeSumsReadbackKernel.Bind(nameof(visibleChangeSumsReadback), visibleChangeSumsReadback);

			Clear();
		}

		private static readonly int[] EmptyCounterArray = new int[1];
		private static int[] EmptyChunkTableArray;
		private static int[] EmptyVisibleChunksArray;

		public void Clear()
		{
			if (EmptyChunkTableArray == null || EmptyChunkTableArray.Length != chunkTableLength)
				EmptyChunkTableArray = new int[chunkTableLength];

			if (EmptyVisibleChunksArray == null || EmptyVisibleChunksArray.Length != maxNumVisibleChunks)
				EmptyVisibleChunksArray = new int[maxNumVisibleChunks];

			reservedChunkCounter.SetData(EmptyCounterArray);
			chunkTable.SetData(EmptyChunkTableArray);
			chunkChangeSums.SetData(EmptyChunkTableArray);
			visibleChunks.SetData(EmptyVisibleChunksArray);

			clearKernel.DispatchFit(chunkData);

			Cleared.Invoke();
		}

		private void Start()
		{
			UpdateLoop();
		}

		private void OnEnable()
		{
			if (didStart)
				UpdateLoop();
		}

		private void OnDisable()
		{
			visibleChunks.SetData(EmptyVisibleChunksArray);
			updateLoopTknSrc?.Cancel();
		}

		private void OnDestroy()
		{
			reservedChunkCounter?.Release();
			chunkTable?.Release();
			chunkChangeSums?.Release();
			visibleChunks?.Release();
			integrateDispatchDims?.Release();
			chunkData?.Release();
			visibleChangeSumsReadback?.Release();
		}

		private async void UpdateLoop()
		{
			updateLoopTknSrc?.Cancel();
			updateLoopTknSrc = new CancellationTokenSource();
			CancellationToken ctkn = updateLoopTknSrc.Token;

			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.WaitForSecondsAsync(1 / updateFrequency, ctkn);
					ctkn.ThrowIfCancellationRequested();

					Scan();

					Updated.Invoke();
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		public void Scan()
		{
			DepthKitDriver dkd = DepthKitDriver.Instance;

			if (!DepthKitDriver.DepthAvailable) return;

			compute.SetMatrixArray(DepthKitDriver.viewID, dkd.View);
			compute.SetMatrixArray(DepthKitDriver.projID, dkd.Proj);

			compute.SetMatrixArray(DepthKitDriver.viewInvID, dkd.ViewInv);
			compute.SetMatrixArray(DepthKitDriver.projInvID, dkd.ProjInv);

			// upload player head positions for masking
			int numPlayerHeads = 0;
			for (int i = 0; i < PlayerHeads.Count && numPlayerHeads < MaxPlayerHeads; i++)
			{
				Transform head = PlayerHeads[i];
				if (head != null)
					playerHeads[numPlayerHeads++] = head.position;
			}

			compute.SetInt(nameof(numPlayerHeads), numPlayerHeads);
			compute.SetVectorArray(nameof(playerHeads), playerHeads);

			// reset visible chunks counter
			visibleChunks.SetCounterValue(0);

			// mark active chunks
			markKernel.Bind(DepthKitDriver.depthTexID, dkd.DepthTex);
			markKernel.DispatchFit(dkd.DepthTex, 1);

			// integrate into active chunks
			integrateKernel.Bind(DepthKitDriver.depthTexID, dkd.DepthTex);
			ComputeBuffer.CopyCount(visibleChunks, integrateDispatchDims, 0);
			integrateKernel.DispatchIndirect(integrateDispatchDims);
		}

		private static readonly int readbackBufferID = Shader.PropertyToID("readbackBuffer");
		private static readonly int readbackChunkIndexID = Shader.PropertyToID("readbackChunkIndex");

		public async Awaitable<VisibleChunksReadbackResult> ReadbackVisibleChunks()
		{
			visChangeSumsReadbackKernel.DispatchFit(maxNumVisibleChunks, 1, 1);

			Awaitable<AsyncGPUReadbackRequest> dataReqWait = AsyncGPUReadback.RequestAsync(visibleChunks);
			Awaitable<AsyncGPUReadbackRequest> countReqWait = AsyncGPUReadback.RequestAsync(integrateDispatchDims);
			Awaitable<AsyncGPUReadbackRequest>
				changeSumsWait = AsyncGPUReadback.RequestAsync(visibleChangeSumsReadback);

			AsyncGPUReadbackRequest dataReq = await dataReqWait;
			AsyncGPUReadbackRequest countReq = await countReqWait;
			AsyncGPUReadbackRequest changeSumsReq = await changeSumsWait;

			if (dataReq.hasError || countReq.hasError || changeSumsReq.hasError)
			{
				LogDebug("Visible chunks readback failed!", LogType.Warning);
				return new VisibleChunksReadbackResult();
			}

			int count = countReq.GetData<int>()[0];
			count = math.min(count, maxNumVisibleChunks);

			NativeArray<int> visibleChunkData = dataReq.GetData<int>();
			NativeArray<uint> changesSums = changeSumsReq.GetData<uint>();

			return new VisibleChunksReadbackResult(count, visibleChunkData, changesSums);
		}

		public ChunkReadbackBuffer CreateChunkReadbackBuffer()
		{
			int vpcd = voxPerChunkDim;

			ComputeBuffer computeBuffer = new(vpcd / 4 * vpcd * vpcd, sizeof(uint));
			NativeArray<Voxel> data = new(vpcd * vpcd * vpcd, Allocator.Persistent);

			return new ChunkReadbackBuffer(computeBuffer, data, vpcd);
		}

		public async Awaitable<bool> ReadbackChunkInto(int chunkIndex, ChunkReadbackBuffer readbackBuffer)
		{
			if (readbackBuffer.voxPerChunkDim != voxPerChunkDim)
				throw new Exception("Readback chunk dimensions do not match scanner chunk dimensions");

			if (chunkIndex < 0 || chunkIndex >= chunkTableLength)
			{
				LogDebug("Readback chunk index out of range!", LogType.Warning);
				return false;
			}

			compute.SetInt(readbackChunkIndexID, chunkIndex);
			dataReadbackKernel.Bind(readbackBufferID, readbackBuffer.gpuBuffer);

			// each thread covers FOUR voxels on X axis for byte packing
			int vpcd = voxPerChunkDim;
			dataReadbackKernel.DispatchFit(vpcd / 4, vpcd, vpcd);

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(readbackBuffer.gpuBuffer);

			if (req.hasError)
			{
				LogDebug("Readback chunk failed!", LogType.Warning);
				return false;
			}

			req.GetData<byte>().Reinterpret<Voxel>(1).CopyTo(readbackBuffer.data);
			return true;
		}

		public int3 ChunkIndexToChunkCoord(int chunkIndex)
		{
			int3 coord;
			coord.x = chunkIndex % chunkTableDims.x;
			coord.y = chunkIndex / chunkTableDims.x % chunkTableDims.y;
			coord.z = chunkIndex / (chunkTableDims.x * chunkTableDims.y);
			return coord;
		}

		public float3 ChunkCoordToCornerWorldPos(int3 chunkCoord)
		{
			int3 chunkCoordUncentered = chunkCoord - chunkTableDims / 2;
			// subtract two to account for surrounding 1-vox apron
			// this makes the corner voxel overlap with another chunk's 'top' corner

			return (float3)chunkCoordUncentered * chunkWorldSizeDim - new float3(voxSize, voxSize, voxSize) + originOffset;
		}

		public int3 WorldPosToChunkCoord(float3 world)
		{
			// quantize to chunk size.
			// subtract two from voxPerChunkDim to account for 1-vox apron
			int3 quantized = (int3)math.floor((world - originOffset) / chunkWorldSizeDim);

			// center so 0,0,0 is at corner
			int3 chunkCoord = quantized + chunkTableDims / 2;

			return chunkCoord;
		}

		public int ChunkCoordToChunkIndex(int3 coord)
		{
			if (math.any(coord < 0) || math.any(coord >= (int3)chunkTableDims))
				return -1;

			return
				coord.x +
				coord.y * chunkTableDims.x +
				coord.z * chunkTableDims.x * chunkTableDims.y;
		}

		public void DrawChunkBounds(int3 coord, Color color)
		{
			float h = chunkWorldSizeDim / 2f;

			float3 corner = ChunkCoordToCornerWorldPos(coord);
			float3 center = corner + h;

			XRGizmos.DrawWireCube(center, Quaternion.identity, (float3)chunkWorldSizeDim, color);
		}

		public void DrawChunkBounds(int chunkIndex, Color color)
		{
			DrawChunkBounds(ChunkIndexToChunkCoord(chunkIndex), color);
		}

		private static void LogDebug(string str, LogType logType = LogType.Log)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.unityLogger.Log($"[{nameof(EnvScanner)}] {str}", logType);
#endif
		}

		[StructLayout(LayoutKind.Sequential)]
		public readonly struct Voxel
		{
			public readonly sbyte value;
			// public readonly byte count;

			public const int stride = 1; //2;

			public float CalcDistNorm()
			{
				return value / (float)sbyte.MaxValue;
			}

			public Voxel(sbyte value)
			{
				this.value = value;
				// count = 0;
			}
		}
	}
}