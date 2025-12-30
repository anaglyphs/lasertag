using System;
using Meta.XR.EnvironmentDepth;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR.Features.Meta;

namespace Anaglyph.XRTemplate.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static DepthKitDriver Instance { get; private set; }
		
		// private MetaOpenXROcclusionSubsystem depthSubsystem;
		private XRFov[]         depthFrameFOVs   = new XRFov[2];
		private Pose[]          depthFramePoses  = new Pose[2];
		private XRNearFarPlanes depthPlanes;

		private Matrix4x4[] agDepthProj = new Matrix4x4[2];
		private Matrix4x4[] agDepthProjInv = new Matrix4x4[2];

		private Matrix4x4[] agDepthView = new Matrix4x4[2];
		private Matrix4x4[] agDepthViewInv = new Matrix4x4[2];

		private static int ID(string str) => Shader.PropertyToID(str);

		// public static readonly int Meta_PreprocessedEnvironmentDepthTexture_ID = ID("_PreprocessedEnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthTexture_ID = ID("_EnvironmentDepthTexture");
		public static readonly int Meta_EnvironmentDepthZBufferParams_ID = ID("_EnvironmentDepthZBufferParams");
		
		public static readonly int inputDepthTex_ID = ID("inputDepthTex");
		public static readonly int agDepthTexRW_ID = ID("agDepthTexRW");
		public static readonly int agDepthTex_ID = ID("agDepthTex");
		// public static readonly int agDepthEdgeTex_ID = ID("agDepthEdgeTex");
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

			Texture depthTex = Shader.GetGlobalTexture(Meta_EnvironmentDepthTexture_ID);
			
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
			Shader.SetGlobalVector(agDepthTexSize, new Vector2(depthTex.width, depthTex.height));
			
			Shader.SetGlobalVector(agDepthZParams_ID,
				Shader.GetGlobalVector(Meta_EnvironmentDepthZBufferParams_ID));
			
			// Shader.SetGlobalTexture(agDepthEdgeTex_ID, 
			// 	Shader.GetGlobalTexture(Meta_PreprocessedEnvironmentDepthTexture_ID));

			// create normals from depth
			
			normKernel.Set(agDepthTex_ID, depthTex);
			normKernel.Set(agDepthNormalTexRW_ID, normTex);
			normKernel.DispatchGroups(normTex);

			Shader.SetGlobalTexture(agDepthNormTex_ID, normTex);


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

			for (int i = 0; i < agDepthProj.Length; i++)
			{
				agDepthProj[i] = CalculateDepthProjMatrix(depthFrameFOVs[i], depthPlanes);
				agDepthProjInv[i] = Matrix4x4.Inverse(agDepthProj[i]);
				
				var pose = depthFramePoses[i];
				Matrix4x4 depthFrameMat = Matrix4x4.TRS(pose.position, pose.rotation, Vector3.one);

				agDepthView[i] = depthFrameMat.inverse * MainXRRig.TrackingSpace.worldToLocalMatrix;
				agDepthViewInv[i] = Matrix4x4.Inverse(agDepthView[i]);
			}

			Shader.SetGlobalMatrixArray(nameof(agDepthProj), agDepthProj);
			Shader.SetGlobalMatrixArray(nameof(agDepthProjInv), agDepthProjInv);
			Shader.SetGlobalMatrixArray(nameof(agDepthView), agDepthView);
			Shader.SetGlobalMatrixArray(nameof(agDepthViewInv), agDepthViewInv);
			
			Updated.Invoke();
		}

		// private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

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
