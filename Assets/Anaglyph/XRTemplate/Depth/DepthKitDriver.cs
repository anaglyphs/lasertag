using Meta.XR.EnvironmentDepth;
using System.Threading;
using Unity.XR.Oculus;
using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		Matrix4x4[] agDepthProj = new Matrix4x4[2];
		Matrix4x4[] agDepthProjInv = new Matrix4x4[2];

		Matrix4x4[] agDepthView = new Matrix4x4[2];
		Matrix4x4[] agDepthViewInv = new Matrix4x4[2];

		public static readonly int Meta_EnvironmentDepthTexture_ID = Shader.PropertyToID("_EnvironmentDepthTexture");

		public static readonly int agDepthTexSize_ID = Shader.PropertyToID("agDepthTexSize");
		public static readonly int agDepthTex_ID = Shader.PropertyToID("agDepthTex");
		public static readonly int agDepthNormTex_ID = Shader.PropertyToID("agDepthNormalTex");
		public static readonly int agDepthNormalTexRW_ID = Shader.PropertyToID("agDepthNormalTexRW");

		public static readonly int agDepthProj_ID = Shader.PropertyToID(nameof(agDepthProj));
		public static readonly int agDepthProjInv_ID = Shader.PropertyToID(nameof(agDepthProjInv));

		public static readonly int agDepthView_ID = Shader.PropertyToID(nameof(agDepthView));
		public static readonly int agDepthViewInv_ID = Shader.PropertyToID(nameof(agDepthViewInv));

		[SerializeField] private EnvironmentDepthManager envDepthTextureProvider;
		[SerializeField] private ComputeShader normalTexShader;
		private (uint x, uint y, uint z) normShadSize;

		private RenderTexture normTex;

		public Transform trackingSpace;
		public static bool DepthAvailable { get; private set; }

		private void Start()
		{
			normalTexShader.GetKernelThreadGroupSizes(0, 
				out normShadSize.x, 
				out normShadSize.y, 
				out normShadSize.z);
		}

		private void Update()
		{
			UpdateCurrentRenderingState();
		}

		public void UpdateCurrentRenderingState()
		{
			DepthAvailable = //Utils.GetEnvironmentDepthSupported() &&
				envDepthTextureProvider != null &&
				envDepthTextureProvider.IsDepthAvailable;

			if (!DepthAvailable)
				return;

			Texture depthTex = Shader.GetGlobalTexture(Meta_EnvironmentDepthTexture_ID);

			Shader.SetGlobalTexture(agDepthTex_ID, depthTex);

			Shader.SetGlobalVector(agDepthTexSize_ID, new Vector2(depthTex.width, depthTex.height));

			if(normTex == null)
			{
				normTex = new(depthTex.width, depthTex.height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SNorm, 1);

				normTex.dimension = UnityEngine.Rendering.TextureDimension.Tex2DArray;
				normTex.volumeDepth = 2;
				normTex.useMipMap = false;
				normTex.enableRandomWrite = true;

				normTex.Create();
			}

			normalTexShader.SetTexture(0, agDepthTex_ID, depthTex);
			normalTexShader.SetTexture(0, agDepthNormalTexRW_ID, normTex);
			normalTexShader.Dispatch(0, depthTex.width / (int)normShadSize.x, depthTex.height / (int)normShadSize.y, 2);

			Shader.SetGlobalTexture(agDepthNormTex_ID,
				normTex);

			for (int i = 0; i < agDepthProj.Length; i++)
			{
				var desc = Utils.GetEnvironmentDepthFrameDesc(i);

				agDepthProj[i] = CalculateDepthProjMatrix(desc);
				agDepthProjInv[i] = Matrix4x4.Inverse(agDepthProj[i]);

				agDepthView[i] = CalculateDepthViewMatrix(desc) * trackingSpace.worldToLocalMatrix;
				agDepthViewInv[i] = Matrix4x4.Inverse(agDepthView[i]);
			}

			Shader.SetGlobalMatrixArray(nameof(agDepthProj), agDepthProj);
			Shader.SetGlobalMatrixArray(nameof(agDepthProjInv), agDepthProjInv);
			Shader.SetGlobalMatrixArray(nameof(agDepthView), agDepthView);
			Shader.SetGlobalMatrixArray(nameof(agDepthViewInv), agDepthViewInv);
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
