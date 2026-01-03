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

		private static int ID(string str)
		{
			return Shader.PropertyToID(str);
		}

		// public static readonly int Meta_PreprocessedEnvironmentDepthTexture_ID = ID("_PreprocessedEnvironmentDepthTexture");
		public static readonly int metaDepthTexID = ID("_EnvironmentDepthTexture");
		public static readonly int metaZParamsID = ID("_EnvironmentDepthZBufferParams");

		public static readonly int inputDepthTex_ID = ID("inputDepthTex");
		public static readonly int agDepthTexRW_ID = ID("agDepthTexRW");

		public static readonly int agDepthTex_ID = ID("agDepthTex");

		// public static readonly int agDepthEdgeTex_ID = ID("agDepthEdgeTex");
		public static readonly int normTexID = ID("agDepthNormalTex");
		public static readonly int normTexWriteID = ID("agDepthNormalTexRW");
		public static readonly int zParamsID = ID("agDepthZParams");

		public static readonly int projID = ID(nameof(proj));
		public static readonly int projInvID = ID(nameof(projInv));

		public static readonly int viewID = ID(nameof(view));
		public static readonly int viewInvID = ID(nameof(viewInv));

		public static readonly int texSizeID = ID(nameof(texSizeID));

		public static bool DepthAvailable { get; private set; }

		[SerializeField] private ComputeShader depthNormalCompute = null;
		
		private static readonly Vector3 NegZ = new(1, 1, -1);
		
		// private ComputeKernel copyKernel;
		private ComputeKernel normKernel;

		// [SerializeField] private RenderTexture depthTex = null;
		[SerializeField] private RenderTexture normTex = null;
		private EnvironmentDepthManager depthManager;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;
		}

		private async void OnEnable()
		{
			await Awaitable.EndOfFrameAsync();
			Application.onBeforeRender += UpdateCurrentRenderingState;
		}

		private void OnDisable()
		{
			Application.onBeforeRender -= UpdateCurrentRenderingState;
		}

		private void Start()
		{
			depthManager = FindFirstObjectByType<EnvironmentDepthManager>();

			// copyKernel = new ComputeKernel(depthNormalCompute, "DepthCopy");
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

		private void UpdateCurrentRenderingState()
		{
			// if (depthSubsystem == null && !GetDepthSubsystem(out depthSubsystem))
			// 	return;
			//
			// if (!depthSubsystem.TryGetFrame(Allocator.Temp, out XROcclusionFrame frame))
			// 	return;

			Texture depthTex = Shader.GetGlobalTexture(metaDepthTexID);

			DepthAvailable = depthTex != null;
			if (!DepthAvailable)
				return;

			int w = depthTex.width;
			int h = depthTex.height;

			if (normTex == null)
			{
				// depthTex = new RenderTexture(w, h, 0, GraphicsFormat.R16_UNorm, 1)
				// {
				// 	// depthStencilFormat = GraphicsFormat.D16_UNorm,
				// 	dimension = TextureDimension.Tex2DArray,
				// 	volumeDepth = 2,
				// 	useMipMap = false,
				// 	enableRandomWrite = true
				// };
				//
				// depthTex.Create();

				normTex = new RenderTexture(w, h, 0, GraphicsFormat.R8G8B8A8_SNorm, 1)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					useMipMap = false,
					enableRandomWrite = true
				};

				normTex.Create();
			}

			// copy meta depth tex into color format tex

			// copyKernel.Set(inputDepthTex_ID, metaDepthTex);
			// copyKernel.Set(agDepthTexRW_ID, depthTex);
			//
			// copyKernel.DispatchGroups(depthTex);

			Shader.SetGlobalTexture(agDepthTex_ID, depthTex);
			Shader.SetGlobalVector(texSizeID, new Vector2(depthTex.width, depthTex.height));

			Shader.SetGlobalVector(zParamsID,
				Shader.GetGlobalVector(metaZParamsID));

			// Shader.SetGlobalTexture(agDepthEdgeTex_ID, 
			// 	Shader.GetGlobalTexture(Meta_PreprocessedEnvironmentDepthTexture_ID));

			// create normals from depth

			normKernel.Set(agDepthTex_ID, depthTex);
			normKernel.Set(normTexWriteID, normTex);
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
				Matrix4x4 depthFrameMat = Matrix4x4.TRS(pose.position, pose.rotation, NegZ);

				view[i] = depthFrameMat.inverse * MainXRRig.TrackingSpace.worldToLocalMatrix;
				viewInv[i] = Matrix4x4.Inverse(view[i]);
			}

			Shader.SetGlobalMatrixArray(nameof(proj), proj);
			Shader.SetGlobalMatrixArray(nameof(projInv), projInv);
			Shader.SetGlobalMatrixArray(nameof(view), view);
			Shader.SetGlobalMatrixArray(nameof(viewInv), viewInv);

			Updated.Invoke();
		}

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

		// private static Matrix4x4 CalculateDepthViewMatrix(Utils.EnvironmentDepthFrameDesc frameDesc)
		// {
		// 	var createRotation = frameDesc.createPoseRotation;
		// 	var depthOrientation = new Quaternion(
		// 		createRotation.x,
		// 		createRotation.y,
		// 		createRotation.z,
		// 		createRotation.w
		// 	);
		//
		// 	var viewMatrix = Matrix4x4.TRS(frameDesc.createPoseLocation, depthOrientation,
		// 		_scalingVector3).inverse;
		//
		// 	return viewMatrix;
		// }
	}
}