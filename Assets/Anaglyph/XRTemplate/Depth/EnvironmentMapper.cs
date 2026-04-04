using System;
using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private float voxelSize = 0.1f;
		[SerializeField] private float voxelDistance = 0.2f;
		[SerializeField] private float voxelMin = 0.1f;

		[SerializeField] private float depthDisparityThreshold = 1f;
		[SerializeField] private int depthDilationSteps = 8;
		private int depthDilationMaxStep = 0;

		public float updateFrequency = 5f;

		[SerializeField] private RenderTexture volume;

		private RenderTexture dilationA, dilationB;
		[SerializeField] private RenderTexture dilatedDepth;

		public int3 VoxelCount { get; private set; }

		[SerializeField] private float maxUpdateDist = 6f;
		[SerializeField] private float minUpdateDist = 1f;

		public float VoxelSize => voxelSize;
		public float VoxelDistance => voxelDistance;
		public float MaxUpdateDist => maxUpdateDist;
		public RenderTexture Volume => volume;

		private static int viewID => DepthKitDriver.viewID;
		private static int projID => DepthKitDriver.projID;

		private static int viewInvID => DepthKitDriver.viewInvID;
		private static int projInvID => DepthKitDriver.projInvID;

		private static int depthTexID => DepthKitDriver.depthTexID;
		private static int normTexID => DepthKitDriver.normTexID;

		private static readonly int volumeWritableID = Shader.PropertyToID("envVolumeRW");
		private static readonly int volumeID = Shader.PropertyToID("envVolume");
		private static readonly int voxelCountID = Shader.PropertyToID("envVoxCount");
		private static readonly int voxelSizeID = Shader.PropertyToID("envVoxSize");
		private static readonly int voxelMinID = Shader.PropertyToID("envVoxMin");
		private static readonly int depthDisparityThresholdID = Shader.PropertyToID("depthDispThresh");
		private static readonly int voxelDistanceID = Shader.PropertyToID("envVoxDist");
		private static readonly int frustumVolumeID = Shader.PropertyToID("envFrustumVolume");
		private static readonly int dilatedDepthID = Shader.PropertyToID("envDilatedDepth");

		private static readonly int dilateSrcID = Shader.PropertyToID("dilateSrc");
		private static readonly int dilateDestID = Shader.PropertyToID("dilateDest");
		private static readonly int dilateStepSizeID = Shader.PropertyToID("dilateStepSize");

		private static readonly int numPlayersID = Shader.PropertyToID("envNumPlayers");
		private static readonly int playerHeadsWorldID = Shader.PropertyToID("envPlayerHeads");

		private static readonly int numRaymarchRequestsID = Shader.PropertyToID("numRaymarchRequests");
		private static readonly int raymarchRequestsID = Shader.PropertyToID("raymarchRequests");
		private static readonly int raymarchResultsID = Shader.PropertyToID("raymarchResults");

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;

		private ComputeKernel initDepthDilationKernel;
		private ComputeKernel dilateDepthKernel;

		private ComputeKernel raymarchKernel;

		// pre-computed points within depth sensor frustum
		// to map gpu threads to voxels. like froxels
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private readonly Vector4[] headPositions = new Vector4[512];

		public event Action Updated = delegate { };
		public event Action Cleared = delegate { };
		
		public static bool UseEdgeServer { get; set; }

		private void Awake()
		{
			Instance = this;
			VoxelCount = new int3(volume.width, volume.height, volume.volumeDepth);

			depthDilationMaxStep = 1;

			for (int i = 0; i < depthDilationSteps; i++)
				depthDilationMaxStep *= 2;
		}

		private void Start()
		{
			clearKernel = new ComputeKernel(compute, "Clear");
			clearKernel.Set(volumeWritableID, volume);

			integrateKernel = new ComputeKernel(compute, "Integrate");
			integrateKernel.Set(volumeWritableID, volume);

			initDepthDilationKernel = new ComputeKernel(compute, "InitDepthDilation");
			dilateDepthKernel = new ComputeKernel(compute, "DilateDepthStep");

			raymarchKernel = new ComputeKernel(compute, "Raymarch");
			raymarchKernel.Set(volumeID, volume);

			Shader.SetGlobalTexture(volumeID, volume);

			int3 s = VoxelCount;
			compute.SetInts(voxelCountID, s.x, s.y, s.z);
			Shader.SetGlobalVector(voxelCountID, new Vector4(s.x, s.y, s.z, 0));

			compute.SetFloat(voxelSizeID, voxelSize);
			Shader.SetGlobalFloat(voxelSizeID, voxelSize);

			compute.SetFloat(voxelMinID, voxelMin);

			compute.SetFloat(voxelDistanceID, voxelDistance);
			Shader.SetGlobalFloat(voxelDistanceID, voxelDistance);

			compute.SetFloat(depthDisparityThresholdID, depthDisparityThreshold);

			Clear();

			UpdateLoop();
		}

		private void OnEnable()
		{
			if (!didStart)
				UpdateLoop();
		}

		private void OnDestroy()
		{
			frustumVolume?.Release();
		}


		public void Clear()
		{
			clearKernel.DispatchFit(volume);
			Cleared.Invoke();
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

					if (frustumVolume == null) Setup();

					if (UseEdgeServer)
					{
						Updated.Invoke();
						continue;
					}

					ApplyScan();

					Updated.Invoke();
				}
			}
			catch (OperationCanceledException _)
			{
			}
		}

		private void Setup()
		{
			if (!DepthKitDriver.DepthAvailable) return;

			// set up frustum volume

			Matrix4x4 depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.projID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			frustum.zFar = maxUpdateDist;

			List<Vector3> positions = new(200000);

			FrustumPlanes f = frustum;
			// slopes 
			float ls = f.left / f.zNear;
			float rs = f.right / f.zNear;
			float ts = f.top / f.zNear;
			float bs = f.bottom / f.zNear;

			for (float z = f.zNear; z < f.zFar; z += voxelSize)
			{
				float xMin = ls * z + voxelSize;
				float xMax = rs * z - voxelSize;

				float yMin = bs * z + voxelSize;
				float yMax = ts * z - voxelSize;

				for (float x = xMin; x < xMax; x += voxelSize)
				for (float y = yMin; y < yMax; y += voxelSize)
				{
					Vector3 v = new(x, y, -z);

					if (v.magnitude > minUpdateDist && v.magnitude < maxUpdateDist)
						positions.Add(v);
				}
			}

			if (positions.Count == 0)
				return;

			frustumVolume = new ComputeBuffer(positions.Count, sizeof(float) * 3);

			frustumVolume.SetData(positions);
			integrateKernel.Set(frustumVolumeID, frustumVolume);

			// set up dilated depth tex

			RenderTextureDescriptor dilateTexDesc = new()
			{
				width = DepthKitDriver.Instance.DepthTex.width,
				height = DepthKitDriver.Instance.DepthTex.height,
				volumeDepth = 1,
				dimension = TextureDimension.Tex2D,
				autoGenerateMips = false,
				enableRandomWrite = true,
				graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
				msaaSamples = 1
			};

			dilationA = new RenderTexture(dilateTexDesc);
			dilationB = new RenderTexture(dilateTexDesc);
		}

		public void ApplyScan()
		{
			// set state

			DepthKitDriver dkd = DepthKitDriver.Instance;

			if (!DepthKitDriver.DepthAvailable) return;

			compute.SetMatrixArray(viewID, dkd.View);
			compute.SetMatrixArray(projID, dkd.Proj);

			compute.SetMatrixArray(viewInvID, dkd.ViewInv);
			compute.SetMatrixArray(projInvID, dkd.ProjInv);

			compute.SetInt(numPlayersID, PlayerHeads.Count);
			compute.SetVectorArray(playerHeadsWorldID, headPositions);

			// dilate depth tex
			initDepthDilationKernel.Set(depthTexID, dkd.DepthTex);
			initDepthDilationKernel.Set(dilateSrcID, dilationA);
			initDepthDilationKernel.Set(dilateDestID, dilationB);
			initDepthDilationKernel.DispatchFit(dilationA);

			int stepSize = depthDilationMaxStep;

			for (int i = 0; i < stepSize; i++)
			{
				dilateDepthKernel.Set(dilateSrcID, dilationA);
				dilateDepthKernel.Set(dilateDestID, dilationB);
				compute.SetInt(dilateStepSizeID, stepSize);
				dilateDepthKernel.DispatchFit(dilationA);

				stepSize /= 2;
				(dilationA, dilationB) = (dilationB, dilationA);
			}

			dilatedDepth = dilationA;

			// integrate depth into world volume
			for (int i = 0; i < PlayerHeads.Count; i++)
			{
				Vector3 playerHead = PlayerHeads[i].position;
				headPositions[i] = playerHead;
			}

			compute.SetMatrixArray(DepthKitDriver.projID, dkd.Proj);
			integrateKernel.Set(depthTexID, dkd.DepthTex);
			integrateKernel.Set(normTexID, dkd.NormTex);
			integrateKernel.Set(dilatedDepthID, dilatedDepth);
			integrateKernel.DispatchFit(frustumVolume.count, 1);
		}

		[StructLayout(LayoutKind.Sequential)]
		public struct RaymarchRequest
		{
			public RaymarchRequest(Ray ray, float maxDistance)
			{
				origin = ray.origin;
				direction = ray.direction;
				this.maxDistance = maxDistance;
			}

			public Vector4 origin;
			public Vector3 direction;
			public float maxDistance;
		}

		private readonly int requestStride = Marshal.SizeOf<RaymarchRequest>();

		private readonly List<RaymarchRequest> pendingRequests = new();

		public struct RaymarchResult
		{
			public readonly Ray ray;
			public readonly Vector3 point;
			public readonly float distance;
			public readonly bool didHit;

			public RaymarchResult(Ray ray, float distance)
			{
				this.ray = ray;
				point = ray.origin + ray.direction * distance;
				this.distance = distance;
				didHit = distance >= 0;
			}
		}

		private Task<float[]> currentRaymarchBatch = null;

		public async Task<RaymarchResult> RaymarchAsync(Ray ray, float maxDistance)
		{
			RaymarchRequest request = new(ray, maxDistance);
			int index = pendingRequests.Count;
			pendingRequests.Add(request);

			currentRaymarchBatch ??= DispatchRaymarches();

			float[] data = await currentRaymarchBatch;
			float dist = data[index];

			RaymarchResult result = new(ray, dist);
			return result;
		}

		private async Task<float[]> DispatchRaymarches()
		{
			await Awaitable.EndOfFrameAsync();

			int count = pendingRequests.Count;
			ComputeBuffer requestsBuffer = new(count, requestStride);
			requestsBuffer.SetData(pendingRequests);
			pendingRequests.Clear();
			ComputeBuffer resultBuffer = new(count, sizeof(float));
			currentRaymarchBatch = null;

			compute.SetInt(numRaymarchRequestsID, count);
			raymarchKernel.Set(raymarchRequestsID, requestsBuffer);
			raymarchKernel.Set(raymarchResultsID, resultBuffer);

			raymarchKernel.DispatchFit(count, 1, 1);

			float[] results = new float[count];
			resultBuffer.GetData(results);

			requestsBuffer.Dispose();
			resultBuffer.Dispose();

			return results;
		}

		public float3 VoxelToWorld(uint3 indices)
		{
			float3 pos = indices;
			pos += 0.5f; // voxel center
			pos -= (float3)VoxelCount / 2.0f;
			pos *= voxelSize;

			return pos;
		}

		public float3 WorldToVoxelFloat(float3 pos)
		{
			pos /= VoxelSize;
			pos += (float3)VoxelCount / 2.0f;
			// do not subtract half
			return pos;
		}

		public uint3 WorldToVoxel(float3 pos)
		{
			pos = WorldToVoxelFloat(pos);

			uint3 id = new(math.floor(pos));
			id = math.clamp(id, 0, (uint3)VoxelCount);
			return id;
		}

		// public async Task<bool> TestForCrossings(float3 start, float3 size)
		// {
		// 	uint3 a = WorldToVoxel(start);
		// 	int3 b = new(size / voxelSize);
		//
		// 	ComputeBuffer resultBuffer = new(2, sizeof(uint));
		// 	crossingTestKernel.Set(crossingTestResultID, resultBuffer);
		// 	compute.SetVector(crossingTestStartID, new Vector4(a.x, a.y, a.z, 0));
		//
		// 	crossingTestKernel.DispatchFit(b.x, b.y, b.z);
		//
		// 	AsyncGPUReadbackRequest req = await AwaitReadback(resultBuffer);
		//
		// 	if (req.hasError)
		// 		return false;
		//
		// 	uint[] arr = new uint[2];
		//
		// 	resultBuffer.GetData(arr);
		// 	return arr[0] == 1 && arr[1] == 1;
		// }

		// private static Task<AsyncGPUReadbackRequest> AwaitReadback(ComputeBuffer buffer)
		// {
		// 	TaskCompletionSource<AsyncGPUReadbackRequest> tcs = new();
		//
		// 	AsyncGPUReadback.Request(buffer, (req) => { tcs.SetResult(req); });
		//
		// 	return tcs.Task;
		// }
	}
}