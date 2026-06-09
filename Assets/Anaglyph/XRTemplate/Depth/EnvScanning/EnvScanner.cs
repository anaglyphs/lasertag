using System;
using System.Threading;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

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

		[SerializeField] private int3 chunkTableDims = new(64, 16, 64);

		private int chunkTableLength;

		private int maxNumChunks;
		[SerializeField] private int3 chunkDataDims = new(8, 8, 8);
		[SerializeField] private int maxNumVisibleChunks = 256;

		public float VoxSize => voxSize;
		public float DistanceTruncationBand => distanceTruncationBand;
		public int VoxPerChunkDim => voxPerChunkDim;
		public int3 ChunkTableDims => chunkTableDims;
		public int ChunkTableLength => chunkTableLength;
		public int MaxNumChunks => maxNumChunks;
		public float ChunkWorldSizeDim => chunkWorldSizeDim;

		private ComputeBuffer reservedChunkCounter;
		private ComputeBuffer chunkTable;
		private ComputeBuffer visibleChunks;
		private ComputeBuffer integrateDispatchDims;

		public ComputeBuffer ReservedChunkCounter => reservedChunkCounter;
		public ComputeBuffer ChunkTable => chunkTable;
		public ComputeBuffer VisibleChunks => visibleChunks;

		private RenderTexture chunkData;
		public RenderTexture ChunkData => chunkData;
		public int3 ChunkDataDims => chunkDataDims;

		private ComputeKernel clearKernel;
		private ComputeKernel markKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel readbackKernel;

		private CancellationTokenSource updateLoopTknSrc;

		public event Action Updated = delegate { };

		public struct VisibleChunksReadbackResult
		{
			public int count { get; private set; }
			public NativeArray<int> visibleChunks { get; private set; }
			public bool valid { get; private set; }

			public VisibleChunksReadbackResult(int count, NativeArray<int> visibleChunks)
			{
				this.count = count;
				this.visibleChunks = visibleChunks;
				valid = true;
			}
		}

		public struct ChunkDataReadbackResult
		{
			public NativeArray<sbyte> data { get; private set; }
			public bool valid { get; private set; }

			public ChunkDataReadbackResult(NativeArray<sbyte> data)
			{
				this.data = data;
				valid = true;
			}
		}

		private void Awake()
		{
			Setup();
			Instance = this;
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
			int3 ctd = chunkTableDims;
			chunkTableLength = ctd.x * ctd.y * ctd.z;
			chunkTable = new ComputeBuffer(chunkTableLength, sizeof(int));

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
			compute.SetFloat(nameof(distanceTruncationBand), distanceTruncationBand);
			compute.SetInt(nameof(voxPerChunkDim), voxPerChunkDim);
			compute.SetInts(nameof(chunkTableDims), ctd.x, ctd.y, ctd.z);
			compute.SetInt(nameof(chunkTableLength), chunkTableLength);
			compute.SetInts(nameof(chunkDataDims), cdd.x, cdd.y, cdd.z);
			compute.SetInt(nameof(maxNumVisibleChunks), maxNumVisibleChunks);
			compute.SetInt(nameof(maxNumChunks), maxNumChunks);

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
			integrateKernel.Bind(nameof(visibleChunks), visibleChunks);

			integrateDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			int groupsPerChunkDim = voxPerChunkDim / integrateKernel.groupSize.x;
			compute.SetInt(nameof(groupsPerChunkDim), groupsPerChunkDim);
			int groupsPerChunk = groupsPerChunkDim * groupsPerChunkDim * groupsPerChunkDim;
			integrateDispatchDims.SetData(new uint[] { 0, (uint)groupsPerChunk, 1 });

			// clear
			clearKernel = new ComputeKernel(compute, "Clear");
			clearKernel.Bind(nameof(chunkData), chunkData);

			// readback
			readbackKernel = new ComputeKernel(compute, "ChunkReadback");
			readbackKernel.Bind(nameof(chunkTable), chunkTable);
			readbackKernel.Bind(nameof(chunkData), chunkData);

			Clear();
		}

		public void Clear()
		{
			reservedChunkCounter.SetData(new int[1]);
			chunkTable.SetData(new int[chunkTableLength]);
			visibleChunks.SetData(new int[maxNumVisibleChunks]);

			clearKernel.DispatchFit(chunkData);
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
			updateLoopTknSrc?.Cancel();
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
			Awaitable<AsyncGPUReadbackRequest> dataReqWait = AsyncGPUReadback.RequestAsync(visibleChunks);
			Awaitable<AsyncGPUReadbackRequest> countReqWait = AsyncGPUReadback.RequestAsync(integrateDispatchDims);

			AsyncGPUReadbackRequest dataReq = await dataReqWait;
			AsyncGPUReadbackRequest countReq = await countReqWait;

			if (dataReq.hasError || countReq.hasError)
			{
				LogDebug("Visible chunks readback failed!", LogType.Warning);
				return new VisibleChunksReadbackResult();
			}

			int count = countReq.GetData<int>()[0];
			count = math.min(count, maxNumVisibleChunks);

			NativeArray<int> visibleChunkData = dataReq.GetData<int>();

			return new VisibleChunksReadbackResult(count, visibleChunkData);
		}

		public ComputeBuffer CreateChunkReadbackBuffer()
		{
			int vpcd = voxPerChunkDim;
			return new ComputeBuffer(vpcd / 4 * vpcd * vpcd, sizeof(uint));
		}

		public async Awaitable<ChunkDataReadbackResult> ReadbackChunk(int chunkIndex, ComputeBuffer readbackBuffer)
		{
			if (chunkIndex < 0 || chunkIndex >= chunkTableLength)
			{
				LogDebug("Readback chunk index out of range!", LogType.Warning);
				return new ChunkDataReadbackResult();
			}

			compute.SetInt(readbackChunkIndexID, chunkIndex);
			readbackKernel.Bind(readbackBufferID, readbackBuffer);

			// each thread covers FOUR voxels on X axis for byte packing
			int vpcd = voxPerChunkDim;
			readbackKernel.DispatchFit(vpcd / 4, vpcd, vpcd);

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(readbackBuffer);

			if (req.hasError)
			{
				LogDebug("Readback chunk failed!", LogType.Warning);
				return new ChunkDataReadbackResult();
			}

			return new ChunkDataReadbackResult(req.GetData<sbyte>());
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

			return (float3)chunkCoordUncentered * chunkWorldSizeDim - new float3(voxSize, voxSize, voxSize);
		}

		private void OnDestroy()
		{
			reservedChunkCounter?.Release();
			chunkTable?.Release();
			visibleChunks?.Release();
			integrateDispatchDims?.Release();
			chunkData?.Release();
		}

		private static void LogDebug(string str, LogType logType = LogType.Log)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			Debug.unityLogger.Log($"[{nameof(EnvScanner)}] {str}", logType);
#endif
		}
	}
}