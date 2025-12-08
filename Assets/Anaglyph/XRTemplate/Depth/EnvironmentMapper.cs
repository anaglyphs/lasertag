using System;
using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

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

		public int VWidth => volume.width;
		public int VHeight => volume.height;
		public int VDepth => volume.volumeDepth;

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

		private static readonly int NumPlayersID = Shader.PropertyToID("numPlayers");
		private static readonly int PlayerHeadsWorldID = Shader.PropertyToID("playerHeadsWorld");

		private static readonly int NumRaymarchRequestsID = Shader.PropertyToID("numRaymarchRequests");
		private static readonly int RaymarchRequestsID = Shader.PropertyToID("raymarchRequests");
		private static readonly int RaymarchResultsID = Shader.PropertyToID("raymarchResults");
		private static readonly int RaymarchVolumeID = Shader.PropertyToID("raymarchVolume");

		private static readonly int VolumeID = Shader.PropertyToID("volume");
		private static readonly int VolumeSizeID = Shader.PropertyToID("volumeSize");
		private static readonly int VoxSizeID = Shader.PropertyToID("voxSize");
		private static readonly int TruncMaxID = Shader.PropertyToID("truncMax");
		private static readonly int TruncMinID = Shader.PropertyToID("truncMin");

		private static readonly int OcclusionTexID = Shader.PropertyToID("agOcclusionTex");
		private static readonly int OcclusionTexSizeID = Shader.PropertyToID("occlusionTexSize");

		// private static readonly int camViewID = Shader.PropertyToID("camView");
		// private static readonly int camProjID = Shader.PropertyToID("camProj");
		// private static readonly int camInvViewID = Shader.PropertyToID("camInvView");
		// private static readonly int camInvProjID = Shader.PropertyToID("camInvProj");

		// cached points within viewspace depth frustum for compute dispatch
		private ComputeBuffer frustumVolume;

		public List<Transform> playerHeads = new();
		private readonly Vector4[] headPositions = new Vector4[512];

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			clearKernel = new ComputeKernel(shader, "Clear");
			clearKernel.Set(VolumeID, volume);

			integrateKernel = new ComputeKernel(shader, "Integrate");
			integrateKernel.Set(VolumeID, volume);

			integrateKernel = new ComputeKernel(shader, "Integrate");

			raymarchKernel = new ComputeKernel(shader, "Raymarch");
			raymarchKernel.Set(RaymarchVolumeID, volume);

			occlusionMarchKernel = new ComputeKernel(shader, "OcclusionMarch");
			occlusionMarchKernel.Set(RaymarchVolumeID, volume);
			occlusionMarchKernel.Set(OcclusionTexID, occlusionTex);

			shader.SetInts(VolumeSizeID, VWidth, VHeight, VDepth);
			shader.SetFloat(VoxSizeID, voxelSize);
			shader.SetFloat(TruncMaxID, truncationMax);
			shader.SetFloat(TruncMinID, truncationMin);
			shader.SetInts(OcclusionTexSizeID, occlusionTex.width, occlusionTex.height);

			Clear();

			ScanLoop();
			//OcclusionTextureLoop();
		}
		
		private void OnEnable()
		{
			if (didStart)
				ScanLoop();
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
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

			for (int i = 0; i < playerHeads.Count; i++)
			{
				Vector3 playerHead = playerHeads[i].position;
				headPositions[i] = playerHead;
			}

			shader.SetInt(NumPlayersID, playerHeads.Count);
			shader.SetVectorArray(PlayerHeadsWorldID, headPositions);

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
			public Ray Ray;
			public Vector3 Point;
			public readonly float Distance;
			public readonly bool DidHit;

			public RaymarchResult(Ray ray, float distance)
			{
				this.Ray = ray;
				Point = ray.origin + ray.direction * distance;
				this.Distance = distance;
				DidHit = distance >= 0;
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

			shader.SetInt(NumRaymarchRequestsID, count);
			raymarchKernel.Set(RaymarchRequestsID, requestsBuffer);
			raymarchKernel.Set(RaymarchResultsID, resultBuffer);

			raymarchKernel.DispatchGroups(count, 1, 1);

			float[] results = new float[count];
			resultBuffer.GetData(results);

			requestsBuffer.Dispose();
			resultBuffer.Dispose();

			return results;
		}

		// private Matrix4x4[] camViewMats = new Matrix4x4[2];
		// private Matrix4x4[] camInvViewMats = new Matrix4x4[2];
		// private Matrix4x4[] camProjMats = new Matrix4x4[2];
		// private Matrix4x4[] camInvProjMats = new Matrix4x4[2];
		//
		// public void ComputeOcclusionTexture()
		// {
		// 	Camera cam = MainXRRig.Instance.camera;
		//
		// 	for (int i = 0; i < 2; i++)
		// 	{
		// 		Camera.StereoscopicEye eye = (Camera.StereoscopicEye)i;
		// 		Matrix4x4 viewMat = cam.GetStereoViewMatrix(eye);
		// 		camViewMats[i] = viewMat;
		// 		camInvViewMats[i] = viewMat.inverse;
		//
		// 		Matrix4x4 projMat = cam.GetStereoProjectionMatrix(eye);
		// 		Matrix4x4 projMatGL = GL.GetGPUProjectionMatrix(projMat, false);
		// 		camProjMats[i] = projMatGL;
		// 		camInvProjMats[i] = projMatGL.inverse;
		// 	}
		//
		// 	shader.SetMatrixArray(camViewID, camViewMats);
		// 	shader.SetMatrixArray(camInvViewID, camInvViewMats);
		//
		// 	shader.SetMatrixArray(camProjID, camProjMats);
		// 	shader.SetMatrixArray(camInvProjID, camInvProjMats);
		//
		// 	occlusionMarchKernel.DispatchGroups(occlusionTex);
		//
		// 	Shader.SetGlobalTexture(occlusionTexID, occlusionTex);
		// }
	}
}