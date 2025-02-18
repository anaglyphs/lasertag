using Anaglyph.XRTemplate.DepthKit;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentTSDFMapper : MonoBehaviour
	{
		public static EnvironmentTSDFMapper Instance { get; private set; }

		[SerializeField] private ComputeShader compute = null;
		[SerializeField] private float metersPerVoxel;

		private static int ID(string str) => Shader.PropertyToID(str);
		private static readonly int _Width = ID(nameof(_Width));
		private static readonly int _Height = ID(nameof(_Height));
		private static readonly int _Depth = ID(nameof(_Depth));
		private static readonly int _MetersPerVoxel = ID(nameof(_MetersPerVoxel));
		private static readonly int _Volume = ID(nameof(_Volume));
		private static readonly int _IndexOffset = ID(nameof(_IndexOffset));

		[SerializeField] private RenderTexture volume;

		[SerializeField] private int chunkSize = 128;
		[SerializeField] private float maxZDist = 7f;

		private ComputeKernel clearKernel;
		private ComputeKernel scanKernel;
		private ComputeKernel raycastKernel;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			clearKernel = new(compute, "Clear");
			clearKernel.Set(_Volume, volume);
			clearKernel.DispatchGroups(volume);

			scanKernel = new(compute, "Scan"); 
			scanKernel.Set(_Volume, volume);

			raycastKernel = new(compute, "Raycast");
			raycastKernel.Set(_Volume, volume);

			compute.SetInt(_Width, volume.width);
			compute.SetInt(_Height, volume.height);
			compute.SetInt(_Depth, volume.volumeDepth);

			compute.SetFloat(_MetersPerVoxel, metersPerVoxel);

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

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(0.5f);

				var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
				
				if (depthTex == null)
					return;

				scanKernel.Set(DepthKitDriver.agDepthTex_ID, depthTex);
				var depthProj = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthProj_ID)[0];
				var depthView = Shader.GetGlobalMatrixArray(DepthKitDriver.agDepthView_ID)[0];
				var p = depthProj.decomposeProjection;
				p.zFar = maxZDist;
				depthProj = Matrix4x4.Frustum(p);
				Plane[] planes = new Plane[6];
				GeometryUtility.CalculateFrustumPlanes(depthProj * depthView, planes);

				Vector3Int chunks = new(volume.width, volume.height, volume.volumeDepth);
				chunks /= chunkSize;

				Vector3Int chunkSizeHalf = Vector3Int.one * (chunkSize / 2);

				for (int x = 0; x < chunks.x; x++)
				{
					int xc = x * chunkSize;

					for(int y = 0; y < chunks.y; y++)
					{
						int yc = y * chunkSize;

						for (int z = 0; z < chunks.z; z++)
						{
							int zc = z * chunkSize;

							Vector3Int centerInt = new(xc, yc, zc);
							centerInt += chunkSizeHalf;

							Vector3 center = VoxelToWorld(centerInt);
							Vector3 size = Vector3.one * chunkSize * metersPerVoxel;
							Bounds chunkBounds = new Bounds(center, size);

							if (GeometryUtility.TestPlanesAABB(planes, chunkBounds))
							{
								Vector3Int o = new Vector3Int(x, y, z) * chunkSize; 
								compute.SetInts(_IndexOffset, o.x, o.y, o.z);
								scanKernel.DispatchGroups(chunkSize, chunkSize, chunkSize);
							}
						}
					}
				}
			}
		}

		private static readonly int raycastOrigin = ID(nameof(raycastOrigin));
		private static readonly int raycastStep = ID(nameof(raycastStep));
		private static readonly int hitIndex = ID(nameof(hitIndex));

		public bool Raycast(Ray ray, float length, out Vector3 hitPoint)
		{
			hitPoint = ray.origin;

			if (length == 0)
				return false;

			compute.SetVector(raycastOrigin, ray.origin);
			Vector3 stepVector = ray.direction * metersPerVoxel / 2;
			compute.SetVector(raycastStep, stepVector);
			ComputeBuffer stepHitNumber = new ComputeBuffer(1, sizeof(uint));

			int totalNumSteps = Mathf.RoundToInt(length / metersPerVoxel) * 2;

			stepHitNumber.SetData(new uint[] { (uint)totalNumSteps });
			raycastKernel.Set(hitIndex, stepHitNumber);

			raycastKernel.Dispatch(totalNumSteps, 1, 1);

			uint[] gpuReadback = new uint[1];
			stepHitNumber.GetData(gpuReadback);
			uint stepHitIndex = gpuReadback[0];

			if (stepHitIndex == totalNumSteps)
				return false;

			hitPoint = ray.origin + stepVector * stepHitIndex;
			return true;
		}
	}
}
