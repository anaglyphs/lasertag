using Anaglyph.XRTemplate.DepthKit;
using StrikerLink.ThirdParty.WebSocketSharp;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader shader = null;
		[SerializeField] private float metersPerVoxel = 0.1f;
		[SerializeField] private float dispatchesPerSecond = 30f;

		[SerializeField] private RenderTexture volume;

		private int vWidth => volume.width;
		private int vHeight => volume.height;
		private int vDepth => volume.volumeDepth;
		
		[SerializeField] private float maxEyeDist = 7f;

		private ComputeKernel clearKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel raycastKernel;

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;
		// private ComputeBuffer lastIntegration;

		private void Awake()
		{
			Instance = this;
		}

		private bool setupDone = false;
		private void Start()
		{
			clearKernel = new(shader, "Clear");
			clearKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");
			integrateKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");

			raycastKernel = new(shader, "Raycast");
			raycastKernel.Set("rcVolume", volume);

			shader.SetInts("volumeSize", vWidth, vHeight, vDepth);
			shader.SetFloat(nameof(metersPerVoxel), metersPerVoxel);

			Clear();

			ScanLoop();
		}

		public void Clear()
		{
			clearKernel.DispatchGroups(volume);
		}

		private void OnEnable()
		{
			if (setupDone)
				ScanLoop();
		}

		//private Vector3 VoxelToWorld(Vector3Int indices)
		//{
		//	Vector3 pos = indices;
		//	pos.x -= volume.width / 2;
		//	pos.y -= volume.height / 2;
		//	pos.z -= volume.volumeDepth / 2;

		//	return pos * metersPerVoxel;
		//}

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(1f / dispatchesPerSecond);

				var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
				if (depthTex == null) continue;
				
				integrateKernel.Set(DepthKitDriver.agDepthTex_ID, depthTex);

				if (frustumVolume == null)
					Setup();

				integrateKernel.DispatchGroups(frustumVolume.count, 1, 1);
			}
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

						if (v.magnitude < maxEyeDist)
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

		private const float RaycastScaleFactor = 1000f;

		public bool Raycast(Ray ray, float maxDist, out Vector3 hitPoint)
		{
			hitPoint = ray.origin;

			if (maxDist == 0)
				return false;

			shader.SetVector("rcOrig", ray.origin);
			shader.SetVector("rcDir", ray.direction);
			shader.SetFloat("rcIntScale", RaycastScaleFactor);

			uint lengthInt = (uint)(maxDist * RaycastScaleFactor);

			ComputeBuffer resultBuffer = new ComputeBuffer(1, sizeof(uint));
			resultBuffer.SetData(new uint[] { lengthInt });
			raycastKernel.Set("rcResult", resultBuffer);

			int totalNumSteps = Mathf.RoundToInt(maxDist / metersPerVoxel);

			raycastKernel.DispatchGroups(totalNumSteps, 1, 1);

			uint[] resultData = new uint[1];
			resultBuffer.GetData(resultData);
			resultBuffer.Release();
			uint hitDistInt = resultData[0];

			if (hitDistInt >= lengthInt)
				return false;

			float hitDist = hitDistInt / RaycastScaleFactor;

			if (hitDist >= maxDist)
				return false;

			hitPoint = ray.GetPoint(hitDist);
			return true;
		}
	}
}
