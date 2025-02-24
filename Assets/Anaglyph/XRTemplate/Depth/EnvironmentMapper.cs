using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using UnityEngine;

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

		private ComputeKernel clearVolumeKernel;
		private ComputeKernel clearUpdatesKernel;
		private ComputeKernel applyUpdatesKernel;
		private ComputeKernel integrateKernel;
		private ComputeKernel raycastKernel;

		private int viewID => DepthKitDriver.agDepthView_ID;
		private int projID => DepthKitDriver.agDepthProj_ID;

		private int viewInvID => DepthKitDriver.agDepthViewInv_ID;
		private int projInvID => DepthKitDriver.agDepthProjInv_ID;

		private int depthTexID => DepthKitDriver.agDepthTex_ID;

		// cached points within viewspace depth frustum 
		// like a 3D lookup table
		private ComputeBuffer frustumVolume;
		private ComputeBuffer volumeUpdates;

		public ComputeBuffer VolumeUpdate => volumeUpdates;

		private void Awake()
		{
			Instance = this;
		}

		private bool hasStarted = false;
		private void Start()
		{
			clearVolumeKernel = new(shader, "Clear");
			clearVolumeKernel.Set(nameof(volume), volume);

			clearUpdatesKernel = new(shader, "ClearVolumeUpdates");
			applyUpdatesKernel = new(shader, "ApplyVolumeUpdates");
			applyUpdatesKernel.Set(nameof(volume), volume);

			integrateKernel = new(shader, "Integrate");
			integrateKernel.Set(nameof(volume), volume);

			raycastKernel = new(shader, "Raycast");
			raycastKernel.Set("rcVolume", volume);

			shader.SetInts("volumeSize", vWidth, vHeight, vDepth);
			shader.SetFloat(nameof(metersPerVoxel), metersPerVoxel);

			Clear();

			ScanLoop();

			hasStarted = true;
		}

		public void Clear()
		{
			clearVolumeKernel.DispatchGroups(volume);
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
				
				if (frustumVolume == null)
					Setup();

				clearUpdatesKernel.DispatchGroups(frustumVolume.count, 1, 1);
				
				integrateKernel.DispatchGroups(frustumVolume.count, 1, 1);
			}
		}

		private void Setup()
		{
			var depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
			FrustumPlanes frustum = depthProj.decomposeProjection;
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

			frustumVolume = new(positions.Count, sizeof(float) * 3);
			frustumVolume.SetData(positions);

			integrateKernel.Set(nameof(frustumVolume), frustumVolume);
			applyUpdatesKernel.Set(nameof(frustumVolume), frustumVolume);

			volumeUpdates = new(positions.Count / 4, sizeof(int));

			clearUpdatesKernel.Set(nameof(volumeUpdates), volumeUpdates);
			applyUpdatesKernel.Set(nameof(volumeUpdates), volumeUpdates);
			integrateKernel.Set(nameof(volumeUpdates), volumeUpdates);
		}

		public void ApplyUpdates(Matrix4x4 view, Matrix4x4 proj, byte[] updates)
		{
			shader.SetMatrixArray(viewID, new[] { view, Matrix4x4.zero });
			shader.SetMatrixArray(projID, new[] { proj, Matrix4x4.zero });

			shader.SetMatrixArray(viewInvID, new[] { view.inverse, Matrix4x4.zero });
			shader.SetMatrixArray(projInvID, new[] { proj.inverse, Matrix4x4.zero });

			volumeUpdates.SetData(updates);
			applyUpdatesKernel.DispatchGroups(frustumVolume.count, 1, 1);
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
