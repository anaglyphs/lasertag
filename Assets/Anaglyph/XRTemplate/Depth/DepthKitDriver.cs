using System;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static DepthKitDriver Instance { get; private set; }

		private readonly Matrix4x4[] proj = new Matrix4x4[2];
		public Matrix4x4[] Proj => proj;
		private readonly Matrix4x4[] projInv = new Matrix4x4[2];
		public Matrix4x4[] ProjInv => projInv;

		private readonly Matrix4x4[] view = new Matrix4x4[2];
		public Matrix4x4[] View => view;
		private readonly Matrix4x4[] viewInv = new Matrix4x4[2];
		public Matrix4x4[] ViewInv => viewInv;

		private Vector2 planes;
		public Vector2 Planes => planes;

		private static int ID(string str)
		{
			return Shader.PropertyToID(str);
		}

		public static readonly int depthTexID = ID("agDepthTex");
		public static readonly int rwDepthTexID = ID("agDepthTexRW");
		public static readonly int texSizeID = ID("agDepthTexSize");
		public static readonly int normTexID = ID("agDepthNormalTex");
		public static readonly int rwNormTexID = ID("agDepthNormalTexRW");
		public static readonly int zParamsID = ID("agDepthZParams");

		public static readonly int projID = ID("agDepthProj");
		public static readonly int projInvID = ID("agDepthProjInv");
		public static readonly int viewID = ID("agDepthView");
		public static readonly int viewInvID = ID("agDepthViewInv");

		public static readonly int inputRawMonoDepthID = ID("inputRawMonoDepth");

		public static bool DepthAvailable { get; private set; }

		[SerializeField] private ComputeShader depthNormalCompute = null;

		private ComputeKernel depthCopyKernel;
		private ComputeKernel monoRawDepthConvert;
		private ComputeKernel normKernel;

		private Camera mainCam;

		[SerializeField] private Texture depthTex;
		public Texture DepthTex => depthTex;
		[SerializeField] private RenderTexture normTex;
		public RenderTexture NormTex => normTex;

		private RenderTexture simulatedDepthTex;

		private AROcclusionManager arOcclusionManager;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;
		}

		private async void OnEnable()
		{
			await Awaitable.EndOfFrameAsync();
		}

		private void Start()
		{
			arOcclusionManager = FindFirstObjectByType<AROcclusionManager>();
			arOcclusionManager.frameReceived += OnDepthFrame;

			normKernel = new ComputeKernel(depthNormalCompute, "DepthNorm");
			monoRawDepthConvert = new ComputeKernel(depthNormalCompute, "MonoRawDepthToStereo");
			depthCopyKernel = new ComputeKernel(depthNormalCompute, "DepthCopy");
		}

		private void OnDestroy()
		{
			if (arOcclusionManager)
				arOcclusionManager.frameReceived -= OnDepthFrame;
		}

		private void OnDepthFrame(AROcclusionFrameEventArgs args)
		{
#if UNITY_EDITOR
			Texture rawDepth = args.externalTextures[0].texture;

			DepthAvailable = rawDepth != null;
			if (!DepthAvailable) return;

			if (simulatedDepthTex == null ||
			    simulatedDepthTex.width != rawDepth.width ||
			    simulatedDepthTex.height != rawDepth.height)
			{
				simulatedDepthTex = new RenderTexture(rawDepth.width, rawDepth.height, 0,
					GraphicsFormat.R16_UNorm, 1)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					enableRandomWrite = true
				};
				Shader.SetGlobalVector(texSizeID, new Vector2(rawDepth.width, rawDepth.height));
			}

			monoRawDepthConvert.Set(rwDepthTexID, simulatedDepthTex);
			monoRawDepthConvert.Set(inputRawMonoDepthID, rawDepth);
			monoRawDepthConvert.DispatchFit(rawDepth.width, rawDepth.height);
			depthTex = simulatedDepthTex;

			if (!mainCam) mainCam = Camera.main;
			Matrix4x4 p = mainCam.projectionMatrix;
			Matrix4x4 pi = p.inverse;

			Transform ct = mainCam.transform;
			Matrix4x4 vi = Matrix4x4.TRS(ct.position, ct.rotation, _scalingVector3);
			Matrix4x4 v = vi.inverse;

			for (int i = 0; i < 2; i++)
			{
				proj[i] = p;
				projInv[i] = pi;
				view[i] = v;
				viewInv[i] = vi;
			}

			planes = new Vector2(mainCam.nearClipPlane, mainCam.farClipPlane);

#elif UNITY_ANDROID
			depthTex = args.externalTextures[0].texture;

			ReadOnlyList<XRFov> fovs = null;
			ReadOnlyList<Pose> poses = null;
			XRNearFarPlanes depthPlanes = default;

			DepthAvailable = depthTex != null &&
			                 args.TryGetFovs(out fovs) &&
			                 args.TryGetPoses(out poses) &&
			                 args.TryGetNearFarPlanes(out depthPlanes);

			if (!DepthAvailable) return;

			for (int i = 0; i < 2; i++)
			{
				proj[i] = CalculateDepthProjMatrix(fovs[i], depthPlanes);
				projInv[i] = Matrix4x4.Inverse(proj[i]);

				Pose pose = poses[i];
				Matrix4x4 depthFrameMat = Matrix4x4.TRS(pose.position, pose.rotation, _scalingVector3);

				view[i] = depthFrameMat.inverse * MainXRRig.TrackingSpace.worldToLocalMatrix;
				viewInv[i] = Matrix4x4.Inverse(view[i]);
			}

			planes = new Vector2(depthPlanes.nearZ, depthPlanes.farZ);
#endif

			Shader.SetGlobalMatrixArray(projID, proj);
			Shader.SetGlobalMatrixArray(projInvID, projInv);
			Shader.SetGlobalMatrixArray(viewID, view);
			Shader.SetGlobalMatrixArray(viewInvID, viewInv);
			Shader.SetGlobalVector(zParamsID, planes);
			Shader.SetGlobalVector(texSizeID, new Vector2(depthTex.width, depthTex.height));
			Shader.SetGlobalTexture(depthTexID, depthTex);

			// create normals from depth
			if (normTex == null || normTex.width != depthTex.width || normTex.height != depthTex.height)

				normTex = new RenderTexture(depthTex.width, depthTex.height, 0, GraphicsFormat.R8G8B8A8_SNorm, 1)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					useMipMap = false,
					enableRandomWrite = true
				};

			normKernel.Set(depthTexID, depthTex);
			normKernel.Set(rwNormTexID, normTex);
			normKernel.DispatchFit(normTex);
			Shader.SetGlobalTexture(normTexID, normTex);

			Updated.Invoke();
		}

		private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

		private static Matrix4x4 CalculateDepthProjMatrix(XRFov fov, XRNearFarPlanes planes)
		{
			float left = fov.angleLeft;
			float right = fov.angleRight;
			float bottom = fov.angleDown;
			float top = fov.angleUp;

			left = Mathf.Tan(fov.angleLeft);
			right = Mathf.Tan(fov.angleRight);
			bottom = Mathf.Tan(fov.angleDown);
			top = Mathf.Tan(fov.angleUp);

			float near = planes.nearZ;
			float far = planes.farZ;

			float x = 2.0F / (right - left);
			float y = 2.0F / (top - bottom);
			float a = (right + left) / (right - left);
			float b = (top + bottom) / (top - bottom);
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
			Matrix4x4 m = new()
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
	}
}