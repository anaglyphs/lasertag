using Meta.XR.EnvironmentDepth;
using Unity.XR.Oculus;
using UnityEngine;

namespace Anaglyph.XRTemplate.Depth
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static readonly int DepthTextureID = Shader.PropertyToID("_EnvironmentDepthTexture");
		public static readonly int ReprojectionMatricesID = Shader.PropertyToID("_EnvironmentDepthReprojectionMatrices");
		public static readonly int ZBufferParamsID = Shader.PropertyToID("_EnvironmentDepthZBufferParams");

		[SerializeField] private EnvironmentDepthManager envDepthTextureProvider;
		private Camera mainCamera;

		public static bool DepthAvailable { get; private set; }

		private void Awake()
		{
			mainCamera = Camera.main;
		}

		private void Update()
		{
			UpdateCurrentRenderingState();
		}

		public void UpdateCurrentRenderingState()
		{
			DepthAvailable = Unity.XR.Oculus.Utils.GetEnvironmentDepthSupported() &&
				envDepthTextureProvider != null &&
				envDepthTextureProvider.IsDepthAvailable;

			if (!DepthAvailable)
				return;

			Shader.SetGlobalTexture("dk_DepthTexture", 
				Shader.GetGlobalTexture(DepthTextureID));

			Matrix4x4[] reproj = Shader.GetGlobalMatrixArray(ReprojectionMatricesID);
			Matrix4x4[] invReproj = new Matrix4x4[2];

			for (int i = 0; i < reproj.Length; i++)
				invReproj[i] = Matrix4x4.Inverse(reproj[i]);

			Shader.SetGlobalMatrixArray("dk_DepthTexReprojMatrices",
				reproj);

			Shader.SetGlobalMatrixArray("dk_InvDepthTexReprojMatrices",
				invReproj);

			Shader.SetGlobalVector("dk_DepthTexZBufferParams",
					Shader.GetGlobalVector(ZBufferParamsID));

			Shader.SetGlobalVector("dk_ZBufferParams",
				Shader.GetGlobalVector("_ZBufferParams"));

			Shader.SetGlobalMatrixArray("dk_StereoMatrixInvVP",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixInvVP"));

			Shader.SetGlobalMatrixArray("dk_StereoMatrixVP",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixVP"));

			Shader.SetGlobalMatrixArray("dk_StereoMatrixV",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixV"));

			Shader.SetGlobalMatrixArray("dk_StereoMatrixInvP",
				Shader.GetGlobalMatrixArray("unity_StereoMatrixInvP"));
		}
	}
}
