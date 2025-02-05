using Anaglyph.XRTemplate.DepthKit;
using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(-10)]
	public class EnvironmentMapper : SingletonBehavior<EnvironmentMapper>
	{
		public const int PER_FRAME_UNWRITTEN = 0;

		public static Action<NativeArray<int>> OnScan = delegate { };
		public static Action OnApply = delegate { };

		[SerializeField] private ComputeShader compute = null;

		[SerializeField] private int textureSize = 512;
		public int TextureSize => textureSize;

		[SerializeField] private float envSize = 50;
		public float EnvironmentSize => envSize;

		[SerializeField] private int depthSamples = 128;

		[SerializeField] private float scanFrequency = 15f;

		private static int ID(string str) => Shader.PropertyToID(str);

		private static readonly int agEnvSizeMeters = ID(nameof(agEnvSizeMeters));
		private static readonly int agEnvHeightMap = ID(nameof(agEnvHeightMap));

		private static readonly int _PerFrameScan = ID(nameof(_PerFrameScan));
		private static readonly int _HeightMap = ID(nameof(_HeightMap));
		private static readonly int _TexSize = ID(nameof(_TexSize));
		
		private static readonly int _DepthSamples = ID(nameof(_DepthSamples));

		private RenderTexture heightMap;
		public RenderTexture Map => heightMap;
		private ComputeBuffer perFrameScanBuffer;

		private ComputeKernel Scan;
		private ComputeKernel Apply;
		private ComputeKernel Clear;
		private ComputeKernel RaycastKernel;

		private Transform cameraTransform;

		protected override void SingletonAwake()
		{
			
		}

		protected override void OnSingletonDestroy()
		{
			ClearMap();

			OnScan = delegate { };
			OnApply = delegate { };
		}

		private void Start()
		{
			heightMap = new(textureSize, textureSize, 0, GraphicsFormat.R16_SFloat);
			heightMap.enableRandomWrite = true;

			var size = heightMap.width * heightMap.height;
			perFrameScanBuffer = new(size, sizeof(Int32), ComputeBufferType.Structured);

			Shader.SetGlobalFloat(agEnvSizeMeters, envSize);
			Shader.SetGlobalTexture(agEnvHeightMap, heightMap);

			compute.SetInt(_TexSize, heightMap.width);

			compute.SetFloat(_TexSize, heightMap.width);
			compute.SetInt(_DepthSamples, depthSamples);

			Scan = new ComputeKernel(compute, nameof(Scan));
			Scan.Set(_PerFrameScan, perFrameScanBuffer);
			Scan.Set(_HeightMap, heightMap);

			Apply = new ComputeKernel(compute, nameof(Apply));
			Apply.Set(_PerFrameScan, perFrameScanBuffer);
			Apply.Set(_HeightMap, heightMap);

			Clear = new ComputeKernel(compute, nameof(Clear));
			Clear.Set(_HeightMap, heightMap);
			Clear.Set(_PerFrameScan, perFrameScanBuffer);

			RaycastKernel = new(compute, "Raycast");
			RaycastKernel.Set(agEnvHeightMap, heightMap);

			Clear.Dispatch(textureSize, textureSize);

			cameraTransform = Camera.main.transform;

			StartCoroutine(ScanTimer());
		}

		private IEnumerator ScanTimer()
		{
            while (true)
            {
				yield return new WaitForSeconds(1f / scanFrequency);
				shouldScanThisUpdate = true;
            }
        }

		bool shouldScanThisUpdate = true;
		private void LateUpdate()
		{
			RaycastKernel.Set(DepthKitDriver.agDepthTex_ID, heightMap);

			if (!shouldScanThisUpdate || !DepthKitDriver.DepthAvailable)
				return;

			float camY = cameraTransform.position.y;

			if (-100 > camY || camY > 100)
				return;

			shouldScanThisUpdate = false;

			int id = DepthKitDriver.agDepthViewInv_ID;
			compute.SetMatrixArray(id, Shader.GetGlobalMatrixArray(id));
			id = DepthKitDriver.agDepthView_ID;
			compute.SetMatrixArray(id, Shader.GetGlobalMatrixArray(id));

			var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
			Scan.Set(DepthKitDriver.agDepthTex_ID, depthTex);
			Scan.Dispatch(depthSamples, depthSamples, 1);
			var dataRequest = AsyncGPUReadback.Request(perFrameScanBuffer);

			Apply.Dispatch(textureSize, textureSize);

			StartCoroutine(WaitForGPUData());
			IEnumerator WaitForGPUData()
			{
				while (!dataRequest.done) yield return null;

				if (!dataRequest.hasError)
					OnScan.Invoke(dataRequest.GetData<int>());
			}
		}

		public void ApplyData(NativeArray<int> data)
		{
			perFrameScanBuffer.SetData(data);
			Apply.Dispatch(textureSize, textureSize);
		}

		public void ClearMap()
		{
			Clear.Dispatch(textureSize, textureSize);
		}

		private static readonly int raycastOrigin = ID(nameof(raycastOrigin));
		private static readonly int raycastStep = ID(nameof(raycastStep));
		private static readonly int hitIndex = ID(nameof(hitIndex));

		public bool Raycast(Ray ray, out Vector3 hitPoint, float maxDistance = 10f)
		{
			hitPoint = ray.origin;
			compute.SetVector(raycastOrigin, ray.origin);

			float metersPerPixel = (envSize / textureSize);
			int totalNumSteps = Mathf.CeilToInt(maxDistance / metersPerPixel);

			if (totalNumSteps <= 0)
				return false;

			Vector3 stepVector = ray.direction * metersPerPixel;
			compute.SetVector(raycastStep, stepVector);

			ComputeBuffer stepHitNumber = new ComputeBuffer(1, sizeof(uint));
			stepHitNumber.SetData(new uint[] { (uint)totalNumSteps });
			RaycastKernel.Set(hitIndex, stepHitNumber);

			RaycastKernel.Dispatch(totalNumSteps, 1, 1);

			uint[] gpuReadback = new uint[1];
			stepHitNumber.GetData(gpuReadback);
			uint stepHitIndex = gpuReadback[0];

			if (stepHitIndex == totalNumSteps)
				return false;

			hitPoint = ray.origin + stepVector * stepHitIndex;
			return true;
		}
	}

	public static class EnvironmentMap
	{
		public static bool Raycast(Ray ray, out Vector3 point, float maxDistance = 10f) =>
			EnvironmentMapper.Instance.Raycast(ray, out point, maxDistance);
	}
}
