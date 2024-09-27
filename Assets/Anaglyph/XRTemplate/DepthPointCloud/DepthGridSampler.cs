//using Meta.XR.Depth;
//using System;
//using System.Runtime.InteropServices;
//using UnityEngine;

//namespace Anaglyph.XRTemplate.PointCloud
//{
//    public class DepthGridSampler : SingletonBehavior<DepthGridSampler>
//    {
//		private const Camera.MonoOrStereoscopicEye Left = Camera.MonoOrStereoscopicEye.Left;

//		private static readonly int ResultsId = Shader.PropertyToID("Results");
//		private static readonly int EnvDepthTextureCSId = Shader.PropertyToID("EnvDepthTextureCS");
//		private static readonly int EnvDepthTextureSizeId = Shader.PropertyToID("EnvDepthTextureSize");

//		private static readonly int NumSamplesXYId = Shader.PropertyToID("NumSamplesXY");

//		[SerializeField] private ComputeShader computeShader;
//		[SerializeField] private EnvironmentDepthTextureProvider envDepthTextureProvider;

//		private static readonly Vector2Int DefaultEnvironmentDepthTextureSize = new Vector2Int(2000, 2000);

//		public Vector2Int environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;

//		private bool depthEnabled = false;
//		private ComputeBuffer resultsBuffer;

//		/// <summary>
//		/// 
//		/// </summary>
//		/// <param name="ray"></param>
//		/// <param name="results"></param>
//		/// <param name="maxLength"></param>
//		/// <param name="minDotForVertical"></param>
//		/// <returns></returns>
//		public static bool Sample(out DepthCastResult[] results, Vector2Int numSamplesXY, bool handRejection = false) =>
//			Instance.SampleBlocking(out results, numSamplesXY, handRejection);

//		/// <summary>
//		/// 
//		/// </summary>
//		/// <param name="ray"></param>
//		/// <param name="result"></param>
//		/// <param name="maxLength"></param>
//		/// <param name="verticalThreshold"></param>
//		/// <returns></returns>
//		public bool SampleBlocking(out DepthCastResult[] results, Vector2Int numSamplesXY, bool handRejection = false)
//		{
//			results = Array.Empty<DepthCastResult>();

//			UpdateCurrentRenderingState();

//			if (!depthEnabled)
//			{
//				if (Debug.isDebugBuild)
//					Debug.Log("Depth incapable or disabled! Falling back to floorcast...");

//				return false;
//			}

//			// Ignore steps along the ray outside of the camera bounds
//			Matrix4x4 projMat = Camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
//			Matrix4x4 viewMat = Camera.worldToCameraMatrix;
//			Plane[] planes = GeometryUtility.CalculateFrustumPlanes(projMat * viewMat);
//			// Ordering: [0] = Left, [1] = Right, [2] = Down, [3] = Up, [4] = Near, [5] = Far
//			// need to nudge Right plane left a bit
//			//planes[1].distance -= 0.1f;

//			int numDepthTextureSamples = numSamplesXY.x * numSamplesXY.y;

//			int threads = Mathf.CeilToInt(numDepthTextureSamples / 32f);

//			resultsBuffer = GetComputeBuffers(numDepthTextureSamples);

//			computeShader.SetInts(NumSamplesXYId, numSamplesXY.x, numSamplesXY.y);
//			computeShader.SetBuffer(0, ResultsId, resultsBuffer);
//			computeShader.Dispatch(0, threads, 1, 1);

//			results = new DepthCastResult[numDepthTextureSamples];
//			resultsBuffer.GetData(results);

//			return true;
//		}

//		public static Camera Camera { get; private set; }

//		protected override void SingletonAwake()
//		{
//			resultsBuffer?.Release();
//			resultsBuffer = null;

//			if (envDepthTextureProvider == null)
//			{
//				envDepthTextureProvider = FindObjectOfType<EnvironmentDepthTextureProvider>(true);
//			}
//		}

//		private void OnEnable()
//		{
//			Camera = Camera.main;
//		}

//		//private void Update()
//		//{
//		//	UpdateCurrentRenderingState();
//		//}

//		protected override void OnSingletonDestroy()
//		{
//			resultsBuffer?.Release();
//		}

//		private void UpdateCurrentRenderingState()
//		{
//			depthEnabled = Unity.XR.Oculus.Utils.GetEnvironmentDepthSupported() &&
//				envDepthTextureProvider != null &&
//				envDepthTextureProvider.GetEnvironmentDepthEnabled();

//			if (!depthEnabled)
//				return;

//			int depthTextureId = EnvironmentDepthTextureProvider.DepthTextureID;


//			computeShader.SetTextureFromGlobal(0, EnvDepthTextureCSId, depthTextureId);
//			computeShader.SetInts(EnvDepthTextureSizeId, environmentDepthTextureSize.x, environmentDepthTextureSize.y);
//		}

//		private ComputeBuffer GetComputeBuffers(int size)
//		{
//			if (resultsBuffer != null && resultsBuffer.count != size)
//			{
//				resultsBuffer.Release();
//				resultsBuffer = null;
//			}

//			if (resultsBuffer == null)
//			{
//				resultsBuffer = new ComputeBuffer(size, Marshal.SizeOf<DepthCastResult>(),
//					ComputeBufferType.Structured);
//			}

//			return resultsBuffer;
//		}

//		private void OnValidate()
//		{
//			if (environmentDepthTextureSize == default)
//			{
//				environmentDepthTextureSize = DefaultEnvironmentDepthTextureSize;
//			}
//		}
//	}
//}
