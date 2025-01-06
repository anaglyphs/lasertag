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

		[SerializeField] private ComputeShader compute;

		[SerializeField] private int textureSize = 512;
		public int TextureSize => textureSize;

		[SerializeField] private Vector2 depthRange = new Vector2(0.5f, 6f);
		[SerializeField] private Vector2 heightRange = new Vector2(-3f, 0.5f);

		[SerializeField] private float edgeFilterSize = 0.02f;
		[SerializeField] private float gradientCutoff = 0.2f;

		[SerializeField] private float lerpHeight = 0.2f;

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

		private static readonly int _DepthRange = ID(nameof(_DepthRange));
		private static readonly int _HeightRange = ID(nameof(_HeightRange));

		private static readonly int _EdgeFilterSize = ID(nameof(_EdgeFilterSize));
		private static readonly int _GradientCutoff = ID(nameof(_GradientCutoff));

		private static readonly int _LerpHeight = ID(nameof(_LerpHeight));

		private RenderTexture heightMap;
		public RenderTexture Map => heightMap;
		private ComputeBuffer perFrameScanBuffer;

		private ComputeKernel Accumulate;
		private ComputeKernel Apply;
		private ComputeKernel ClearEnvMap;
		private ComputeKernel ClearPerFrame;

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
			heightMap = new(textureSize, textureSize, 0, GraphicsFormat.R16G16_SFloat);
			heightMap.enableRandomWrite = true;

			var size = heightMap.width * heightMap.height;
			perFrameScanBuffer = new(size, sizeof(Int32), ComputeBufferType.Structured);

			Shader.SetGlobalFloat(agEnvSizeMeters, envSize);
			Shader.SetGlobalTexture(agEnvHeightMap, heightMap);

			compute.SetInt(_TexSize, heightMap.width);

			compute.SetFloat(_TexSize, heightMap.width);
			compute.SetInt(_DepthSamples, depthSamples);

			compute.SetVector(_DepthRange, depthRange);
			compute.SetVector(_HeightRange, heightRange);

			compute.SetFloat(_EdgeFilterSize, edgeFilterSize);
			compute.SetFloat(_GradientCutoff, gradientCutoff);

			compute.SetFloat(_LerpHeight, lerpHeight);

			Accumulate = new ComputeKernel(compute, 0);
			Accumulate.Set(_PerFrameScan, perFrameScanBuffer);
			Accumulate.Set(_HeightMap, heightMap);

			Apply = new ComputeKernel(compute, 1);
			Apply.Set(_PerFrameScan, perFrameScanBuffer);
			Apply.Set(_HeightMap, heightMap);

			ClearEnvMap = new ComputeKernel(compute, 2);
			ClearEnvMap.Set(_HeightMap, heightMap);

			ClearEnvMap.Dispatch(textureSize, textureSize);

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
			if (!shouldScanThisUpdate || !DepthKitDriver.DepthAvailable)
				return;

			shouldScanThisUpdate = false;

			int id = DepthKitDriver.agDepthViewInv_ID;
			compute.SetMatrixArray(id, Shader.GetGlobalMatrixArray(id));
			id = DepthKitDriver.agDepthView_ID;
			compute.SetMatrixArray(id, Shader.GetGlobalMatrixArray(id));

			var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);
			Accumulate.Set(DepthKitDriver.agDepthTex_ID, depthTex);
			Accumulate.Dispatch(depthSamples, depthSamples, 1);
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
			ClearEnvMap.Dispatch(textureSize, textureSize);
		}
	}
}
