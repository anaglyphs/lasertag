using Meta.XR.EnvironmentDepth;
using System;
using Unity.XR.Oculus;
using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : SingletonBehavior<DepthKitDriver>
	{
		public static Action OnNewDepthFrame = delegate { };

		Matrix4x4[] agDepthProj = new Matrix4x4[2];
		Matrix4x4[] agDepthProjInv = new Matrix4x4[2];

		Matrix4x4[] agDepthView = new Matrix4x4[2];
		Matrix4x4[] agDepthViewInv = new Matrix4x4[2];

		public static readonly int Meta_PreprocessedEnvironmentDepthTexture_ID = Shader.PropertyToID("_PreprocessedEnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthTexture_ID = Shader.PropertyToID("_EnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthZBufferParams_ID = Shader.PropertyToID("_EnvironmentDepthZBufferParams");
		public static readonly int agDepthTex_ID = Shader.PropertyToID("agDepthTex");
		public static readonly int agDepthEdgeTex_ID = Shader.PropertyToID("agDepthEdgeTex");
		public static readonly int dk_NormalTexture_ID = Shader.PropertyToID("dk_NormalTexture");
		public static readonly int agDepthZParams_ID = Shader.PropertyToID("agDepthZParams");

		public static readonly int agDepthProj_ID = Shader.PropertyToID(nameof(agDepthProj));
		public static readonly int agDepthProjInv_ID = Shader.PropertyToID(nameof(agDepthProjInv));

		public static readonly int agDepthView_ID = Shader.PropertyToID(nameof(agDepthView));
		public static readonly int agDepthViewInv_ID = Shader.PropertyToID(nameof(agDepthViewInv));

		[SerializeField] private EnvironmentDepthManager envDepthTextureProvider;

		public Transform trackingSpace;
		public static bool DepthAvailable { get; private set; }

		protected override void SingletonAwake()
		{
			
		}

		protected override void OnSingletonDestroy()
		{
			OnNewDepthFrame = delegate { };
		}

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

			Shader.SetGlobalTexture(agDepthTex_ID,
				Shader.GetGlobalTexture(Meta_EnvironmentDepthTexture_ID));

			Shader.SetGlobalTexture(agDepthEdgeTex_ID,
				Shader.GetGlobalTexture(Meta_PreprocessedEnvironmentDepthTexture_ID));

			Shader.SetGlobalVector(agDepthZParams_ID,
				Shader.GetGlobalVector(Meta_EnvironmentDepthZBufferParams_ID));

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

			OnNewDepthFrame.Invoke();
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
