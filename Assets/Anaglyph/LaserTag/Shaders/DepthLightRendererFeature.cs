using Anaglyph.XR.DepthKit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag.Shaders
{
	// Lights the depth map at its own resolution before the opaques (IrradiancePass, once
	// per eye slice), then blends that irradiance under the finished eye buffer after the
	// transparents (CompositePass), so the light loop runs per depth texel instead of per
	// eye pixel and coverage alpha, not depth, decides where it shows
	[DisallowMultipleRendererFeature("Depth Lighting")]
	public class DepthLightingRendererFeature : ScriptableRendererFeature
	{
		[SerializeField, Tooltip("Fullscreen depth lighting shader. Requires Forward+ and Render Graph.")]
		private Shader shader;
		[SerializeField] private RenderingLayerMask renderingLayers = RenderingLayerMask.defaultRenderingLayerMask;
		[SerializeField, Min(1), Tooltip("Most point/spot lights evaluated per depth texel, in URP's visible-light order.")]
		private int maxLights = 32;

		private Material material;
		private IrradiancePass irradiancePass;
		private CompositePass compositePass;
		private static bool globallyEnabled = true;

		public static void SetGloballyEnabled(bool value) => globallyEnabled = value;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => globallyEnabled = true;

		public override void Create()
		{
			irradiancePass?.Dispose();
			irradiancePass = null;
			compositePass = null;
			CoreUtils.Destroy(material);
			material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
			if (material == null) return;

			// before the opaques, so drawing into the irradiance slices doesn't split the
			// eye buffer's native render pass
			irradiancePass = new IrradiancePass(material)
			{
				renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
			};
			compositePass = new CompositePass(material)
			{
				// after everything that writes coverage alpha
				renderPassEvent = RenderPassEvent.AfterRenderingTransparents
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (irradiancePass == null || !globallyEnabled || !DepthKitDriver.DepthAvailable) return;
			if (renderingData.cameraData.cameraType != CameraType.Game ||
			    renderingData.cameraData.renderType != CameraRenderType.Base ||
			    renderingData.cameraData.targetTexture != null) return;

			irradiancePass.renderingLayers = renderingLayers.value;
			irradiancePass.maxLights = maxLights;
			renderer.EnqueuePass(irradiancePass);
			renderer.EnqueuePass(compositePass);
		}

		protected override void Dispose(bool disposing)
		{
			irradiancePass?.Dispose();
			irradiancePass = null;
			compositePass = null;
			CoreUtils.Destroy(material);
			material = null;
		}

		private const int IrradianceShaderPass = 0;
		private const int CompositeShaderPass = 1;

		private static readonly int WorldToDepthClipID = Shader.PropertyToID("_WorldToDepthClip");
		private static readonly int DepthClipToWorldID = Shader.PropertyToID("_DepthClipToWorld");

		// hands this camera's irradiance from IrradiancePass to CompositePass
		private sealed class DepthLightFrame : ContextItem
		{
			public TextureHandle irradiance;
			public Matrix4x4[] worldToDepthClip;

			public override void Reset()
			{
				irradiance = TextureHandle.nullHandle;
				worldToDepthClip = null;
			}
		}

		private sealed class IrradiancePass : ScriptableRenderPass
		{
			private static readonly int TexelSizeID = Shader.PropertyToID("_DepthLightTexelSize");
			private static readonly int EyeID = Shader.PropertyToID("_DepthLightEye");
			private static readonly int ClusterViewID = Shader.PropertyToID("_DepthLightClusterView");
			private static readonly int WorldToCameraClipID = Shader.PropertyToID("_DepthLightWorldToCameraClip");
			private static readonly int MaxLightsID = Shader.PropertyToID("_DepthLightMaxLights");
			private static readonly int RenderingLayersID = Shader.PropertyToID("_EnvironmentRenderingLayers");

			private readonly Material material;
			private readonly MaterialPropertyBlock properties = new();
			private readonly GraphicsFormat format;
			private readonly Matrix4x4[] worldToDepthClip = new Matrix4x4[2];
			private readonly Matrix4x4[] depthClipToWorld = new Matrix4x4[2];
			private RTHandle depthHandle;
			private RTHandle normalHandle;

			public uint renderingLayers;
			public int maxLights;

			public IrradiancePass(Material material)
			{
				this.material = material;
				profilingSampler = new ProfilingSampler("Depth Light Irradiance");
				format = SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormatUsage.Render)
					? GraphicsFormat.B10G11R11_UFloatPack32
					: GraphicsFormat.R16G16B16A16_SFloat;
			}

			private sealed class PassData
			{
				public Material material;
				public MaterialPropertyBlock properties;
				public XRPass xr;
				public Texture depth;
				public Texture normals;
				public Vector2 texelSize;
				public int eye;
				public int clusterView;
				public Matrix4x4 worldToCameraClip;
				public int maxLights;
				public uint renderingLayers;
				public Matrix4x4[] depthClipToWorld;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				DepthKitDriver source = DepthKitDriver.Instance;
				if (source == null || source.DepthTex is not RenderTexture depth || source.NormTex == null) return;

				UniversalLightData lights = frameData.Get<UniversalLightData>();
				if (lights.additionalLightsCount == 0) return;
				if (!AnyLightReachesEnvironment(lights)) return;

				UpdateHandle(ref depthHandle, depth);
				UpdateHandle(ref normalHandle, source.NormTex);
				for (int eye = 0; eye < 2; eye++)
				{
					worldToDepthClip[eye] = source.Proj[eye] * source.View[eye];
					depthClipToWorld[eye] = source.ViewInv[eye] * source.ProjInv[eye];
				}

				UniversalCameraData camera = frameData.Get<UniversalCameraData>();
				TextureHandle depthTexture = graph.ImportTexture(depthHandle);
				TextureHandle normalTexture = graph.ImportTexture(normalHandle);
				TextureHandle irradiance = graph.CreateTexture(new TextureDesc(depth.width, depth.height)
				{
					dimension = TextureDimension.Tex2DArray,
					slices = 2,
					format = format,
					filterMode = FilterMode.Bilinear,
					wrapMode = TextureWrapMode.Clamp,
					clearBuffer = false,
					name = "Depth Light Irradiance"
				});

				for (int eye = 0; eye < 2; eye++)
				{
					using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<PassData>(
						"Depth Light Irradiance", out PassData data, profilingSampler);

					data.material = material;
					data.properties = properties;
					data.xr = camera.xr;
					data.depth = depth;
					data.normals = source.NormTex;
					data.texelSize = new Vector2(1f / depth.width, 1f / depth.height);
					data.eye = eye;
					// URP only clusters a second view under single-pass stereo. These are the
					// matrices it clusters with: GetGPUProjectionMatrix's flip isn't in the tiles
					data.clusterView = camera.xr.enabled && camera.xr.singlePassEnabled ? eye : 0;
					data.worldToCameraClip = camera.GetProjectionMatrix(data.clusterView) * camera.GetViewMatrix(data.clusterView);
					data.maxLights = maxLights;
					data.renderingLayers = renderingLayers;
					data.depthClipToWorld = depthClipToWorld;

					builder.UseTexture(depthTexture, AccessFlags.Read);
					builder.UseTexture(normalTexture, AccessFlags.Read);
					builder.SetRenderAttachment(irradiance, 0, AccessFlags.WriteAll, 0, eye);
					// single-pass stereo goes off around a draw into one plain slice
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
					{
						data.properties.Clear();
						data.properties.SetTexture(DepthKitDriver.depthTexID, data.depth);
						data.properties.SetTexture(DepthKitDriver.normTexID, data.normals);
						data.properties.SetVector(TexelSizeID, data.texelSize);
						data.properties.SetFloat(EyeID, data.eye);
						data.properties.SetFloat(ClusterViewID, data.clusterView);
						data.properties.SetMatrix(WorldToCameraClipID, data.worldToCameraClip);
						data.properties.SetFloat(MaxLightsID, data.maxLights);
						data.properties.SetInteger(RenderingLayersID, unchecked((int)data.renderingLayers));
						data.properties.SetMatrixArray(DepthClipToWorldID, data.depthClipToWorld);
						data.xr.StopSinglePass(context.cmd);
						context.cmd.DrawProcedural(Matrix4x4.identity, data.material, IrradianceShaderPass,
							MeshTopology.Triangles, 3, 1, data.properties);
						data.xr.StartSinglePass(context.cmd);
					});
				}

				DepthLightFrame frame = frameData.GetOrCreate<DepthLightFrame>();
				frame.irradiance = irradiance;
				frame.worldToDepthClip = worldToDepthClip;
			}

			// the clusters ignore rendering layers, so this skips the pass when no light could
			// light the environment; the shader still checks each light's layers
			private bool AnyLightReachesEnvironment(UniversalLightData lights)
			{
				foreach (VisibleLight light in lights.visibleLights)
				{
					if (light.lightType is not (LightType.Point or LightType.Spot)) continue;
					if (!lights.supportsLightLayers || light.light == null ||
					    (light.light.GetUniversalAdditionalLightData().renderingLayers & renderingLayers) != 0) return true;
				}
				return false;
			}

			private static void UpdateHandle(ref RTHandle handle, RenderTexture texture)
			{
				if (handle != null && handle.rt == texture) return;
				handle?.Release();
				handle = RTHandles.Alloc(texture);
			}

			public void Dispose()
			{
				depthHandle?.Release();
				normalHandle?.Release();
				depthHandle = null;
				normalHandle = null;
			}
		}

		private sealed class CompositePass : ScriptableRenderPass
		{
			private static readonly int IrradianceID = Shader.PropertyToID("_DepthLightIrradiance");

			private readonly Material material;
			private readonly MaterialPropertyBlock properties = new();

			public CompositePass(Material material)
			{
				this.material = material;
				profilingSampler = new ProfilingSampler("Depth Lighting");
			}

			private sealed class PassData
			{
				public Material material;
				public MaterialPropertyBlock properties;
				public TextureHandle irradiance;
				public Matrix4x4[] worldToDepthClip;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				if (!frameData.Contains<DepthLightFrame>()) return;
				DepthLightFrame frame = frameData.Get<DepthLightFrame>();
				if (!frame.irradiance.IsValid()) return;

				UniversalResourceData resources = frameData.Get<UniversalResourceData>();
				UniversalCameraData camera = frameData.Get<UniversalCameraData>();

				using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<PassData>(
					"Depth Lighting", out PassData data, profilingSampler);

				data.material = material;
				data.properties = properties;
				data.irradiance = frame.irradiance;
				data.worldToDepthClip = frame.worldToDepthClip;

				builder.UseTexture(frame.irradiance, AccessFlags.Read);
				builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
				// unused, but matching the transparent pass's attachments keeps this merged
				// into its native render pass
				builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
				if (camera.xr.enabled)
				{
					bool canFoveateIntermediate = true;
#if UNITY_ANDROID || UNITY_WSA
					canFoveateIntermediate = Application.isEditor || XRSystem.GetRenderViewportScale() == 1.0f;
#endif
					builder.EnableFoveatedRasterization(camera.xr.supportsFoveatedRendering &&
					                                   (canFoveateIntermediate || resources.isActiveTargetBackBuffer));
					builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
				}
				builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
				{
					data.properties.Clear();
					data.properties.SetTexture(IrradianceID, (RTHandle)data.irradiance);
					data.properties.SetMatrixArray(WorldToDepthClipID, data.worldToDepthClip);
					// ViewQuad.hlsl's 6-vertex quad
					context.cmd.DrawProcedural(Matrix4x4.identity, data.material, CompositeShaderPass,
						MeshTopology.Triangles, 6, 1, data.properties);
				});
			}
		}
	}
}
