using System;
using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float voxelSize = 0.1f;
		public float VoxelSize => voxelSize;

		[SerializeField] private float truncationMax = 0.5f;
		[SerializeField] private float truncationMin = -0.1f;

		[SerializeField] private float dispatchesPerSecond = 5f;

		public event Action Integrated = delegate { };

		[SerializeField] private RenderTexture volume;
		public RenderTexture Volume => volume;

		[SerializeField] private RenderTexture occlusionTex;
		public RenderTexture OcclusionTex => occlusionTex;

		public int vWidth => volume.width;
		public int vHeight => volume.height;
		public int vDepth => volume.volumeDepth;

		[SerializeField] private float maxEyeDist = 7f;
		public float MaxEyeDist => maxEyeDist;

		[SerializeField] private float minEyeDist = 1f;
		public float MinEyeDist => minEyeDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel raymarchKernel;
		private ComputeKernel occlusionMarchKernel;

		private int ViewID => DepthKitDriver.agDepthView_ID;
		private int ProjID => DepthKitDriver.agDepthProj_ID;

		private int ViewInvID => DepthKitDriver.agDepthViewInv_ID;
		private int ProjInvID => DepthKitDriver.agDepthProjInv_ID;

		private int DepthTexID => DepthKitDriver.agDepthTex_ID;
		private int NormTexID => DepthKitDriver.agDepthNormTex_ID;

		private readonly int numPlayersID = Shader.PropertyToID("numPlayers");
		private readonly int playerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		private readonly int numRaymarchRequestsID = Shader.PropertyToID("numRaymarchRequests");
		private readonly int raymarchRequestsID = Shader.PropertyToID("raymarchRequests");
		private readonly int raymarchResultsID = Shader.PropertyToID("raymarchResults");
		private readonly int raymarchVolumeID = Shader.PropertyToID("raymarchVolume");

		private readonly int volumeID = Shader.PropertyToID("volume");
		private readonly int volumeSizeID = Shader.PropertyToID("volumeSize");
		private readonly int voxSizeID = Shader.PropertyToID("voxSize");
		private readonly int truncMaxID = Shader.PropertyToID("truncMax");
		private readonly int truncMinID = Shader.PropertyToID("truncMin");

		private readonly int occlusionTexID = Shader.PropertyToID("occlusionTex");
		private readonly int occlusionTexSizeID = Shader.PropertyToID("occlusionTexSize");

		private readonly int camViewID = Shader.PropertyToID("camView");
		private readonly int camProjID = Shader.PropertyToID("camProj");
		private readonly int camInvViewID = Shader.PropertyToID("camInvView");
		private readonly int camInvProjID = Shader.PropertyToID("camInvProj");

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private Vector4[] headPositions = new Vector4[512];

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			clearKernel = new ComputeKernel(shader, "Clear");
			clearKernel.Set(volumeID, volume);

			integrateKernel = new ComputeKernel(shader, "Integrate");
			integrateKernel.Set(volumeID, volume);

			integrateKernel = new ComputeKernel(shader, "Integrate");

			raymarchKernel = new ComputeKernel(shader, "Raymarch");
			raymarchKernel.Set(raymarchVolumeID, volume);

			occlusionMarchKernel = new ComputeKernel(shader, "OcclusionMarch");
			occlusionMarchKernel.Set(raymarchVolumeID, volume);
			occlusionMarchKernel.Set(occlusionTexID, occlusionTex);

			shader.SetInts(volumeSizeID, vWidth, vHeight, vDepth);
			shader.SetFloat(voxSizeID, voxelSize);
			shader.SetFloat(truncMaxID, truncationMax);
			shader.SetFloat(truncMinID, truncationMin);
			shader.SetInts(occlusionTexSizeID, occlusionTex.width, occlusionTex.height);


			Clear();

			ScanLoop();
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
		}

		private void OnEnable()
		{
			if (didStart)
				ScanLoop();
		}

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(1f / dispatchesPerSecond);

				Texture depthTex = Shader.GetGlobalTexture(DepthTexID);
				if (depthTex == null) continue;

				Texture normTex = Shader.GetGlobalTexture(NormTexID);

				if (frustumVolume == null)
					Setup();

				Matrix4x4 view = Shader.GetGlobalMatrixArray(ViewID)[0];
				Matrix4x4 proj = Shader.GetGlobalMatrixArray(ProjID)[0];

				ApplyScan(depthTex, normTex, view, proj);

				Integrated.Invoke();
			}
		}

		public void ApplyScan(Texture depthTex, Texture normTex, Matrix4x4 view, Matrix4x4 proj) //, bool useDepthFrame)
		{
			shader.SetMatrixArray(ViewID, new[] { view, Matrix4x4.zero });
			shader.SetMatrixArray(ProjID, new[] { proj, Matrix4x4.zero });

			shader.SetMatrixArray(ViewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(ProjInvID, new[] { proj.inverse, Matrix4x4.zero });

			for (int i = 0; i < PlayerHeads.Count; i++)
			{
				Vector3 playerHead = PlayerHeads[i].position;
				headPositions[i] = playerHead;
			}

			shader.SetInt(numPlayersID, PlayerHeads.Count);
			shader.SetVectorArray(playerHeadsWorldID, headPositions);

			integrateKernel.Set(DepthTexID, depthTex);
			integrateKernel.Set(NormTexID, normTex);

			integrateKernel.DispatchGroups(frustumVolume.count, 1, 1);
		}

		private void Setup()
		{
			Matrix4x4 depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			//frustum.zNear = 0.2f;
			frustum.zFar = maxEyeDist;

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

					if (v.magnitude > minEyeDist && v.magnitude < maxEyeDist)
						positions.Add(v);
				}
			}

			frustumVolume = new ComputeBuffer(positions.Count, sizeof(float) * 3);
			// lastIntegration = new ComputeBuffer(positions.Count, sizeof())

			frustumVolume.SetData(positions);
			integrateKernel.Set(nameof(frustumVolume), frustumVolume);
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

			shader.SetInt(numRaymarchRequestsID, count);
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

		public async Task ComputeOcclusionTexture(ushort[] results)
		{
			Camera cam = MainXRRig.Instance.camera;

			shader.SetMatrix(camViewID, cam.worldToCameraMatrix);
			shader.SetMatrix(camInvViewID, cam.cameraToWorldMatrix);

			Matrix4x4 projMat = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
			shader.SetMatrix(camProjID, projMat);
			shader.SetMatrix(camInvProjID, projMat.inverse);

			occlusionMarchKernel.DispatchGroups(occlusionTex);
			
			AsyncGPUReadbackRequest request = await AsyncGPUReadback.RequestAsync(occlusionTex);
			
			if (request.hasError) throw new Exception("Readback error");
			
			request.GetData<ushort>().CopyTo(results);
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