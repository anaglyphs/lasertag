using System;
using System.Threading;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvScanner2 : MonoBehaviour
	{
		public static EnvScanner2 Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private float updateFrequency = 15.0f;

		[SerializeField] private float voxSize = 0.1f;
		private float voxSizeHalf;
		[SerializeField] private float distanceTruncationBand = 0.2f;
		[SerializeField] private int voxPerChunkDim = 32;
		private Vector3 chunkSize;
		private Vector3 chunkSizeHalf;
		[SerializeField] private int3 chunkAreaDims = new(64, 16, 64);
		private int maxNumChunks;
		[SerializeField] private int3 chunkDataDims = new(8, 8, 8);
		[SerializeField] private int maxNumVisibleChunks = 256;

		public float VoxSize => voxSize;
		public float DistanceTruncationBand => distanceTruncationBand;
		public int VoxPerChunkDim => voxPerChunkDim;
		public int3 ChunkAreaDims => chunkAreaDims;
		public int MaxNumChunks => maxNumChunks;
		public Vector3 ChunkSize => chunkSize;
		public Vector3 ChunkSizeHalf => chunkSizeHalf;

		private ComputeBuffer reservedChunkCounter;
		private ComputeBuffer chunkTable;
		private ComputeBuffer visibleChunks;
		private ComputeBuffer visibleChunksCount;

		private ComputeBuffer integrateDispatchDims;

		public ComputeBuffer ReservedChunkCounter => reservedChunkCounter;
		public ComputeBuffer ChunkTable => chunkTable;
		public ComputeBuffer VisibleChunks => visibleChunks;

		private RenderTexture chunkData;

		private ComputeKernel clearKernel;
		private ComputeKernel markKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel readbackKernel;

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
			Instance = this;
			Setup();
		}

		private void Setup()
		{
			// helpful values
			voxSizeHalf = voxSize * 0.5f;

			float csd = voxSize * voxPerChunkDim;
			chunkSize = new float3(csd, csd, csd);
			chunkSizeHalf = ChunkSize / 2f;

			int3 cad = chunkAreaDims;
			maxNumChunks = cad.x * cad.y * cad.z;

			// buffers
			reservedChunkCounter = new ComputeBuffer(1, sizeof(int));
			visibleChunks = new ComputeBuffer(maxNumVisibleChunks, sizeof(int), ComputeBufferType.Append);
			compute.SetInt(nameof(maxNumVisibleChunks), maxNumVisibleChunks);

			int chunkTableLength = cad.x * cad.y * cad.z;
			chunkTable = new ComputeBuffer(chunkTableLength, sizeof(int));

			RenderTextureDescriptor dataDesc = new()
			{
				width = chunkDataDims.x * voxPerChunkDim,
				height = chunkDataDims.y * voxPerChunkDim,
				volumeDepth = chunkDataDims.z * voxPerChunkDim,
				msaaSamples = 1,
				useMipMap = false,
				graphicsFormat = GraphicsFormat.R8_SNorm,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true
			};
			chunkData = new RenderTexture(dataDesc);

			// uniform values
			compute.SetFloat(nameof(voxSize), voxSize);
			compute.SetFloat(nameof(voxSizeHalf), voxSizeHalf);
			compute.SetVector(nameof(chunkSize), (Vector3)chunkSize);
			compute.SetVector(nameof(chunkSizeHalf), (Vector3)chunkSizeHalf);
			compute.SetFloat(nameof(distanceTruncationBand), distanceTruncationBand);
			compute.SetInt(nameof(voxPerChunkDim), voxPerChunkDim);
			compute.SetInts(nameof(chunkAreaDims), chunkAreaDims.x, chunkAreaDims.y, chunkAreaDims.z);
			compute.SetInt(nameof(chunkTableLength), chunkTableLength);
			compute.SetInts(nameof(chunkDataDims), chunkDataDims.x, chunkDataDims.y, chunkDataDims.z);

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
			clearKernel.DispatchFit(chunkData);

			// readback
			readbackKernel = new ComputeKernel(compute, "ChunkReadback");
			readbackKernel.Bind(nameof(chunkTable), chunkTable);
			readbackKernel.Bind(nameof(chunkData), chunkData);
		}

		private void Start()
		{
			UpdateLoop();
		}

		private void OnEnable()
		{
			if (!didStart)
				UpdateLoop();
		}

		private async void UpdateLoop()
		{
			try
			{
				CancellationToken ctkn = destroyCancellationToken;

				while (enabled)
				{
					await Awaitable.WaitForSecondsAsync(1 / updateFrequency, ctkn);

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
				Debug.LogWarning("[EnvScanner2] Visible chunks readback failed!");
				return new VisibleChunksReadbackResult();
			}

			int count = countReq.GetData<int>()[0];
			count = math.min(count, maxNumVisibleChunks);

			NativeArray<int> visibleChunkData = dataReq.GetData<int>();

			return new VisibleChunksReadbackResult(count, visibleChunkData);
		}

		public async Awaitable<ChunkDataReadbackResult> ReadbackChunk(int chunkIndex)
		{
			if (chunkIndex > maxNumChunks)
			{
				Debug.LogWarning("[EnvScanner2] Readback chunk index out of range!");
				return new ChunkDataReadbackResult();
			}

			int d = voxPerChunkDim;
			// uint = 4 sbytes packed
			ComputeBuffer readbackBuffer = new(d / 4 * d * d, sizeof(uint));

			compute.SetInt(readbackChunkIndexID, chunkIndex);
			readbackKernel.Bind(readbackBufferID, readbackBuffer);

			// each thread covers FOUR voxels on X axis for byte packing
			readbackKernel.DispatchFit(d / 4, d, d);

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(readbackBuffer);

			if (req.hasError)
			{
				Debug.LogWarning("[EnvScanner2] Readback chunk failed!");
				return new ChunkDataReadbackResult();
			}

			return new ChunkDataReadbackResult(req.GetData<sbyte>());
		}

		public int3 ChunkIndexToChunkCoord(int chunkIndex)
		{
			int3 coord;
			coord.x = chunkIndex % chunkAreaDims.x;
			coord.y = chunkIndex / chunkAreaDims.x % chunkAreaDims.y;
			coord.z = chunkIndex / (chunkAreaDims.x * chunkAreaDims.y);
			return coord;
		}

		public int3 WorldPosToChunkCoord(float3 world)
		{
			// quantize to chunk size.
			// subtract two from voxPerChunkDim to account for 1-vox apron
			float chunkSize = (voxPerChunkDim - 2) * voxSize;
			int3 quantized = (int3)math.floor(world / chunkSize);

			// center so 0,0,0 is at corner
			int3 tableDimHalf = chunkAreaDims / 2;
			int3 chunkCoord = quantized + tableDimHalf;

			return chunkCoord;
		}

		public float3 ChunkCoordToCornerWorldPos(int3 chunkCoord)
		{
			int3 tableDimHalf = chunkAreaDims / 2;
			int3 chunkCoordUncentered = chunkCoord - tableDimHalf;
			// subtract one to account for 1-vox apron
			// this makes the corner voxel overlap with another chunk's 'top' corner
			float chunkSizeDim = (voxPerChunkDim - 1) * voxSize;

			return (float3)chunkCoordUncentered * chunkSizeDim;
		}

		private void OnDestroy()
		{
			reservedChunkCounter?.Release();
			chunkTable?.Release();
			visibleChunks?.Release();
			integrateDispatchDims?.Release();
			chunkData?.Release();
		}
	}
}