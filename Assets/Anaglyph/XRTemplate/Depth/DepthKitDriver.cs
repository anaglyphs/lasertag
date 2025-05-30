using Meta.XR.EnvironmentDepth;
using Unity.XR.Oculus;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static DepthKitDriver Instance { get; private set; }

		private Matrix4x4[] agDepthProj = new Matrix4x4[2];
		private Matrix4x4[] agDepthProjInv = new Matrix4x4[2];

		private Matrix4x4[] agDepthView = new Matrix4x4[2];
		private Matrix4x4[] agDepthViewInv = new Matrix4x4[2];

		private static int ID(string str) => Shader.PropertyToID(str);

		public static readonly int Meta_PreprocessedEnvironmentDepthTexture_ID = ID("_PreprocessedEnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthTexture_ID = ID("_EnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthZBufferParams_ID = ID("_EnvironmentDepthZBufferParams");
		public static readonly int agDepthTex_ID = ID("agDepthTex");
		public static readonly int agDepthEdgeTex_ID = ID("agDepthEdgeTex");
		public static readonly int agDepthNormTex_ID = ID("agDepthNormalTex");
		public static readonly int agDepthNormalTexRW_ID = ID("agDepthNormalTexRW");
		public static readonly int agDepthZParams_ID = ID("agDepthZParams");

		public static readonly int agDepthProj_ID = ID(nameof(agDepthProj));
		public static readonly int agDepthProjInv_ID = ID(nameof(agDepthProjInv));

		public static readonly int agDepthView_ID = ID(nameof(agDepthView));
		public static readonly int agDepthViewInv_ID = ID(nameof(agDepthViewInv));

		public static readonly int agDepthTexSize = ID(nameof(agDepthTexSize));

		public static bool DepthAvailable { get; private set; }

		[SerializeField] private ComputeShader depthNormalCompute = null;
		
		private ComputeKernel normKernel;
		[SerializeField] private RenderTexture normTex = null;

		private void Awake()
		{
			Instance = this;
		}
		
		private void Start()
		{
			normKernel = new(depthNormalCompute, "DepthNorm");
		}

		private void Update()
		{
			UpdateCurrentRenderingState();
		}

		public void UpdateCurrentRenderingState()
		{
			Texture depthTex = Shader.GetGlobalTexture(Meta_EnvironmentDepthTexture_ID);

			DepthAvailable = depthTex != null;

			if (!DepthAvailable)
				return;

			Shader.SetGlobalVector(agDepthTexSize, new Vector2(depthTex.width, depthTex.height));

			Shader.SetGlobalTexture(agDepthTex_ID, depthTex);

			Shader.SetGlobalTexture(agDepthEdgeTex_ID,
				Shader.GetGlobalTexture(Meta_PreprocessedEnvironmentDepthTexture_ID));

			Shader.SetGlobalVector(agDepthZParams_ID,
				Shader.GetGlobalVector(Meta_EnvironmentDepthZBufferParams_ID));

			int w = depthTex.width;
			int h = depthTex.height;

			if (normTex == null)
			{
				normTex = new(w, h, 0, GraphicsFormat.R8G8B8A8_SNorm, 1);

				normTex.dimension = TextureDimension.Tex2DArray;
				normTex.volumeDepth = 2;
				normTex.useMipMap = false;
				normTex.enableRandomWrite = true;

				normTex.Create();
			}

			normKernel.Set(agDepthTex_ID, depthTex);
			normKernel.Set(agDepthNormalTexRW_ID, normTex);
			normKernel.DispatchGroups(normTex);

			Shader.SetGlobalTexture(agDepthNormTex_ID, normTex);

			for (int i = 0; i < agDepthProj.Length; i++)
			{
				var desc = Utils.GetEnvironmentDepthFrameDesc(i);

				agDepthProj[i] = CalculateDepthProjMatrix(desc);
				agDepthProjInv[i] = Matrix4x4.Inverse(agDepthProj[i]);

				agDepthView[i] = CalculateDepthViewMatrix(desc) * MainXROrigin.Transform.worldToLocalMatrix;
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
