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

		[SerializeField] private int voxPerChunkRoot = 32;
		private static readonly int voxPerChunkRootID = Shader.PropertyToID("voxPerChunkRoot");

		[SerializeField] private Vector3Int chunkAreaDims = new(64, 16, 64);
		private static readonly int chunkAreaDimsID = Shader.PropertyToID(nameof(chunkAreaDims));
		private static readonly int chunkTableLengthID = Shader.PropertyToID("chunkTableLength");

		[SerializeField] private Vector3Int chunkDataDims = new(8, 8, 8);
		private static readonly int chunkDataDimsID = Shader.PropertyToID(nameof(chunkDataDims));
		private static readonly int maxNumChunksID = Shader.PropertyToID("maxNumChunks");

		private ComputeBuffer reservedChunkCounter;
		private static readonly int reservedChunkCounterID = Shader.PropertyToID(nameof(reservedChunkCounter));
		private ComputeBuffer chunkTable;
		private static readonly int chunkTableID = Shader.PropertyToID(nameof(chunkTable));
		private ComputeBuffer visibleChunks;
		private static readonly int visibleChunksID = Shader.PropertyToID(nameof(visibleChunks));
		private static readonly int visibleChunksAppendID = Shader.PropertyToID("visibleChunksAppend");

		private ComputeBuffer integrateDispatchDims;

		// private static readonly int integrateDispatchDimsID = Shader.PropertyToID(nameof(integrateDispatchDims));

		[SerializeField] private RenderTexture chunkData;
		private static readonly int chunkDataID = Shader.PropertyToID(nameof(chunkData));

		private ComputeKernel markKernel;
		private ComputeKernel integrateKernel;

		private static int viewID => DepthKitDriver.viewID;
		private static int projID => DepthKitDriver.projID;

		private static int viewInvID => DepthKitDriver.viewInvID;
		private static int projInvID => DepthKitDriver.projInvID;

		private static int depthTexID => DepthKitDriver.depthTexID;
		private static int normTexID => DepthKitDriver.normTexID;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;

			compute.SetInt(voxPerChunkRootID, voxPerChunkRoot);

			reservedChunkCounter = new ComputeBuffer(1, sizeof(int));
			visibleChunks = new ComputeBuffer(512, sizeof(int), ComputeBufferType.Append);

			int chunkTableLength = chunkAreaDims.x * chunkAreaDims.y * chunkAreaDims.z;
			compute.SetInts(chunkAreaDimsID, chunkAreaDims.x, chunkAreaDims.y, chunkAreaDims.z);
			compute.SetInt(chunkTableLengthID, chunkTableLength);
			chunkTable = new ComputeBuffer(chunkTableLength, sizeof(int));

			int maxNumChunks = chunkDataDims.x * chunkDataDims.y * chunkDataDims.z;
			compute.SetInts(chunkDataDimsID, chunkDataDims.x, chunkDataDims.y, chunkDataDims.z);
			compute.SetInt(maxNumChunksID, maxNumChunks);
			RenderTextureDescriptor dataDesc = new()
			{
				width = chunkDataDims.x * voxPerChunkRoot,
				height = chunkDataDims.y * voxPerChunkRoot,
				volumeDepth = chunkDataDims.z * voxPerChunkRoot,
				msaaSamples = 1,
				useMipMap = false,
				graphicsFormat = GraphicsFormat.R8_SNorm,
				dimension = TextureDimension.Tex3D,
				enableRandomWrite = true
			};
			chunkData = new RenderTexture(dataDesc);

			markKernel = new ComputeKernel(compute, "Mark");
			markKernel.Set(reservedChunkCounterID, reservedChunkCounter);
			markKernel.Set(chunkTableID, chunkTable);
			markKernel.Set(chunkDataID, chunkData);
			markKernel.Set(visibleChunksAppendID, visibleChunks);

			integrateKernel = new ComputeKernel(compute, "Integrate");
			integrateKernel.Set(chunkTableID, chunkTable);
			integrateKernel.Set(chunkDataID, chunkData);
			integrateKernel.Set(visibleChunksID, visibleChunks);

			integrateDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			uint groupsPerChunkRoot = (uint)(voxPerChunkRoot / integrateKernel.groupSize.x);
			uint groupsPerChunk = groupsPerChunkRoot * groupsPerChunkRoot * groupsPerChunkRoot;
			integrateDispatchDims.SetData(new uint[] { 0, groupsPerChunk, 1 });
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

					if (!DepthKitDriver.DepthAvailable) continue;

					ApplyScan();

					Updated.Invoke();
				}
			}
			catch (OperationCanceledException _)
			{
			}
		}

		public void ApplyScan()
		{
			DepthKitDriver dkd = DepthKitDriver.Instance;

			if (!DepthKitDriver.DepthAvailable) return;

			compute.SetMatrixArray(viewID, dkd.View);
			compute.SetMatrixArray(projID, dkd.Proj);

			compute.SetMatrixArray(viewInvID, dkd.ViewInv);
			compute.SetMatrixArray(projInvID, dkd.ProjInv);

			// reset visible chunks counter
			visibleChunks.SetCounterValue(0);

			markKernel.Set(depthTexID, dkd.DepthTex);
			markKernel.DispatchFit(dkd.DepthTex, 1);

			ComputeBuffer.CopyCount(visibleChunks, integrateDispatchDims, 0);

			integrateKernel.Set(depthTexID, dkd.DepthTex);

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