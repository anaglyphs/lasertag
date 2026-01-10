using System;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static DepthKitDriver Instance { get; private set; }

		// private MetaOpenXROcclusionSubsystem depthSubsystem;
		private readonly XRFov[] depthFrameFOVs = new XRFov[2];
		private readonly Pose[] depthFramePoses = new Pose[2];
		private XRNearFarPlanes depthPlanes;

		private readonly Matrix4x4[] proj = new Matrix4x4[2];
		private readonly Matrix4x4[] projInv = new Matrix4x4[2];

		private readonly Matrix4x4[] view = new Matrix4x4[2];
		private readonly Matrix4x4[] viewInv = new Matrix4x4[2];

		private static int ID(string str) =>  Shader.PropertyToID(str);
		
		public static readonly int metaDepthTexID = ID("_EnvironmentDepthTexture");
		public static readonly int metaZParamsID = ID("_EnvironmentDepthZBufferParams");
		
		public static readonly int depthTexID = ID("agDepthTex");
		public static readonly int texSizeID = ID("agDepthTexSize");
		public static readonly int normTexID = ID("agDepthNormalTex");
		public static readonly int rwNormTexID = ID("agDepthNormalTexRW");
		public static readonly int zParamsID = ID("agDepthZParams");
		
		public static readonly int projID = ID("agDepthProj");
		public static readonly int projInvID = ID("agDepthProjInv");
		public static readonly int viewID = ID("agDepthView");
		public static readonly int viewInvID = ID("agDepthViewInv");

		public static bool DepthAvailable { get; private set; }

		[SerializeField] private ComputeShader depthNormalCompute = null;

		private ComputeKernel normKernel;

		[SerializeField] private RenderTexture normTex = null;
		private EnvironmentDepthManager depthManager;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			depthManager = FindFirstObjectByType<EnvironmentDepthManager>();
			normKernel = new ComputeKernel(depthNormalCompute, "DepthNorm");
		}

		// private static bool GetDepthSubsystem(out MetaOpenXROcclusionSubsystem depthSubsystem)
		// {
		// 	depthSubsystem = null;
		// 	
		// 	XRLoader xrLoader = XRGeneralSettings.Instance.Manager.activeLoader;
		// 	if (!xrLoader) return false;
		// 	
		// 	depthSubsystem = xrLoader.GetLoadedSubsystem<XROcclusionSubsystem>() as MetaOpenXROcclusionSubsystem;
		// 	return depthSubsystem != null;
		// }

		private void Update()
		{
			// if (depthSubsystem == null && !GetDepthSubsystem(out depthSubsystem))
			// 	return;
			//
			// if (!depthSubsystem.TryGetFrame(Allocator.Temp, out XROcclusionFrame frame))
			// 	return;

			Texture depthTex = Shader.GetGlobalTexture(metaDepthTexID);

			DepthAvailable = depthTex != null;
			if (!DepthAvailable) return;

			int w = depthTex.width;
			int h = depthTex.height;

			if (normTex == null || normTex.width != w || normTex.height != h)
			{
				normTex = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_SNorm, 1)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					useMipMap = false,
					enableRandomWrite = true
				};

				normTex.Create();

				Shader.SetGlobalVector(texSizeID, new Vector2(w, h));
			}
			
			Shader.SetGlobalTexture(depthTexID, depthTex);

			Shader.SetGlobalVector(zParamsID, Shader.GetGlobalVector(metaZParamsID));

			// create normals from depth
			normKernel.Set(depthTexID, depthTex);
			normKernel.Set(rwNormTexID, normTex);
			normKernel.DispatchGroups(normTex);

			Shader.SetGlobalTexture(normTexID, normTex);

			for (int i = 0; i < depthManager.frameDescriptors.Length; i++)
			{
				DepthFrameDesc d = depthManager.frameDescriptors[i];

				XRFov fov = new(
					d.fovLeftAngleTangent,
					d.fovRightAngleTangent,
					d.fovTopAngleTangent,
					d.fovDownAngleTangent);

				depthFrameFOVs[i] = fov;

				Pose pose = new(d.createPoseLocation, d.createPoseRotation);

				depthFramePoses[i] = pose;

				depthPlanes = new XRNearFarPlanes(d.nearZ, d.farZ);
			}

			// frame.TryGetFovs(out NativeArray<XRFov> nativeFOVs);
			// nativeFOVs.CopyTo(depthFrameFOVs);
			// frame.TryGetPoses(out NativeArray<Pose> nativePoses);
			// nativePoses.CopyTo(depthFramePoses);
			// frame.TryGetNearFarPlanes(out var nearFarPlanes);

			for (int i = 0; i < proj.Length; i++)
			{
				proj[i] = CalculateDepthProjMatrix(depthFrameFOVs[i], depthPlanes);
				projInv[i] = Matrix4x4.Inverse(proj[i]);

				Pose pose = depthFramePoses[i];
				Matrix4x4 depthFrameMat = Matrix4x4.TRS(pose.position, pose.rotation, _scalingVector3);

				view[i] = depthFrameMat.inverse * MainXRRig.TrackingSpace.worldToLocalMatrix;
				viewInv[i] = Matrix4x4.Inverse(view[i]);
			}

			Shader.SetGlobalMatrixArray(projID, proj);
			Shader.SetGlobalMatrixArray(projInvID, projInv);
			Shader.SetGlobalMatrixArray(viewID, view);
			Shader.SetGlobalMatrixArray(viewInvID, viewInv);

			Updated.Invoke();
		}

		private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

		private static Matrix4x4 CalculateDepthProjMatrix(XRFov fov, XRNearFarPlanes planes)
		{
			float left = fov.angleLeft;
			float right = fov.angleRight;
			float bottom = fov.angleDown;
			float top = fov.angleUp;
			float near = planes.nearZ;
			float far = planes.farZ;

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