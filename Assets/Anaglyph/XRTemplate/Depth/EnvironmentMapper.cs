using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Unity.Mathematics;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float voxSize = 0.1f;
		public float VoxSize => voxSize;

		[SerializeField] private float frequency = 5f;

		[SerializeField] private RenderTexture volume;
		public RenderTexture Volume => volume;

		private int vWidth => volume.width;
		private int vHeight => volume.height;
		private int vDepth => volume.volumeDepth;

		public int3 VolDimensions { get; private set; }

		[SerializeField] private float maxDist = 7f;
		public float MaxDist => maxDist;

		[SerializeField] private float minDist = 1f;
		public float MinDist => minDist;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;
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

		private static readonly int volumeID = ID("volume");
		private static readonly int volumeSizeID = ID("volumeSize");
		private static readonly int metersPerVoxelID = ID("metersPerVoxel");
		private static readonly int frustumVolumeID = ID("frustumVolume");

		private static readonly int numPlayersID = ID("numPlayers");
		private static readonly int playerHeadsWorldID = ID("playerHeadsWorld");

		private static readonly int raymarchVolumeID = ID("raymarchVolume");
		private static readonly int numRaymarchRequestsID = ID("numRaymarchRequests");
		private static readonly int raymarchRequestsID = ID("raymarchRequests");
		private static readonly int raymarchResultsID = ID("raymarchResults");

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;

		public List<Transform> PlayerHeads = new();
		private readonly Vector4[] headPositions = new Vector4[512];

		private float lastUpdateTime = 0;

		private void Awake()
		{
			Instance = this;
			VolDimensions = new int3(vWidth, vHeight, vDepth);
		}

		private void Start()
		{
			clearKernel = new ComputeKernel(shader, "Clear");
			clearKernel.Set(volumeID, volume);

			integrateKernel = new ComputeKernel(shader, "Integrate");
			integrateKernel.Set(volumeID, volume);

			raymarchKernel = new ComputeKernel(shader, "Raymarch");
			raymarchKernel.Set(raymarchVolumeID, volume);

			shader.SetInts(volumeSizeID, vWidth, vHeight, vDepth);
			shader.SetFloat(metersPerVoxelID, voxSize);

			Clear();

			DepthKitDriver.Instance.Updated += OnDepthUpdated;
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
		}

		private void OnEnable()
		{
			if (!didStart)
				DepthKitDriver.Instance.Updated += OnDepthUpdated;
		}

		private void OnDisable()
		{
			DepthKitDriver.Instance.Updated -= OnDepthUpdated;
		}

		private void OnDepthUpdated()
		{
			float wait = 1f / frequency;
			if (Time.time < lastUpdateTime + wait) return;

			lastUpdateTime = Time.time;

			Texture depthTex = Shader.GetGlobalTexture(depthTexID);
			if (depthTex == null) return;

			Texture normTex = Shader.GetGlobalTexture(normTexID);

			if (frustumVolume == null)
			{
				Setup();
				return;
			}

			Matrix4x4 view = Shader.GetGlobalMatrixArray(viewID)[0];
			Matrix4x4 proj = Shader.GetGlobalMatrixArray(projID)[0];

			ApplyScan(depthTex, normTex, view, proj);
		}

		public void ApplyScan(Texture depthTex, Texture normTex, Matrix4x4 view, Matrix4x4 proj) //, bool useDepthFrame)
		{
			shader.SetMatrixArray(viewID, new[] { view, Matrix4x4.zero });
			shader.SetMatrixArray(projID, new[] { proj, Matrix4x4.zero });

			shader.SetMatrixArray(viewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(projInvID, new[] { proj.inverse, Matrix4x4.zero });

			for (int i = 0; i < PlayerHeads.Count; i++)
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
			if (!DepthKitDriver.DepthAvailable)
				return;

			Matrix4x4 depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.projID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
			//frustum.zNear = 0.2f;
			frustum.zFar = maxDist;

			List<Vector3> positions = new(200000);

			FrustumPlanes f = frustum;
			// slopes 
			float ls = f.left / f.zNear;
			float rs = f.right / f.zNear;
			float ts = f.top / f.zNear;
			float bs = f.bottom / f.zNear;

			for (float z = f.zNear; z < f.zFar; z += voxSize)
			{
				float xMin = ls * z + voxSize;
				float xMax = rs * z - voxSize;

				float yMin = bs * z + voxSize;
				float yMax = ts * z - voxSize;

				for (float x = xMin; x < xMax; x += voxSize)
				for (float y = yMin; y < yMax; y += voxSize)
				{
					Vector3 v = new(x, y, -z);

					if (v.magnitude > minDist && v.magnitude < maxDist)
						positions.Add(v);
				}
			}

			if (positions.Count == 0)
				return;

			frustumVolume = new ComputeBuffer(positions.Count, sizeof(float) * 3);
			// lastIntegration = new ComputeBuffer(positions.Count, sizeof())

			frustumVolume.SetData(positions);
			integrateKernel.Set(frustumVolumeID, frustumVolume);
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