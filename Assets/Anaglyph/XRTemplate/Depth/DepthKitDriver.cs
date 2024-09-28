using Meta.XR.EnvironmentDepth;
using Unity.XR.Oculus;
using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		Matrix4x4[] dk_Proj = new Matrix4x4[2];
		Matrix4x4[] dk_InvProj = new Matrix4x4[2];

		Matrix4x4[] dk_View = new Matrix4x4[2];
		Matrix4x4[] dk_InvView = new Matrix4x4[2];

		public static readonly int Meta_EnvironmentDepthTexture_ID = Shader.PropertyToID("_EnvironmentDepthTexture");
		public static readonly int dk_DepthTexture_ID = Shader.PropertyToID("dk_DepthTexture");
		public static readonly int dk_NormalTexture_ID = Shader.PropertyToID("dk_NormalTexture");

		public static readonly int dk_Proj_ID = Shader.PropertyToID(nameof(dk_Proj));
		public static readonly int dk_InvProj_ID = Shader.PropertyToID(nameof(dk_InvProj));

		public static readonly int dk_View_ID = Shader.PropertyToID(nameof(dk_View));
		public static readonly int dk_InvView_ID = Shader.PropertyToID(nameof(dk_InvView));

		[SerializeField] private EnvironmentDepthManager envDepthTextureProvider;
		//[SerializeField] private Material normalShader;

		//private RenderTexture normalTexture;

		public Transform trackingSpace;
		public static bool DepthAvailable { get; private set; }

		private void Update()
		{
			UpdateCurrentRenderingState();
		}

		public void UpdateCurrentRenderingState()
		{
			DepthAvailable = Utils.GetEnvironmentDepthSupported() &&
				envDepthTextureProvider != null &&
				envDepthTextureProvider.IsDepthAvailable;

			if (!DepthAvailable)
				return;

			//if(normalTexture == null)
			//{
			//	normalTexture = new RenderTexture()
			//}

			Shader.SetGlobalTexture(dk_DepthTexture_ID, 
				Shader.GetGlobalTexture(Meta_EnvironmentDepthTexture_ID));

			for (int i = 0; i < dk_Proj.Length; i++)
			{
				var desc = Utils.GetEnvironmentDepthFrameDesc(i);

				dk_Proj[i] = CalculateDepthProjMatrix(desc);
				dk_InvProj[i] = Matrix4x4.Inverse(dk_Proj[i]);

				dk_View[i] = trackingSpace.localToWorldMatrix * CalculateDepthViewMatrix(desc);
				dk_InvView[i] = Matrix4x4.Inverse(dk_View[i]);
			}

			Shader.SetGlobalMatrixArray(nameof(dk_Proj), dk_Proj);
			Shader.SetGlobalMatrixArray(nameof(dk_InvProj), dk_InvProj);
			Shader.SetGlobalMatrixArray(nameof(dk_View), dk_View);
			Shader.SetGlobalMatrixArray(nameof(dk_InvView), dk_InvView);

			{
				//Shader.SetGlobalVector("dk_DepthTexZBufferParams",
				//		Shader.GetGlobalVector(ZBufferParamsID));

				//Shader.SetGlobalVector("dk_ZBufferParams",
				//	Shader.GetGlobalVector("_ZBufferParams"));

				//Shader.SetGlobalMatrixArray("dk_StereoMatrixInvVP",
				//	Shader.GetGlobalMatrixArray("unity_StereoMatrixInvVP"));

				//Shader.SetGlobalMatrixArray("dk_StereoMatrixVP",
				//	Shader.GetGlobalMatrixArray("unity_StereoMatrixVP"));

				//Shader.SetGlobalMatrixArray("dk_StereoMatrixV",
				//	Shader.GetGlobalMatrixArray("unity_StereoMatrixV"));

				//Shader.SetGlobalMatrixArray("dk_StereoMatrixInvP",
				//	Shader.GetGlobalMatrixArray("unity_StereoMatrixInvP"));
			}
		}

		private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

		private static Matrix4x4 CalculateDepthProjMatrix(Utils.EnvironmentDepthFrameDesc frameDesc)
		{
			float left = frameDesc.fovLeftAngle;
			float right = frameDesc.fovRightAngle;
			float bottom = frameDesc.fovDownAngle;
			float top = frameDesc.fovTopAngle;
			float near = frameDesc.nearZ;
			float far = frameDesc.farZ;

			float x = 2.0F / (right + left);
			float y = 2.0F / (top + bottom);
			float a = (right - left) / (right + left);
			float b = (top - bottom) / (top + bottom);
			float c;
			float d;
			if (float.IsInfinity(far))
			{
				c = -1.0F;
				d = -2.0f * near;
			}
			else
			{
				c = -(far + near) / (far - near);
				d = -(2.0F * far * near) / (far - near);
			}
			float e = -1.0F;
			Matrix4x4 m = new Matrix4x4
			{
				m00 = x,
				m01 = 0,
				m02 = a,
				m03 = 0,
				m10 = 0,
				m11 = y,
				m12 = b,
				m13 = 0,
				m20 = 0,
				m21 = 0,
				m22 = c,
				m23 = d,
				m30 = 0,
				m31 = 0,
				m32 = e,
				m33 = 0

			};

			return m;
		}

		private static Matrix4x4 CalculateDepthViewMatrix(Utils.EnvironmentDepthFrameDesc frameDesc)
		{
			var createRotation = frameDesc.createPoseRotation;
			var depthOrientation = new Quaternion(
				createRotation.x,
				createRotation.y,
				createRotation.z,
				createRotation.w
			);

			var viewMatrix = Matrix4x4.TRS(frameDesc.createPoseLocation, depthOrientation,
				_scalingVector3).inverse;

			return viewMatrix;
		}
	}
}
