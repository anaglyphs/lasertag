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

		[SerializeField] private RenderTexture volume;

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

		private async void ScanLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(0.5f);

				var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);

				if (depthTex == null)
					return;

				scanKernel.Set(DepthKitDriver.agDepthTex_ID, depthTex);
				scanKernel.DispatchGroups(volume);
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
