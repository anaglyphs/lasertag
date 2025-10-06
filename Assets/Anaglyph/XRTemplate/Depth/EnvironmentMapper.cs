using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float metersPerVoxel = 0.1f;
		[SerializeField] private float dispatchesPerSecond = 5f;

		[SerializeField] private RenderTexture volume;

		private int vWidth => volume.width;
		private int vHeight => volume.height;
		private int vDepth => volume.volumeDepth;
		
		[SerializeField] private float maxEyeDist = 7f;
		public float MaxEyeDist => maxEyeDist;

		[SerializeField] private float minEyeDist = 1f;
		public float MinEyeDist => minEyeDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel raymarchKernel;

		private int viewID => DepthKitDriver.agDepthView_ID;
		private int projID => DepthKitDriver.agDepthProj_ID;

		private int viewInvID => DepthKitDriver.agDepthViewInv_ID;
		private int projInvID => DepthKitDriver.agDepthProjInv_ID;

		private int depthTexID => DepthKitDriver.agDepthTex_ID;
		private int normTexID => DepthKitDriver.agDepthNormTex_ID;

		private int numPlayersID = Shader.PropertyToID("numPlayers");
		private int playerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		private int numRaymarchRequestsID = Shader.PropertyToID("numRaymarchRequests");
		private int raymarchRequestsID = Shader.PropertyToID("raymarchRequests");
		private int raymarchResultsID = Shader.PropertyToID("raymarchResults");

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private Vector4[] headPositions = new Vector4[512];

		private void Awake()
		{
			Instance = this;
		}

		private bool hasStarted = false;
		private void Start()
		{
			clearKernel = new(shader, "Clear");
			clearKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");
			integrateKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");

			raymarchKernel = new(shader, "Raymarch");
			raymarchKernel.Set("raymarchVolume", volume);

			shader.SetInts("volumeSize", vWidth, vHeight, vDepth);
			shader.SetFloat(nameof(metersPerVoxel), metersPerVoxel);

			Clear();

			ScanLoop();

			hasStarted = true;
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
		}

		private void OnEnable()
		{
			if(!hasStarted)
				ScanLoop();
		}

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(1f / dispatchesPerSecond);

				var depthTex = Shader.GetGlobalTexture(depthTexID);
				if (depthTex == null) continue;

				var normTex = Shader.GetGlobalTexture(normTexID);
				
				if (frustumVolume == null)
					Setup();

				Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
				Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];

				ApplyScan(depthTex, normTex, view, proj);
			}
		}

		public void ApplyScan(Texture depthTex, Texture normTex, Matrix4x4 view, Matrix4x4 proj)//, bool useDepthFrame)
		{
			shader.SetMatrixArray(viewID, new[]{ view, Matrix4x4.zero });
			shader.SetMatrixArray(projID, new[]{ proj, Matrix4x4.zero });

			shader.SetMatrixArray(viewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(projInvID, new[] { proj.inverse, Matrix4x4.zero });

			for(int i = 0; i < PlayerHeads.Count; i++)
			{
				Vector3 playerHead = PlayerHeads[i].position;
				headPositions[i] = playerHead;
			}

			shader.SetInt(numPlayersID, PlayerHeads.Count);
			shader.SetVectorArray(playerHeadsWorldID, headPositions);

			integrateKernel.Set(depthTexID, depthTex);
			integrateKernel.Set(normTexID, normTex);

			integrateKernel.DispatchGroups(frustumVolume.count, 1, 1);
		}

		private void Setup()
		{
			var depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			//frustum.zNear = 0.2f;
			frustum.zFar = maxEyeDist;

			List<Vector3> positions = new(200000);

			var f = frustum;
			// slopes 
			float ls = f.left / f.zNear;
			float rs = f.right / f.zNear;
			float ts = f.top / f.zNear;
			float bs = f.bottom / f.zNear;

			for (float z = f.zNear; z < f.zFar; z += metersPerVoxel)
			{
				float xMin = ls * z + metersPerVoxel;
				float xMax = rs * z - metersPerVoxel;

				float yMin = bs * z + metersPerVoxel;
				float yMax = ts * z - metersPerVoxel;

				for (float x = xMin; x < xMax; x += metersPerVoxel)
				{
					for (float y = yMin; y < yMax; y += metersPerVoxel)
					{
						Vector3 v = new Vector3(x, y, -z);

						if (v.magnitude > minEyeDist && v.magnitude < maxEyeDist)
							positions.Add(v);
					}
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
				this.point = ray.origin + ray.direction * distance;
				this.distance = distance;
				this.didHit = distance >= 0;
			}
		}

		private Task<float[]> currentRaymarchBatch = null;

		public async Task<RaymarchResult> RaymarchAsync(Ray ray, float maxDistance)
		{
			RaymarchRequest request = new RaymarchRequest(ray, maxDistance);
			int index = pendingRequests.Count;
			pendingRequests.Add(request);

			if(currentRaymarchBatch == null)
			{
				currentRaymarchBatch = DispatchRaymarches();
			}

			var data = await currentRaymarchBatch;
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
