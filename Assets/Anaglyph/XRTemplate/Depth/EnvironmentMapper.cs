using System;
using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Serialization;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private float voxelSize = 0.1f;
		public float VoxelSize => voxelSize;
		[SerializeField] private float voxelDistance = 0.2f;
		public float VoxelDistance => voxelDistance;

		[SerializeField] private float depthDisparityThreshold = 1f;
		public float DepthDisparityThreshold => depthDisparityThreshold;

		[FormerlySerializedAs("maxDepthDilationSteps")] [SerializeField]
		private int depthDilationSteps = 8;

		private int depthDilationMaxStep = 0;

		public float frequency = 5f;

		[SerializeField] private RenderTexture volume;
		public RenderTexture Volume => volume;

		private RenderTexture dilationA, dilationB;
		[SerializeField] private RenderTexture dilatedDepth;

		private int vWidth => volume.width;
		private int vHeight => volume.height;
		private int vDepth => volume.volumeDepth;
		public int3 VoxelCount { get; private set; }

		[SerializeField] private float maxDist = 7f;
		[SerializeField] private float minDist = 1f;
		public float MaxDist => maxDist;
		public float MinDist => minDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;

		private ComputeKernel initDepthDilationKernel;
		private ComputeKernel dilateDepthKernel;

		private ComputeKernel raymarchKernel;

		private static int viewID => DepthKitDriver.viewID;
		private static int projID => DepthKitDriver.projID;

		private static int viewInvID => DepthKitDriver.viewInvID;
		private static int projInvID => DepthKitDriver.projInvID;

		private static int depthTexID => DepthKitDriver.depthTexID;
		private static int normTexID => DepthKitDriver.normTexID;

		private static int ID(string str)
		{
			return Shader.PropertyToID(str);
		}

		private static readonly int volumeWritableID = ID("envVolumeRW");
		private static readonly int volumeID = ID("envVolume");
		private static readonly int voxelCountID = ID("envVoxCount");
		private static readonly int voxelSizeID = ID("envVoxSize");
		private static readonly int depthDisparityThresholdID = ID("depthDispThresh");
		private static readonly int voxelDistanceID = ID("envVoxDist");
		private static readonly int frustumVolumeID = ID("envFrustumVolume");
		private static readonly int dilatedDepthID = ID("envDilatedDepth");

		private static readonly int dilateSrcID = ID("dilateSrc");
		private static readonly int dilateDestID = ID("dilateDest");
		private static readonly int dilateStepSizeID = ID("dilateStepSize");

		private static readonly int numPlayersID = ID("envNumPlayers");
		private static readonly int playerHeadsWorldID = ID("envPlayerHeads");

		private static readonly int numRaymarchRequestsID = ID("numRaymarchRequests");
		private static readonly int raymarchRequestsID = ID("raymarchRequests");
		private static readonly int raymarchResultsID = ID("raymarchResults");

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private readonly Vector4[] headPositions = new Vector4[512];

		private float lastUpdateTime = 0;

		public event Action Updated = delegate { };
		public event Action Cleared = delegate { };

		private void Awake()
		{
			Instance = this;
			VoxelCount = new int3(vWidth, vHeight, vDepth);

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

			compute.SetInts(voxelCountID, vWidth, vHeight, vDepth);
			Shader.SetGlobalVector(voxelCountID, new Vector4(vWidth, vHeight, vDepth, 0));

			compute.SetFloat(voxelSizeID, voxelSize);
			Shader.SetGlobalFloat(voxelSizeID, voxelSize);

			compute.SetFloat(voxelDistanceID, voxelDistance);
			Shader.SetGlobalFloat(voxelDistanceID, voxelDistance);

			compute.SetFloat(depthDisparityThresholdID, depthDisparityThreshold);

			Clear();

			DepthKitDriver.Instance.Updated += OnDepthUpdated;
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
			Cleared.Invoke();
		}

		private void OnEnable()
		{
			if (!didStart)
				DepthKitDriver.Instance.Updated += OnDepthUpdated;
		}

		private void OnDisable()
		{
			if (DepthKitDriver.Instance)
				DepthKitDriver.Instance.Updated -= OnDepthUpdated;
		}

		private void OnDepthUpdated()
		{
			float wait = 1f / frequency;
			if (Time.time < lastUpdateTime + wait) return;

			lastUpdateTime = Time.time;

			Texture depthTex = DepthKitDriver.Instance.DepthTex;
			if (depthTex == null) return;

			if (frustumVolume == null)
			{
				Setup();
				return;
			}

			ApplyScan();

			Updated.Invoke();
		}

		private void Setup()
		{
			if (!DepthKitDriver.DepthAvailable)
				return;

			// set up frustum volume

			Matrix4x4 depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.projID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			frustum.zFar = maxDist;

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

					if (v.magnitude > minDist && v.magnitude < maxDist)
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
				dimension = UnityEngine.Rendering.TextureDimension.Tex2D,
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
			initDepthDilationKernel.DispatchGroups(dilationA);

			int stepSize = depthDilationMaxStep;

			for (int i = 0; i < stepSize; i++)
			{
				dilateDepthKernel.Set(dilateSrcID, dilationA);
				dilateDepthKernel.Set(dilateDestID, dilationB);
				compute.SetInt(dilateStepSizeID, stepSize);
				dilateDepthKernel.DispatchGroups(dilationA);

				stepSize /= 2;
				(dilationA, dilationB) = (dilationB, dilationA);
			}

			dilatedDepth = dilationA;

			// integrate depth into world
			for (int i = 0; i < PlayerHeads.Count; i++)
			{
				Vector3 playerHead = PlayerHeads[i].position;
				headPositions[i] = playerHead;
			}

			compute.SetMatrixArray(DepthKitDriver.projID, dkd.Proj);
			integrateKernel.Set(depthTexID, dkd.DepthTex);
			integrateKernel.Set(normTexID, dkd.NormTex);
			integrateKernel.Set(dilatedDepthID, dilatedDepth);
			integrateKernel.DispatchGroups(frustumVolume.count, 1);
		}

		private void OnDestroy()
		{
			frustumVolume?.Release();
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

		private List<RaymarchRequest> pendingRequests = new();

		public struct RaymarchResult
		{
			public Ray ray;
			public Vector3 point;
			public float distance;
			public bool didHit;

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

			if (currentRaymarchBatch == null) currentRaymarchBatch = DispatchRaymarches();

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

			raymarchKernel.DispatchGroups(count, 1, 1);

			float[] results = new float[count];
			resultBuffer.GetData(results);

			requestsBuffer.Dispose();
			resultBuffer.Dispose();

			return results;

			//var tcs = new TaskCompletionSource<AsyncGPUReadbackRequest>();

			//AsyncGPUReadback.Request(resultBuffer, (req) =>
			//{
			//	if (req.hasError)
			//		tcs.SetException(new System.Exception("GPU readback failed"));
			//	else
			//		tcs.SetResult(req);

			//	requestsBuffer.Dispose();
			//	resultBuffer.Dispose();
			//});

			//AsyncGPUReadbackRequest result = await tcs.Task;
			//return result.GetData<float>().ToArray();
		}

		//private static Task<AsyncGPUReadbackRequest> AwaitReadback(ComputeBuffer buffer)
		//{
		//	var tcs = new TaskCompletionSource<AsyncGPUReadbackRequest>();

		//	AsyncGPUReadback.Request(buffer, (req) =>
		//	{
		//		if (req.hasError)
		//			tcs.SetException(new System.Exception("GPU readback failed"));
		//		else
		//			tcs.SetResult(req);
		//	});

		//	return tcs.Task;
		//}
	}
}