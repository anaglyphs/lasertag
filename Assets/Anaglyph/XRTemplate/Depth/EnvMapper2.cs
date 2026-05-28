using System;
using System.Threading;
using Anaglyph.XRTemplate.DepthKit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvMapper2 : MonoBehaviour
	{
		public static EnvMapper2 Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private float updateFrequency = 15.0f;

		[SerializeField] private int voxPerChunkDim = 32;
		[SerializeField] private Vector3Int chunkAreaDims = new(64, 16, 64);
		[SerializeField] private Vector3Int chunkDataDims = new(8, 8, 8);

		private ComputeBuffer reservedChunkCounter;
		private ComputeBuffer chunkTable;
		private ComputeBuffer visibleChunks;

		private ComputeBuffer integrateDispatchDims;

		[SerializeField] private RenderTexture chunkData;

		private ComputeKernel clearKernel;
		private ComputeKernel markKernel;
		private ComputeKernel integrateKernel;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;
		}

		private void Setup()
		{
			// buffers
			reservedChunkCounter = new ComputeBuffer(1, sizeof(int));
			visibleChunks = new ComputeBuffer(512, sizeof(int), ComputeBufferType.Append);

			int chunkTableLength = chunkAreaDims.x * chunkAreaDims.y * chunkAreaDims.z;
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
			compute.SetInt(nameof(voxPerChunkDim), voxPerChunkDim);
			compute.SetInts(nameof(chunkAreaDims), chunkAreaDims.x, chunkAreaDims.y, chunkAreaDims.z);
			compute.SetInt(nameof(chunkTableLength), chunkTableLength);
			compute.SetInts(nameof(chunkDataDims), chunkDataDims.x, chunkDataDims.y, chunkDataDims.z);

			int maxNumChunks = chunkDataDims.x * chunkDataDims.y * chunkDataDims.z;
			compute.SetInt(nameof(maxNumChunks), maxNumChunks);

			// mark kernel
			markKernel = new ComputeKernel(compute, "Mark");
			markKernel.Set(nameof(reservedChunkCounter), reservedChunkCounter);
			markKernel.Set(nameof(chunkTable), chunkTable);
			markKernel.Set(nameof(chunkData), chunkData);
			markKernel.Set("visibleChunksAppend", visibleChunks);

			// integrate kernel
			integrateKernel = new ComputeKernel(compute, "Integrate");
			integrateKernel.Set(nameof(chunkTable), chunkTable);
			integrateKernel.Set(nameof(chunkData), chunkData);
			integrateKernel.Set("visibleChunks", visibleChunks);

			integrateDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			int groupsPerChunkDim = voxPerChunkDim / integrateKernel.groupSize.x;
			compute.SetInt(nameof(groupsPerChunkDim), groupsPerChunkDim);
			int groupsPerChunk = groupsPerChunkDim * groupsPerChunkDim * groupsPerChunkDim;
			integrateDispatchDims.SetData(new uint[] { 0, (uint)groupsPerChunk, 1 });

			// clear
			clearKernel = new ComputeKernel(compute, "Clear");
			clearKernel.Set(nameof(chunkData), chunkData);

			clearKernel.DispatchFit(chunkData);
		}

		private void Start()
		{
			Setup();
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
			catch (OperationCanceledException _)
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
			markKernel.Set(DepthKitDriver.depthTexID, dkd.DepthTex);
			markKernel.DispatchFit(dkd.DepthTex, 1);

			// integrate into active chunks
			integrateKernel.Set(DepthKitDriver.depthTexID, dkd.DepthTex);
			ComputeBuffer.CopyCount(visibleChunks, integrateDispatchDims, 0);
			integrateKernel.DispatchIndirect(integrateDispatchDims);
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