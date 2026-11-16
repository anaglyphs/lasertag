using System;
using Anaglyph.GPU;
using Unity.XR.CoreUtils.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.DepthKit
{
	[DefaultExecutionOrder(-40)]
	public class DepthKitDriver : MonoBehaviour
	{
		public static DepthKitDriver Instance { get; private set; }

		private readonly Matrix4x4[] proj = new Matrix4x4[2];
		private readonly Matrix4x4[] projInv = new Matrix4x4[2];
		private readonly Matrix4x4[] view = new Matrix4x4[2];
		private readonly Matrix4x4[] viewInv = new Matrix4x4[2];
		private Vector2 planes;

		public Matrix4x4[] Proj => proj;
		public Matrix4x4[] ProjInv => projInv;
		public Matrix4x4[] View => view;
		public Matrix4x4[] ViewInv => viewInv;
		public Vector2 Planes => planes;

		private static int ID(string str)
		{
			return Shader.PropertyToID(str);
		}

		public static readonly int depthTexID = ID("agDepthTex");
		public static readonly int texSizeID = ID("agDepthTexSize");
		public static readonly int normTexID = ID("agDepthNormalTex");
		public static readonly int rwNormTexID = ID("agDepthNormalTexRW");
		public static readonly int zParamsID = ID("agDepthZParams");

		public static readonly int projID = ID("agDepthProj");
		public static readonly int projInvID = ID("agDepthProjInv");
		public static readonly int viewID = ID("agDepthView");
		public static readonly int viewInvID = ID("agDepthViewInv");
		
		public static event Action<bool> AvailabilityChanged = delegate { };

		private static bool depthAvailable;
		
		public static bool DepthAvailable
		{
			get => depthAvailable;

			private set
			{
				if (depthAvailable == value) return;

				depthAvailable = value;
				AvailabilityChanged.Invoke(value);
			}
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			depthAvailable = false;
			AvailabilityChanged = delegate { };
		}

		[SerializeField] private ComputeShader depthNormalCompute = null;
		[FormerlySerializedAs("simDepthConversionShader")]
		[SerializeField] private Shader depthConversionShader;

		[Tooltip("A phone's depth image has its first row at the top of the camera's view; Unity's textures count rows from the bottom")]
		[SerializeField] private bool flipPhoneDepthRows = true;

		private Material depthConversionMat;
		private MaterialPropertyBlock depthConversionProps;

		private static readonly int rawDepthID = ID("rawDepth");
		private static readonly int rawDepthArrayID = ID("rawDepthArray");
		private static readonly int sliceID = ID("_Slice");
		private static readonly int rawDepthScaleID = ID("_RawDepthScale");
		private static readonly int flipRowsID = ID("_FlipRows");

		private const int MonoLinearPass = 0;
		private const int StereoSanitizePass = 1;

		private ComputeKernel normKernel;

		private Camera mainCam;

		private Texture depthTex;
		public Texture DepthTex => depthTex;
		private RenderTexture normTex;
		public RenderTexture NormTex => normTex;

		// every depth source is converted into this, never published raw
		private RenderTexture convertedDepthTex;

		private AROcclusionManager arOcclusionManager;
		private ARCameraManager arCameraManager;

		public event Action Updated = delegate { };

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			arOcclusionManager = FindFirstObjectByType<AROcclusionManager>();

			if (!arOcclusionManager)
				throw new Exception("[DepthKitDriver] AROcclusionManager not found");

			normKernel = new ComputeKernel(depthNormalCompute, "DepthNorm");

			arOcclusionManager.frameReceived += OnDepthFrame;
		}

		private void OnEnable()
		{
			if (didStart && arOcclusionManager)
				arOcclusionManager.frameReceived += OnDepthFrame;
		}

		private void OnDisable()
		{
			if (arOcclusionManager)
				arOcclusionManager.frameReceived -= OnDepthFrame;

			// no more frames are coming, so the globals stop tracking the headset
			DepthAvailable = false;
		}

		private enum DepthFormatScenario
		{
			PhoneOrARFoundationSimulator,
			StereoHeadset,
			Unknown
		}

		private void OnDepthFrame(AROcclusionFrameEventArgs args)
		{
			ReadOnlyList<ARExternalTexture> extTextures = args.externalTextures;
			if (extTextures == null || extTextures.Count < 1 || extTextures[0].texture == null)
			{
				DepthAvailable = false;
				Debug.LogWarning("[DepthKitDriver] No depth texture!");
				return;
			}

			Texture rawDepth = extTextures[0].texture;

			DepthFormatScenario depthFormat = DepthFormatScenario.Unknown;
			// meters per sampled unit: ARKit and the simulator give meters, ARCore millimeters in 16 bits or half-float meters
			float rawDepthScale = rawDepth.graphicsFormat switch
			{
				GraphicsFormat.R32_SFloat or GraphicsFormat.R16_SFloat => 1,
				GraphicsFormat.R16_UNorm => 65.535f,
				_ => 0
			};
			if (rawDepthScale > 0 && rawDepth.dimension == TextureDimension.Tex2D)
				depthFormat = DepthFormatScenario.PhoneOrARFoundationSimulator;
			else if (rawDepth is RenderTexture rawDepthRT &&
			         rawDepthRT.depthStencilFormat == GraphicsFormat.D16_UNorm &&
			         rawDepthRT.dimension == TextureDimension.Tex2DArray &&
			         rawDepthRT.volumeDepth == 2)
				depthFormat = DepthFormatScenario.StereoHeadset;

			switch (depthFormat)
			{
				case DepthFormatScenario.PhoneOrARFoundationSimulator:

					// Convert mono linear R32_SFloat depth texture to (mock) stereo non-linear D16_UNorm

					if (!mainCam) mainCam = Camera.main;
					planes = new Vector2(mainCam.nearClipPlane, mainCam.farClipPlane);
					// the conversion reads these
					Shader.SetGlobalVector(zParamsID, planes);

					bool phone = HandheldAR.Runtime != HandheldAR.Provider.None;
					ConvertDepth(rawDepth, rawDepthID, MonoLinearPass, rawDepthScale, phone && flipPhoneDepthRows);

					Matrix4x4 p, vi;
					if (phone)
					{
						// the depth image shares the camera image's lens and its frame's pose, not the screen's
						if (!TryGetPhoneDepthCamera(args, rawDepth.width, rawDepth.height, out p, out vi))
						{
							DepthAvailable = false;
							return;
						}
					}
					else
					{
						// the simulator renders depth through the Unity camera as it is now
						p = mainCam.projectionMatrix;
						Transform ct = mainCam.transform;
						vi = Matrix4x4.TRS(ct.position, ct.rotation, _scalingVector3);
					}

					Matrix4x4 pi = p.inverse;
					Matrix4x4 v = vi.inverse;

					for (int i = 0; i < 2; i++)
					{
						proj[i] = p;
						projInv[i] = pi;
						view[i] = v;
						viewInv[i] = vi;
					}

					break;

				case DepthFormatScenario.StereoHeadset:

					// Likely hard-coded to only support Meta Quest for now...
					ReadOnlyList<XRFov> fovs = null;
					ReadOnlyList<Pose> poses = null;
					XRNearFarPlanes depthPlanes = default;

					bool gotMetadata = args.TryGetFovs(out fovs) &&
					                   args.TryGetPoses(out poses) &&
					                   args.TryGetNearFarPlanes(out depthPlanes);

					if (!gotMetadata)
					{
						DepthAvailable = false;
						Debug.LogWarning("[DepthKitDriver] couldn't get depth frame metadata!");
						return;
					}

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

					// Meta writes 0 (= the near plane) where it has no depth, e.g. the sky
					ConvertDepth(rawDepth, rawDepthArrayID, StereoSanitizePass);

					break;

				default:

					DepthAvailable = false;
					Debug.LogWarning("[DepthKitDriver] Unrecognized texture format!");
					return;
			}

			Shader.SetGlobalMatrixArray(projID, proj);
			Shader.SetGlobalMatrixArray(projInvID, projInv);
			Shader.SetGlobalMatrixArray(viewID, view);
			Shader.SetGlobalMatrixArray(viewInvID, viewInv);
			Shader.SetGlobalVector(zParamsID, planes);
			Shader.SetGlobalVector(texSizeID, new Vector2(depthTex.width, depthTex.height));
			Shader.SetGlobalTexture(depthTexID, depthTex);

			// create normals from depth
			if (normTex == null || normTex.width != depthTex.width || normTex.height != depthTex.height)
			{
				if (normTex != null)
					Destroy(normTex);

				normTex = new RenderTexture(depthTex.width, depthTex.height, 0, GraphicsFormat.R8G8B8A8_SNorm, 1)
				{
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					useMipMap = false,
					enableRandomWrite = true
				};
			}

			normKernel.Bind(depthTexID, depthTex);
			normKernel.Bind(rwNormTexID, normTex);
			normKernel.DispatchFit(normTex);
			Shader.SetGlobalTexture(normTexID, normTex);

			DepthAvailable = true;

			Updated.Invoke();
		}

		// writes rawDepth into convertedDepthTex, one fullscreen draw per eye slice,
		// with unknown texels rewritten to AG_DEPTH_UNKNOWN (see DepthKit.hlsl)
		private void ConvertDepth(Texture rawDepth, int rawDepthPropID, int pass, float rawDepthScale = 1, bool flipRows = false)
		{
			if (convertedDepthTex == null ||
			    convertedDepthTex.width != rawDepth.width ||
			    convertedDepthTex.height != rawDepth.height)
			{
				if (convertedDepthTex != null)
					Destroy(convertedDepthTex);

				RenderTextureDescriptor desc = new()
				{
					width = rawDepth.width,
					height = rawDepth.height,
					autoGenerateMips = false,
					graphicsFormat = GraphicsFormat.None,
					depthStencilFormat = GraphicsFormat.D16_UNorm,
					dimension = TextureDimension.Tex2DArray,
					volumeDepth = 2,
					msaaSamples = 1,
					vrUsage = VRTextureUsage.TwoEyes
				};

				convertedDepthTex = new RenderTexture(desc) { name = "DepthKit Depth" };
			}

			if (depthConversionMat == null)
			{
				depthConversionMat = new Material(depthConversionShader);
				depthConversionProps = new MaterialPropertyBlock();
			}

			depthConversionMat.SetTexture(rawDepthPropID, rawDepth);
			depthConversionMat.SetFloat(rawDepthScaleID, rawDepthScale);
			depthConversionMat.SetFloat(flipRowsID, flipRows ? 1 : 0);

			CommandBuffer cmd = CommandBufferPool.Get("DepthKit Depth Conversion");
			for (int slice = 0; slice < 2; slice++)
			{
				depthConversionProps.SetInteger(sliceID, slice);
				cmd.SetRenderTarget(convertedDepthTex, 0, CubemapFace.Unknown, slice);
				cmd.ClearRenderTarget(true, false, Color.white);
				// fullscreen triangle
				cmd.DrawProcedural(Matrix4x4.identity, depthConversionMat, pass, MeshTopology.Triangles, 3, 1,
					depthConversionProps);
			}

			Graphics.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);

			depthTex = convertedDepthTex;
		}

		private void OnDestroy()
		{
			if (convertedDepthTex != null) Destroy(convertedDepthTex);
			if (depthConversionMat != null) Destroy(depthConversionMat);
		}

		private static readonly Vector3 _scalingVector3 = new(1, 1, -1);

		// a phone's depth: the camera's intrinsics scaled to the depth image, placed where the
		// camera stood at the depth frame's time, both in the sensor's own orientation
		private bool TryGetPhoneDepthCamera(AROcclusionFrameEventArgs args, int width, int height, out Matrix4x4 projection, out Matrix4x4 viewInverse)
		{
			projection = viewInverse = default;
			if (!arCameraManager) arCameraManager = FindFirstObjectByType<ARCameraManager>();
			HeadPoseHistory history = HeadPoseHistory.Instance;
			if (arCameraManager == null || !arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics) || intrinsics.resolution.x <= 0)
				return MissingPhoneDepthCamera("no camera intrinsics");
			if (history == null || !history.SamplesAreCameraFrames)
				return MissingPhoneDepthCamera("no camera frame poses yet");

			// the pose at the depth frame's own time; a depth frame without one is the newest camera frame's
			// (ARKit's scene depth comes with the frame it was captured in)
			bool timed = args.TryGetTimestamp(out long timestampNs) && history.TryGetWorldPose(timestampNs, out _);
			if (!history.TryGetWorldPose(timed ? timestampNs : history.NewestTimestampNs, out Pose camera))
				return MissingPhoneDepthCamera("no camera pose");

			float sx = (float)width / intrinsics.resolution.x, sy = (float)height / intrinsics.resolution.y;
			projection = ProjectionFromIntrinsics(intrinsics.focalLength.x * sx, intrinsics.focalLength.y * sy,
				intrinsics.principalPoint.x * sx, intrinsics.principalPoint.y * sy, width, height, planes.x, planes.y);
			viewInverse = Matrix4x4.TRS(camera.position, camera.rotation, _scalingVector3);
			// once per pairing of depth and camera image sizes: the camera image changes size in camera mode
			Vector4 sizes = new(width, height, intrinsics.resolution.x, intrinsics.resolution.y);
			if (sizes != loggedPhoneDepthSizes)
			{
				loggedPhoneDepthSizes = sizes;
				Debug.Log($"[DepthKitDriver] phone depth {width}×{height} {args.externalTextures[0].texture.graphicsFormat} over a {intrinsics.resolution.x}×{intrinsics.resolution.y} camera image, " +
				          $"focal {intrinsics.focalLength.x * sx:F1}×{intrinsics.focalLength.y * sy:F1} px, center {intrinsics.principalPoint.x * sx:F1},{intrinsics.principalPoint.y * sy:F1}, placed at " +
				          (timed ? "its frame's camera pose" : $"the newest camera frame's pose (its timestamp {(args.TryGetTimestamp(out long t) ? t.ToString() : "absent")}, newest camera frame {history.NewestTimestampNs})"));
			}
			return true;
		}

		private Vector4 loggedPhoneDepthSizes;
		private bool loggedPhoneDepthMissing;

		private bool MissingPhoneDepthCamera(string why)
		{
			if (!loggedPhoneDepthMissing)
			{
				loggedPhoneDepthMissing = true;
				Debug.LogWarning($"[DepthKitDriver] phone depth dropped: {why}");
			}

			return false;
		}

		/// <summary>
		/// An OpenGL-style projection (view looking down -Z, texture rows counted from the bottom) for a
		/// pinhole camera whose intrinsics are in pixels from the image's top-left corner.
		/// </summary>
		public static Matrix4x4 ProjectionFromIntrinsics(float fx, float fy, float cx, float cy, float width, float height, float near, float far)
		{
			Matrix4x4 m = Matrix4x4.zero;
			m.m00 = 2 * fx / width;
			m.m02 = 1 - 2 * cx / width;
			m.m11 = 2 * fy / height;
			m.m12 = 2 * cy / height - 1;
			m.m22 = -(far + near) / (far - near);
			m.m23 = -2 * far * near / (far - near);
			m.m32 = -1;
			return m;
		}

		private static Matrix4x4 CalculateDepthProjMatrix(XRFov fov, XRNearFarPlanes planes)
		{
			// float left = fov.angleLeft;
			// float right = fov.angleRight;
			// float bottom = fov.angleDown;
			// float top = fov.angleUp;

			float left = Mathf.Tan(fov.angleLeft);
			float right = Mathf.Tan(fov.angleRight);
			float bottom = Mathf.Tan(fov.angleDown);
			float top = Mathf.Tan(fov.angleUp);

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