using System;
using System.Runtime.InteropServices;
using System.Threading;
using Anaglyph.GPU;
using Anaglyph.XR.DepthKit.EnvScanning.Encoding;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Utilities.XR;
using Action = System.Action;

namespace Anaglyph.XR.DepthKit.EnvScanning
{
	/// <summary>
	/// Fuses depth into a chunked TSDF volume on the GPU. The unit of integration is
	/// a <see cref="DepthFrameHeader">depth frame</see>: the live sensor is first
	/// captured into one (downsampled, filtered, quantized), and that frame is then
	/// integrated exactly as a frame received from another headset would be. Every
	/// headset that integrates the same frames holds the same volume.
	/// </summary>
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

		// pixels of the frame the live sensor is captured into. at 10cm voxels an
		// 80px-wide 90° frame already resolves ~7cm at 3m; width must be a multiple of 4
		[SerializeField] private int2 frameSize = new(80, 80);

		// bits per depth code on the wire: 8 bands visibly (21.6mm steps over 0.5–6m);
		// 12 is 1.3mm, under the volume's own resolution, for roughly twice the bytes
		[SerializeField, Range(8, 12)] private int frameCodeBits = 12;

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
		public int2 FrameSize => frameSize;

		// the live map: what the mesher, sync and snapshots see
		private EnvVolume live;
		private ComputeBuffer visibleChangeSumsReadback;
		private ComputeBuffer integrateDispatchDims;
		private ComputeBuffer integrateBlocks;
		private ComputeBuffer integrateBlockDispatchDims;

		public RenderTexture ChunkData => live?.ChunkData;
		public ComputeBuffer ChunkTable => live?.ChunkTable;

		/// <summary>Creates the GPU volume now if it does not exist yet.</summary>
		public void EnsureGpuResources()
		{
			if (!GpuResourcesCreated) CreateGpuResources();
		}

		[SerializeField] private Shader maskShader;
		private Material maskMat;
		private RenderTexture maskTex;
		private CommandBuffer maskCmd;

		// local capture only fills localPacked, always at frameSize, so its pending readback
		// never races a peer's frame. Peers' frames decode through the rest, which fit the
		// largest frame seen so far
		private ComputeBuffer localPacked;
		private ComputeBuffer remotePacked;
		private ComputeBuffer frameChange;
		private RenderTexture frameDepth;
		private RenderTexture frameNormal;
		private int2 remoteFrameCapacity;

		private ComputeBuffer uploadBuffer;

		private ComputeKernel clearKernel;
		private ComputeKernel captureKernel;
		private ComputeKernel decodeKernel;
		private ComputeKernel normalsKernel;
		private ComputeKernel markSensorKernel;
		private ComputeKernel cullBlocksSensorKernel;
		private ComputeKernel integrateSensorKernel;
		private ComputeKernel markKernel;
		private ComputeKernel cullBlocksKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel visChangeSumsReadbackKernel;
		private ComputeKernel dataReadbackKernel;
		private ComputeKernel reserveKernel;
		private ComputeKernel uploadKernel;

		private CancellationTokenSource updateLoopTknSrc;

		/// <summary>A frame (local or remote) was integrated; visible chunks are current for this Unity frame.</summary>
		public event Action Updated = delegate { };

		/// <summary>Embedding policy pauses depth integration without discarding the scan.</summary>
		public Func<bool> IntegrationGate { get; set; }

		public event Action Cleared = delegate { };

		/// <summary>
		/// The local sensor frame was integrated and its effect on the volume read back.
		/// The codes are only valid during the callback.
		/// </summary>
		public event Action<CapturedFrame> FrameCaptured = delegate { };

		/// <summary>A whole chunk was fused in through <see cref="UploadChunk"/>.</summary>
		public event Action<int> ChunkUploaded = delegate { };

		public readonly struct CapturedFrame
		{
			public readonly DepthFrameHeader header;
			public readonly NativeArray<byte> codes;

			/// <summary>Summed quantized voxel change this frame caused.</summary>
			public readonly uint change;

			public CapturedFrame(DepthFrameHeader header, NativeArray<byte> codes, uint change)
			{
				this.header = header;
				this.codes = codes;
				this.change = change;
			}
		}

		public struct ChunkReadbackBuffer : IDisposable
		{
			public ComputeBuffer gpuBuffer { get; private set; }
			public NativeArray<Voxel> data { get; private set; }
			public readonly int voxPerChunkDim;

			// shared by every copy of the struct, so a readback's sum reaches whoever holds one
			private NativeArray<uint> changeSum;

			/// <summary>
			/// The chunk's change odometer as it stood when <see cref="data"/> was read: the
			/// change the data already holds
			/// </summary>
			public uint ChangeSum
			{
				get => changeSum[0];
				internal set => changeSum[0] = value;
			}

			public ChunkReadbackBuffer(ComputeBuffer gpuBuffer, NativeArray<Voxel> data, int voxPerChunkDim)
			{
				this.gpuBuffer = gpuBuffer;
				this.data = data;
				this.voxPerChunkDim = voxPerChunkDim;
				changeSum = new NativeArray<uint>(1, Allocator.Persistent);
			}

			public void Dispose()
			{
				gpuBuffer?.Dispose();
				data.Dispose();
				if (changeSum.IsCreated) changeSum.Dispose();
			}
		}

		/// <summary>
		/// Readback result from environment scanner.
		/// `visibleChunks` and `changeSums` MUST be used FRAME OF READBACK!
		/// TODO: enforce this somehow
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
			CalculateDimensions();
			Instance = this;
		}

		private void OnValidate()
		{
			updateFrequency = Mathf.Max(updateFrequency, 1.0f);
			frameSize = math.max(frameSize, new int2(4, 1));
			frameSize.x = frameSize.x / 4 * 4;
			// the shaders address chunks with shifts and masks
			voxPerChunkDim = math.ceilpow2(math.max(voxPerChunkDim, 8));
			chunkDataDims = math.ceilpow2(math.max(chunkDataDims, 1));
		}

		private void CalculateDimensions()
		{
			chunkWorldSizeDim = voxSize * (voxPerChunkDim - 2);
			int3 cdd = chunkDataDims;
			maxNumChunks = cdd.x * cdd.y * cdd.z;
			chunkTableLength = chunkTableDims.x * chunkTableDims.y * chunkTableDims.z;
		}

		private bool GpuResourcesCreated => live != null;

		private static readonly int frameSizeID = Shader.PropertyToID("frameSize");
		private static readonly int frameTanID = Shader.PropertyToID("frameTan");
		private static readonly int frameZRangeID = Shader.PropertyToID("frameZRange");
		private static readonly int frameWeightID = Shader.PropertyToID("frameWeight");
		private static readonly int frameCodeBitsID = Shader.PropertyToID("frameCodeBits");
		private static readonly int frameViewID = Shader.PropertyToID("frameView");
		private static readonly int frameViewInvID = Shader.PropertyToID("frameViewInv");
		private static readonly int framePackedID = Shader.PropertyToID("framePacked");
		private static readonly int frameDepthID = Shader.PropertyToID("frameDepth");
		private static readonly int frameNormalID = Shader.PropertyToID("frameNormal");
		private static readonly int frameChangeID = Shader.PropertyToID("frameChange");
		private static readonly int envMaskTexID = Shader.PropertyToID("envMaskTex");
		private static readonly int maskViewProjID = Shader.PropertyToID("_EnvMaskViewProj");
		private static readonly int maskEyePosID = Shader.PropertyToID("_EnvMaskEyePos");
		private static readonly int readbackBufferID = Shader.PropertyToID("readbackBuffer");
		private static readonly int readbackChunkIndexID = Shader.PropertyToID("readbackChunkIndex");
		private static readonly int uploadBufferID = Shader.PropertyToID("uploadBuffer");
		private static readonly int uploadChunkIndexID = Shader.PropertyToID("uploadChunkIndex");

		private void CreateGpuResources()
		{
			maskMat = new Material(maskShader);
			maskCmd = new CommandBuffer { name = "EnvScanner Mask" };

			// buffers shared by every volume: transient, read back in the frame they are written
			visibleChangeSumsReadback = new ComputeBuffer(maxNumVisibleChunks, sizeof(uint));
			int3 ctd = chunkTableDims;
			frameChange = new ComputeBuffer(1, sizeof(uint));
			uploadBuffer = new ComputeBuffer(voxPerChunkDim / 2 * voxPerChunkDim * voxPerChunkDim, sizeof(uint));

			// uniform values
			compute.SetFloat(nameof(voxSize), voxSize);
			compute.SetFloat(nameof(chunkWorldSizeDim), chunkWorldSizeDim);
			compute.SetVector(nameof(originOffset), new Vector4(originOffset.x, originOffset.y, originOffset.z, 0f));
			compute.SetFloat(nameof(distanceTruncationBand), distanceTruncationBand);
			compute.SetInt(nameof(voxPerChunkDim), voxPerChunkDim);
			compute.SetInts(nameof(chunkTableDims), ctd.x, ctd.y, ctd.z);
			compute.SetInt(nameof(chunkTableLength), chunkTableLength);
			compute.SetInt(nameof(maxNumVisibleChunks), maxNumVisibleChunks);
			compute.SetInt(nameof(maxNumChunks), maxNumChunks);

			captureKernel = new ComputeKernel(compute, "CaptureFrame");
			decodeKernel = new ComputeKernel(compute, "DecodeFrame");
			normalsKernel = new ComputeKernel(compute, "FrameNormals");

			// mark kernels: the live sensor (full resolution) and decoded frames
			markSensorKernel = new ComputeKernel(compute, "MarkSensor");
			markKernel = new ComputeKernel(compute, "Mark");

			// integrate kernels, same split
			integrateSensorKernel = new ComputeKernel(compute, "IntegrateSensor");
			integrateKernel = new ComputeKernel(compute, "Integrate");
			cullBlocksSensorKernel = new ComputeKernel(compute, "CullBlocksSensor");
			cullBlocksKernel = new ComputeKernel(compute, "CullBlocks");
			foreach (ComputeKernel kernel in new[] { integrateSensorKernel, integrateKernel })
				kernel.Bind(frameChangeID, frameChange);

			integrateDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			int groupsPerChunkDim = voxPerChunkDim / integrateKernel.groupSize.x;
			int groupsPerChunkDimLog2 = math.tzcnt(groupsPerChunkDim);
			compute.SetInt(nameof(groupsPerChunkDimLog2), groupsPerChunkDimLog2);
			int groupsPerChunk = groupsPerChunkDim * groupsPerChunkDim * groupsPerChunkDim;
			integrateDispatchDims.SetData(new uint[] { 0, (uint)groupsPerChunk, 1 });

			// matches IntegrateBlock in EnvScanner.compute
			const int integrateBlockStride = 3 * sizeof(float) + 3 * sizeof(uint) + sizeof(int);
			integrateBlocks = new ComputeBuffer(maxNumVisibleChunks * groupsPerChunk, integrateBlockStride, ComputeBufferType.Append);
			integrateBlockDispatchDims = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.IndirectArguments);
			integrateBlockDispatchDims.SetData(new uint[] { 0, 1, 1 });
			foreach (ComputeKernel kernel in new[] { cullBlocksSensorKernel, cullBlocksKernel })
				kernel.Bind("integrateBlocksAppend", integrateBlocks);
			foreach (ComputeKernel kernel in new[] { integrateSensorKernel, integrateKernel })
				kernel.Bind(nameof(integrateBlocks), integrateBlocks);

			clearKernel = new ComputeKernel(compute, "Clear");
			dataReadbackKernel = new ComputeKernel(compute, "ChunkDataReadback");
			reserveKernel = new ComputeKernel(compute, "ChunkReserve");
			uploadKernel = new ComputeKernel(compute, "ChunkDataUpload");
			uploadKernel.Bind(uploadBufferID, uploadBuffer);
			visChangeSumsReadbackKernel = new ComputeKernel(compute, "VisibleChunkChangeSumsReadback");
			visChangeSumsReadbackKernel.Bind(nameof(visibleChangeSumsReadback), visibleChangeSumsReadback);

			// sized for 16-bit containers; 8-bit frames use the first half
			localPacked = new ComputeBuffer(frameSize.x * frameSize.y / 2, sizeof(uint));
			EnsureRemoteFrameCapacity(frameSize);

			// freshly allocated GPU memory holds whatever was there before. listeners are not
			// told about this — they hold chunks meshed from peers, which no local scan owns
			live = new EnvVolume(this, chunkDataDims, voxPerChunkDim, chunkTableLength, maxNumVisibleChunks, "EnvScanner Live Volume");
			BindLiveVolume();
			live.Clear();
		}

		private void BindLiveVolume()
		{
			EnvVolume volume = live;

			int3 chunkDataDimsLog2 = math.tzcnt(volume.DataDims);
			compute.SetInts(nameof(chunkDataDimsLog2), chunkDataDimsLog2.x, chunkDataDimsLog2.y, chunkDataDimsLog2.z);
			compute.SetInt(nameof(maxNumChunks), volume.MaxNumChunks);

			foreach (ComputeKernel kernel in new[] { markSensorKernel, markKernel })
			{
				kernel.Bind("reservedChunkCounter", volume.ReservedChunkCounter);
				kernel.Bind("chunkTable", volume.ChunkTable);
				kernel.Bind("visibleChunksAppend", volume.VisibleChunks);
			}

			foreach (ComputeKernel kernel in new[] { integrateSensorKernel, integrateKernel, uploadKernel })
			{
				kernel.Bind("chunkTable", volume.ChunkTable);
				kernel.Bind("chunkData", volume.ChunkData);
				kernel.Bind("chunkChangeSums", volume.ChunkChangeSums);
			}

			foreach (ComputeKernel kernel in new[] { cullBlocksSensorKernel, cullBlocksKernel })
			{
				kernel.Bind("chunkTable", volume.ChunkTable);
				kernel.Bind("visibleChunks", volume.VisibleChunks);
			}

			clearKernel.Bind("chunkData", volume.ChunkData);
			dataReadbackKernel.Bind("chunkTable", volume.ChunkTable);
			dataReadbackKernel.Bind("chunkData", volume.ChunkData);
			dataReadbackKernel.Bind("chunkChangeSums", volume.ChunkChangeSums);
			reserveKernel.Bind("chunkTable", volume.ChunkTable);
			reserveKernel.Bind("reservedChunkCounter", volume.ReservedChunkCounter);
			visChangeSumsReadbackKernel.Bind("visibleChunks", volume.VisibleChunks);
			visChangeSumsReadbackKernel.Bind("chunkChangeSums", volume.ChunkChangeSums);
		}

		/// <summary>
		/// Grows the peer frame resources to fit <paramref name="size"/>. They never shrink, so
		/// peers with different frame sizes share them; the kernels address them by the
		/// current frame's size
		/// </summary>
		private void EnsureRemoteFrameCapacity(int2 size)
		{
			if (frameDepth && math.all(size <= remoteFrameCapacity)) return;

			remoteFrameCapacity = math.max(remoteFrameCapacity, size);
			int2 capacity = remoteFrameCapacity;

			// Unity's null placeholder objects need the bool operator, not ?.
			if (frameDepth) frameDepth.Release();
			if (frameNormal) frameNormal.Release();
			remotePacked?.Release();

			// sized for 16-bit containers; 8-bit frames use the first half
			remotePacked = new ComputeBuffer(capacity.x * capacity.y / 2, sizeof(uint));

			frameDepth = new RenderTexture(capacity.x, capacity.y, 0, GraphicsFormat.R32_SFloat)
				{ name = "EnvScanner Frame Depth", enableRandomWrite = true, filterMode = FilterMode.Point };
			frameDepth.Create();
			frameNormal = new RenderTexture(capacity.x, capacity.y, 0, GraphicsFormat.R16G16B16A16_SFloat)
				{ name = "EnvScanner Frame Normal", enableRandomWrite = true, filterMode = FilterMode.Point };
			frameNormal.Create();

			decodeKernel.Bind(frameDepthID, frameDepth);
			normalsKernel.Bind(frameDepthID, frameDepth);
			normalsKernel.Bind(frameNormalID, frameNormal);
			markKernel.Bind(frameDepthID, frameDepth);
			integrateKernel.Bind(frameDepthID, frameDepth);
			integrateKernel.Bind(frameNormalID, frameNormal);
		}

		private static readonly int[] EmptyCounterArray = new int[1];
		private static readonly uint[] EmptyChangeArray = new uint[1];
		private static int[] EmptyChunkTableArray;
		private static int[] EmptyVisibleChunksArray;

		internal void ClearVolume(EnvVolume volume)
		{
			if (!GpuResourcesCreated || !volume.IsCreated) return;

			if (EmptyChunkTableArray == null || EmptyChunkTableArray.Length != chunkTableLength)
				EmptyChunkTableArray = new int[chunkTableLength];

			if (EmptyVisibleChunksArray == null || EmptyVisibleChunksArray.Length != maxNumVisibleChunks)
				EmptyVisibleChunksArray = new int[maxNumVisibleChunks];

			volume.ReservedChunkCounter.SetData(EmptyCounterArray);
			volume.ChunkTable.SetData(EmptyChunkTableArray);
			volume.ChunkChangeSums.SetData(EmptyChunkTableArray);
			volume.VisibleChunks.SetData(EmptyVisibleChunksArray);

			clearKernel.DispatchFit(volume.ChunkData);
		}

		/// <summary>Empties the live map.</summary>
		public void Clear()
		{
			if (GpuResourcesCreated)
				live.Clear();

			Cleared.Invoke();
		}

		private void Start()
		{
			OnDepthAvailabilityChanged(DepthKitDriver.DepthAvailable);
			DepthKitDriver.AvailabilityChanged += OnDepthAvailabilityChanged;
		}

		private void OnEnable()
		{
			if (!didStart) return;

			OnDepthAvailabilityChanged(DepthKitDriver.DepthAvailable);
			DepthKitDriver.AvailabilityChanged += OnDepthAvailabilityChanged;
		}

		private void OnDisable()
		{
			DepthKitDriver.AvailabilityChanged -= OnDepthAvailabilityChanged;

			if (GpuResourcesCreated && EmptyVisibleChunksArray != null)
				live.VisibleChunks.SetData(EmptyVisibleChunksArray);

			updateLoopTknSrc?.Cancel();
		}

		private void OnDepthAvailabilityChanged(bool depthAvailable)
		{
			if (!depthAvailable)
			{
				updateLoopTknSrc?.Cancel();
				return;
			}

			if (!GpuResourcesCreated)
				CreateGpuResources();

			UpdateLoop();
		}

		private void OnDestroy()
		{
			live?.Dispose();
			live = null;
			integrateDispatchDims?.Release();
			integrateBlocks?.Release();
			integrateBlockDispatchDims?.Release();
			visibleChangeSumsReadback?.Release();
			frameChange?.Release();
			uploadBuffer?.Release();
			localPacked?.Release();
			remotePacked?.Release();
			if (frameDepth) frameDepth.Release();
			if (frameNormal) frameNormal.Release();
			if (maskTex) maskTex.Release();
			maskCmd?.Release();
			if (maskMat) Destroy(maskMat);
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
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		// the gate is about trusting the LOCAL sensor's pose; frames and chunks from peers
		// arrive already expressed in the shared frame, so they bypass it

		/// <summary>
		/// Integrates the live depth sensor at full resolution and precision into the live
		/// map, captures the same view into a frame for peers, and reports that frame
		/// through <see cref="FrameCaptured"/> once the sensor's effect on the volume is known
		/// </summary>
		public void Scan()
		{
			if (!GpuResourcesCreated || !DepthKitDriver.DepthAvailable || !(IntegrationGate?.Invoke() ?? true)) return;

			DepthKitDriver dkd = DepthKitDriver.Instance;
			SetSensorUniforms(dkd);

			DepthFrameHeader header = BuildLocalHeader(dkd);
			SetFrameUniforms(header);

			// the wire frame: same view, same filters, downsampled and quantized
			captureKernel.Bind(DepthKitDriver.depthTexID, dkd.DepthTex);
			captureKernel.Bind(DepthKitDriver.normTexID, dkd.NormTex);
			captureKernel.Bind(envMaskTexID, maskTex);
			captureKernel.Bind(framePackedID, localPacked);
			captureKernel.DispatchFit(frameSize.x / 4, frameSize.y);

			// the local volume takes the sensor itself
			frameChange.SetData(EmptyChangeArray);
			IntegrateSensor(dkd);
			Updated.Invoke();

			ReportCapturedFrame(header);
		}

		private void SetSensorUniforms(DepthKitDriver dkd)
		{
			SetSourceLimits(minDist, maxDist, minDot);
			compute.SetMatrixArray(DepthKitDriver.viewID, dkd.View);
			compute.SetMatrixArray(DepthKitDriver.projID, dkd.Proj);
			compute.SetMatrixArray(DepthKitDriver.viewInvID, dkd.ViewInv);
			compute.SetMatrixArray(DepthKitDriver.projInvID, dkd.ProjInv);
			RenderMask(dkd);
		}

		// mark → integrate the full-resolution sensor into the live map
		private void IntegrateSensor(DepthKitDriver dkd)
		{
			live.VisibleChunks.SetCounterValue(0);

			markSensorKernel.Bind(DepthKitDriver.depthTexID, dkd.DepthTex);
			markSensorKernel.Bind(envMaskTexID, maskTex);
			markSensorKernel.DispatchFit(dkd.DepthTex, 1);

			integrateSensorKernel.Bind(DepthKitDriver.depthTexID, dkd.DepthTex);
			integrateSensorKernel.Bind(DepthKitDriver.normTexID, dkd.NormTex);
			integrateSensorKernel.Bind(envMaskTexID, maskTex);
			CullAndIntegrateBlocks(cullBlocksSensorKernel, integrateSensorKernel);
		}

		private void CullAndIntegrateBlocks(ComputeKernel cullKernel, ComputeKernel integrateBlocksKernel)
		{
			ComputeBuffer.CopyCount(live.VisibleChunks, integrateDispatchDims, 0);
			integrateBlocks.SetCounterValue(0);
			cullKernel.DispatchIndirect(integrateDispatchDims);

			ComputeBuffer.CopyCount(integrateBlocks, integrateBlockDispatchDims, 0);
			integrateBlocksKernel.DispatchIndirect(integrateBlockDispatchDims);
		}

		private DepthFrameHeader BuildLocalHeader(DepthKitDriver dkd)
		{
			DepthFrameHeader.PoseFromViewInverse(dkd.ViewInv[0], out float3 position, out quaternion rotation);

			return new DepthFrameHeader
			{
				version = DepthFrameHeader.CurrentVersion,
				width = (ushort)frameSize.x,
				height = (ushort)frameSize.y,
				codeBits = (byte)(frameCodeBits > 8 ? 12 : 8),
				// locally every sensor frame is integrated as itself
				weight = 1,
				position = position,
				rotation = rotation,
				tangents = DepthFrameHeader.TangentsFromProjection(dkd.Proj[0]),
				// the range filter is on eye distance; perpendicular depth at the frame's
				// edge is shorter than that, so the code spans below the minimum
				zRange = new float2(minDist * 0.5f, maxDist),
				eyeDistanceRange = new float2(minDist, maxDist),
				minSurfaceDot = minDot
			};
		}

		private void SetSourceLimits(float minDistance, float maxDistance, float minSurfaceDot)
		{
			compute.SetFloat(nameof(minDist), minDistance);
			compute.SetFloat(nameof(maxDist), maxDistance);
			compute.SetFloat(nameof(minDot), minSurfaceDot);
		}

		private void SetFrameUniforms(in DepthFrameHeader header)
		{
			SetSourceLimits(header.eyeDistanceRange.x, header.eyeDistanceRange.y, header.minSurfaceDot);
			compute.SetInts(frameSizeID, header.width, header.height);
			compute.SetVector(frameTanID, (Vector4)header.tangents);
			compute.SetVector(frameZRangeID, new Vector4(header.zRange.x, header.zRange.y, 0, 0));
			compute.SetInt(frameWeightID, Math.Max(header.weight, (ushort)1));
			compute.SetInt(frameCodeBitsID, header.codeBits);
			Matrix4x4 viewInv = header.ViewInverse;
			compute.SetMatrix(frameViewInvID, viewInv);
			compute.SetMatrix(frameViewID, viewInv.inverse);
		}

		// decode → normals → mark → integrate, for a frame received from a peer
		private void IntegrateBoundFrame(ComputeBuffer packed, int2 size)
		{
			decodeKernel.Bind(framePackedID, packed);
			decodeKernel.DispatchFit(size.x, size.y);
			normalsKernel.DispatchFit(size.x, size.y);

			// reset visible chunks counter
			live.VisibleChunks.SetCounterValue(0);

			// mark active chunks
			markKernel.DispatchFit(size.x, size.y);

			// integrate into active chunks
			CullAndIntegrateBlocks(cullBlocksKernel, integrateKernel);

			Updated.Invoke();
		}

		private async void ReportCapturedFrame(DepthFrameHeader header)
		{
			Awaitable<AsyncGPUReadbackRequest> packedWait = AsyncGPUReadback.RequestAsync(localPacked);
			Awaitable<AsyncGPUReadbackRequest> changeWait = AsyncGPUReadback.RequestAsync(frameChange);

			AsyncGPUReadbackRequest packedReq = await packedWait;
			AsyncGPUReadbackRequest changeReq = await changeWait;

			if (!this || packedReq.hasError || changeReq.hasError)
			{
				LogDebug("Frame readback failed!", LogType.Warning);
				return;
			}

			NativeArray<byte> packedData = packedReq.GetData<byte>();
			if (packedData.Length < header.PackedBytes) return;
			// the buffer is sized for 16-bit containers; 8-bit frames fill only its first half
			packedData = packedData.GetSubArray(0, header.PackedBytes);

			uint change = changeReq.GetData<uint>()[0];

			try
			{
				FrameCaptured.Invoke(new CapturedFrame(header, packedData, change));
			}
			catch (Exception e)
			{
				Debug.LogException(e, this);
			}
		}

		/// <summary>
		/// Integrates a frame another headset captured. Returns false when the frame could
		/// not be integrated (no GPU resources yet created, or malformed)
		/// </summary>
		public bool IntegrateFrame(in DepthFrameHeader header, NativeArray<byte> codes)
		{
			if (!header.IsSane || codes.Length != header.PackedBytes) return false;

			if (!GpuResourcesCreated) CreateGpuResources();

			int2 size = new(header.width, header.height);
			EnsureRemoteFrameCapacity(size);
			SetFrameUniforms(header);

			remotePacked.SetData(codes);
			IntegrateBoundFrame(remotePacked, size);
			return true;
		}

		/// <summary>
		/// Fuses a whole chunk (e.g. a snapshot from the session authority) into the live map
		/// with the same count-weighted average integration uses
		/// </summary>
		public bool UploadChunk(int chunkIndex, NativeArray<Voxel> voxels)
		{
			if (chunkIndex < 0 || chunkIndex >= chunkTableLength) return false;
			if (voxels.Length != voxPerChunkDim * voxPerChunkDim * voxPerChunkDim) return false;

			if (!GpuResourcesCreated) CreateGpuResources();

			uploadBuffer.SetData(voxels.Reinterpret<uint>(Voxel.stride));
			compute.SetInt(uploadChunkIndexID, chunkIndex);
			reserveKernel.DispatchGroups(1, 1, 1);

			int vpcd = voxPerChunkDim;
			uploadKernel.DispatchFit(vpcd / 2, vpcd, vpcd);

			ChunkUploaded.Invoke(chunkIndex);
			return true;
		}

		/// <summary>
		/// Renders every <see cref="EnvScanMask"/> into <see cref="maskTex"/> as seen by the
		/// latest depth frame's eye 0, so its texels line up with the depth texture
		/// </summary>
		private void RenderMask(DepthKitDriver dkd)
		{
			Texture depthTex = dkd.DepthTex;
			if (!maskTex || maskTex.width != depthTex.width || maskTex.height != depthTex.height)
			{
				if (maskTex) maskTex.Release();
				maskTex = new RenderTexture(depthTex.width, depthTex.height, GraphicsFormat.R16_SFloat,
					GraphicsFormat.D16_UNorm)
				{
					name = "EnvScanner Mask",
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
				maskTex.Create();
			}

			Matrix4x4 viewProj = GL.GetGPUProjectionMatrix(dkd.Proj[0], true) * dkd.View[0];

			maskCmd.Clear();
			maskCmd.SetRenderTarget(maskTex);
			// cleared to half max: further than anything, so masks nothing
			maskCmd.ClearRenderTarget(true, true, new Color(65504f, 0, 0, 0), 1f);
			maskCmd.SetGlobalMatrix(maskViewProjID, viewProj);
			maskCmd.SetGlobalVector(maskEyePosID, dkd.ViewInv[0].GetColumn(3));

			// drawn regardless of culling, so masks on a layer no camera renders still draw
			foreach (EnvScanMask mask in EnvScanMask.All)
			{
				for (int i = 0; i < mask.SubMeshCount; i++)
					maskCmd.DrawRenderer(mask.Renderer, maskMat, i, 0);
			}

			Graphics.ExecuteCommandBuffer(maskCmd);
		}

		public async Awaitable<VisibleChunksReadbackResult> ReadbackVisibleChunks()
		{
			visChangeSumsReadbackKernel.DispatchFit(maxNumVisibleChunks, 1, 1);

			Awaitable<AsyncGPUReadbackRequest> dataReqWait = AsyncGPUReadback.RequestAsync(live.VisibleChunks);
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

			// the packed voxels, then the chunk's change sum
			ComputeBuffer computeBuffer = new(vpcd / 2 * vpcd * vpcd + 1, sizeof(uint));
			NativeArray<Voxel> data = new(vpcd * vpcd * vpcd, Allocator.Persistent);

			return new ChunkReadbackBuffer(computeBuffer, data, vpcd);
		}

		/// <summary>Reads one chunk of the live map back to the CPU.</summary>
		public async Awaitable<bool> ReadbackChunkInto(int chunkIndex, ChunkReadbackBuffer readbackBuffer)
		{
			if (readbackBuffer.voxPerChunkDim != voxPerChunkDim)
				throw new Exception("Readback chunk dimensions do not match scanner chunk dimensions");

			if (chunkIndex < 0 || chunkIndex >= chunkTableLength)
			{
				LogDebug("Readback chunk index out of range!", LogType.Warning);
				return false;
			}

			if (!GpuResourcesCreated) return false;

			compute.SetInt(readbackChunkIndexID, chunkIndex);
			dataReadbackKernel.Bind(readbackBufferID, readbackBuffer.gpuBuffer);

			// each thread covers TWO voxels on X axis for byte packing
			int vpcd = voxPerChunkDim;
			dataReadbackKernel.DispatchFit(vpcd / 2, vpcd, vpcd);

			AsyncGPUReadbackRequest req = await AsyncGPUReadback.RequestAsync(readbackBuffer.gpuBuffer);

			if (req.hasError)
			{
				LogDebug("Readback chunk failed!", LogType.Warning);
				return false;
			}

			NativeArray<byte> bytes = req.GetData<byte>();
			int voxelBytes = readbackBuffer.data.Length * Voxel.stride;
			bytes.GetSubArray(0, voxelBytes).Reinterpret<Voxel>(1).CopyTo(readbackBuffer.data);
			readbackBuffer.ChangeSum = bytes.GetSubArray(voxelBytes, sizeof(uint)).Reinterpret<uint>(1)[0];
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
			int3 quantized = (int3)math.floor((world - originOffset) / chunkWorldSizeDim);
			int3 chunkCoord = quantized + chunkTableDims / 2;
			return chunkCoord;
		}

		public int ChunkCoordToChunkIndex(int3 coord)
		{
			if (math.any(coord < 0) || math.any(coord >= (int3)chunkTableDims))
				return -1;

			return coord.x +
				coord.y * chunkTableDims.x +
				coord.z * chunkTableDims.x * chunkTableDims.y;
		}

		public void DrawChunkBounds(int3 chunkCoord, Color color)
		{
			float3 cornerPos = ChunkCoordToCornerWorldPos(chunkCoord);
			float3 center = cornerPos + chunkWorldSizeDim / 2;
			XRGizmos.DrawWireCube(center, Quaternion.identity, new Vector3(chunkWorldSizeDim, chunkWorldSizeDim, chunkWorldSizeDim), color);
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

		/// <summary>
		/// One voxel as the GPU packs it for readback: the TSDF value and how many
		/// times it has been integrated (0 = never, i.e. the chunk's cleared state)
		/// </summary>
		[StructLayout(LayoutKind.Sequential)]
		public readonly struct Voxel
		{
			public readonly sbyte value;
			public readonly byte count;

			public const int stride = 2;

			public bool IsEmpty => count == 0;

			public float CalcDistNorm()
			{
				return value / (float)sbyte.MaxValue;
			}

			public Voxel(sbyte value, byte count = 1)
			{
				this.value = value;
				this.count = count;
			}
		}
	}
}
