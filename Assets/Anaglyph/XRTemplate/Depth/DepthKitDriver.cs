using Meta.XR.Depth;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	[DefaultExecutionOrder(30000)]
    public class DepthKitDriver : MonoBehaviour
    {
		[SerializeField] private EnvironmentDepthTextureProvider envDepthTextureProvider;
		private Camera mainCamera;

		public static bool DepthAvailable { get; private set; }

		private void Awake()
		{
			mainCamera = Camera.main;
		}

		private void LateUpdate()
		{
			UpdateCurrentRenderingState();
		}

		public void UpdateCurrentRenderingState()
		{
			DepthAvailable = Unity.XR.Oculus.Utils.GetEnvironmentDepthSupported() &&
				envDepthTextureProvider != null &&
				envDepthTextureProvider.GetEnvironmentDepthEnabled();

			if (!DepthAvailable)
				return;

			Shader.SetGlobalTexture("DepthTextureDK", 
				Shader.GetGlobalTexture(EnvironmentDepthTextureProvider.DepthTextureID));

			Shader.SetGlobalMatrixArray("DepthTex3DOFMatricesDK", 
				Shader.GetGlobalMatrixArray(EnvironmentDepthTextureProvider.Reprojection3DOFMatricesID));

			Shader.SetGlobalMatrixArray("DepthTexReprojMatricesDK",
				Shader.GetGlobalMatrixArray(EnvironmentDepthTextureProvider.ReprojectionMatricesID));

			Shader.SetGlobalVector("DepthTexZBufferParamsDK",
					Shader.GetGlobalVector(EnvironmentDepthTextureProvider.ZBufferParamsID));

			Shader.SetGlobalVector("ZBufferParams",
				Shader.GetGlobalVector("_ZBufferParams"));

			Shader.SetGlobalMatrixArray("StereoMatrixInvVP", 
				Shader.GetGlobalMatrixArray("unity_StereoMatrixInvVP"));

			Shader.SetGlobalMatrixArray("StereoMatrixVP",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixVP"));

			Shader.SetGlobalMatrixArray("StereoMatrixV",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixV"));

			Shader.SetGlobalMatrixArray("StereoMatrixInvP",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixInvP"));
		}
	}
}
