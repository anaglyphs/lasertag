using Anaglyph.XRTemplate.DepthKit;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper : MonoBehaviour
	{
		public static EnvironmentMapper Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;
		[SerializeField] private float metersPerVoxel;

		private static int ID(string str) => Shader.PropertyToID(str);
		private static readonly int volume_ID = ID("volume");
		private static readonly int viewVoxels_ID = ID("viewVoxels");
		private static readonly int volumeSize_ID = ID("volumeSize");
		private static readonly int metersPerVoxel_ID = ID("metersPerVoxel");
		private static readonly int idOffset_ID = ID("idOffset");
		private static readonly int maxEyeDist_ID = ID("maxEyeDist");

		[SerializeField] private RenderTexture volume;
		private Vector3Int volumeSize;

		[SerializeField] private float maxEyeDistance = 7f;

		private ComputeKernel clearKernel;
		private ComputeKernel scanKernel;
		private ComputeKernel raycastKernel;

		private ComputeBuffer viewVoxelsBuffer;

		private bool started = false;
		private void Awake()
		{
			Instance = this;
		}

		private void OnEnable()
		{
			if(started)
				ScanLoop();
		}

		private void Start()
		{
			volumeSize = new(volume.width, volume.height, volume.volumeDepth);

			clearKernel = new(compute, "Clear");
			clearKernel.Set(volume_ID, volume);

			scanKernel = new(compute, "Scan");
			scanKernel.Set(volume_ID, volume);

			raycastKernel = new(compute, "Raycast");
			raycastKernel.Set(rcVolume_ID, volume);

			var v = volumeSize;
			compute.SetInts(volumeSize_ID, v.x, v.y, v.z);
			compute.SetFloat(maxEyeDist_ID, maxEyeDistance);
			compute.SetFloat(metersPerVoxel_ID, metersPerVoxel);

			clearKernel.DispatchGroups(volume);

			ScanLoop();
		}

		private Vector3 VoxelToWorld(Vector3Int indices)
		{
			Vector3 pos = indices;
			pos.x -= volume.width / 2;
			pos.y -= volume.height / 2;
			pos.z -= volume.volumeDepth / 2;

			return pos * metersPerVoxel;
		}

		private List<Vector3> voxelViewPositions;

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.NextFrameAsync();

				var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
				if (depthTex == null) continue;
				scanKernel.Set(DepthKitDriver.agDepthTex_ID, depthTex);

				// calculate 
				if (voxelViewPositions == null)
				{
					var depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
					FrustumPlanes frustum = depthProj.decomposeProjection;
					frustum.zFar = maxEyeDistance;

					voxelViewPositions = new();

					var f = frustum;
					// slopes 
					float ls = f.left / f.zNear;
					float rs = f.right / f.zNear;
					float ts = f.top / f.zNear;
					float bs = f.bottom / f.zNear;

					for (float z = f.zNear; z < f.zFar; z += metersPerVoxel)
					{
						float xMin = ls * z;
						float xMax = rs * z;

						float yMin = bs * z;
						float yMax = ts * z;

						for (float x = xMin; x < xMax; x += metersPerVoxel)
						{
							for (float y = yMin; y < yMax; y += metersPerVoxel)
							{
								Vector3 v = new Vector3(x, y, -z);
								voxelViewPositions.Add(v);
							}
						}
					}

					viewVoxelsBuffer = new ComputeBuffer(voxelViewPositions.Count, sizeof(float) * 3);

					viewVoxelsBuffer.SetData(voxelViewPositions);
					scanKernel.Set(viewVoxels_ID, viewVoxelsBuffer);
				}

				scanKernel.DispatchGroups(voxelViewPositions.Count, 1, 1);
			}
		}

		private void OnDestroy()
		{
			viewVoxelsBuffer?.Release();
		}

		private static readonly int rcOrig = ID("rcOrig");
		private static readonly int rcDir_ID = ID("rcDir");
		private static readonly int rcIntScale_ID = ID("rcIntScale");
		private static readonly int rcResult_ID = ID("rcResult");
		private static readonly int rcVolume_ID = ID("rcVolume");

		private const float RaycastScaleFactor = 1000f;

		public bool Raycast(Ray ray, float maxDist, out Vector3 hitPoint)
		{
			hitPoint = ray.origin;

			if (maxDist == 0)
				return false;

			compute.SetVector(rcOrig, ray.origin);
			compute.SetVector(rcDir_ID, ray.direction);
			compute.SetFloat(rcIntScale_ID, RaycastScaleFactor);

			uint lengthInt = (uint)(maxDist * RaycastScaleFactor);

			ComputeBuffer result = new ComputeBuffer(1, sizeof(uint));
			result.SetData(new uint[] { lengthInt });
			raycastKernel.Set(rcResult_ID, result);

			int totalNumSteps = Mathf.RoundToInt(maxDist / metersPerVoxel);

			raycastKernel.DispatchGroups(totalNumSteps, 1, 1);

			uint[] resultData = new uint[1];
			result.GetData(resultData);
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
