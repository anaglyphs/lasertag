using Anaglyph.XR.DepthKit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag.Shaders
{
	// Lights the depth map at its own resolution before the opaques (IrradiancePass, once
	// per eye slice), then composites that irradiance into the eye buffer after the skybox
	// (CompositePass), so the light loop runs per depth texel instead of per eye pixel
	[DisallowMultipleRendererFeature("Depth Lighting")]
	public class DepthLightingRendererFeature : ScriptableRendererFeature
	{
		[SerializeField, Tooltip("Fullscreen depth lighting shader. Requires Render Graph.")]
		private Shader shader;
		[SerializeField] private RenderingLayerMask renderingLayers = RenderingLayerMask.defaultRenderingLayerMask;
		[SerializeField, Min(1), Tooltip("Most point/spot lights evaluated per frame, in URP's visible-light order.")]
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
				renderPassEvent = RenderPassEvent.AfterRenderingSkybox
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (irradiancePass == null || !globallyEnabled || !DepthKitDriver.DepthAvailable) return;
			if (renderingData.cameraData.cameraType != CameraType.Game ||
			    renderingData.cameraData.renderType != CameraRenderType.Base) return;

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
			public TextureHandle depth;
			public TextureHandle irradiance;
			public Matrix4x4[] worldToDepthClip;
			public Matrix4x4[] depthClipToWorld;

			public override void Reset()
			{
				depth = TextureHandle.nullHandle;
				irradiance = TextureHandle.nullHandle;
				worldToDepthClip = null;
				depthClipToWorld = null;
			}
		}

		private sealed class IrradiancePass : ScriptableRenderPass
		{
			private static readonly int TexelSizeID = Shader.PropertyToID("_DepthLightTexelSize");
			private static readonly int EyeID = Shader.PropertyToID("_DepthLightEye");
			private static readonly int LightCountID = Shader.PropertyToID("_DepthLightCount");
			private static readonly int LightIndicesID = Shader.PropertyToID("_DepthLightIndices");

			private readonly Material material;
			private readonly MaterialPropertyBlock properties = new();
			private readonly GraphicsFormat format;
			// sized to the shader's MAX_VISIBLE_LIGHTS array, which a property block fixes on first set
			private readonly float[] lightIndices = new float[UniversalRenderPipeline.maxVisibleAdditionalLights];
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
				public int lightCount;
				public float[] lightIndices;
				public Matrix4x4[] depthClipToWorld;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				DepthKitDriver source = DepthKitDriver.Instance;
				if (source == null || source.DepthTex is not RenderTexture depth || source.NormTex == null) return;

				UniversalLightData lights = frameData.Get<UniversalLightData>();
				if (lights.additionalLightsCount == 0) return;
				int lightCount = CollectLights(lights);
				if (lightCount == 0) return;

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
					data.lightCount = lightCount;
					data.lightIndices = lightIndices;
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
						data.properties.SetFloat(LightCountID, data.lightCount);
						data.properties.SetFloatArray(LightIndicesID, data.lightIndices);
						data.properties.SetMatrixArray(DepthClipToWorldID, data.depthClipToWorld);
						data.xr.StopSinglePass(context.cmd);
						context.cmd.DrawProcedural(Matrix4x4.identity, data.material, IrradianceShaderPass,
							MeshTopology.Triangles, 3, 1, data.properties);
						data.xr.StartSinglePass(context.cmd);
					});
				}

				DepthLightFrame frame = frameData.GetOrCreate<DepthLightFrame>();
				frame.depth = depthTexture;
				frame.irradiance = irradiance;
				frame.worldToDepthClip = worldToDepthClip;
				frame.depthClipToWorld = depthClipToWorld;
			}

			// Fills lightIndices with the additional-light array slots of the point and spot
			// lights that reach the environment's rendering layers. URP packs that array from
			// visibleLights in order, skipping only the main light
			private int CollectLights(UniversalLightData lights)
			{
				int capacity = Mathf.Min(maxLights, lightIndices.Length);
				int mainLight = lights.mainLightIndex;
				int count = 0;
				for (int i = 0; i < lights.visibleLights.Length && count < capacity; i++)
				{
					if (i == mainLight) continue;
					int slot = mainLight >= 0 && mainLight < i ? i - 1 : i;
					if (slot >= lightIndices.Length) break;

					VisibleLight light = lights.visibleLights[i];
					if (light.lightType is not (LightType.Point or LightType.Spot)) continue;
					if (lights.supportsLightLayers && light.light != null &&
					    (light.light.GetUniversalAdditionalLightData().renderingLayers & renderingLayers) == 0) continue;

					lightIndices[count++] = slot;
				}
				return count;
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
				public TextureHandle depth;
				public TextureHandle irradiance;
				public Matrix4x4[] worldToDepthClip;
				public Matrix4x4[] depthClipToWorld;
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
				data.depth = frame.depth;
				data.irradiance = frame.irradiance;
				data.worldToDepthClip = frame.worldToDepthClip;
				data.depthClipToWorld = frame.depthClipToWorld;

				builder.UseTexture(frame.depth, AccessFlags.Read);
				builder.UseTexture(frame.irradiance, AccessFlags.Read);
				builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
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
					data.properties.SetTexture(DepthKitDriver.depthTexID, (RTHandle)data.depth);
					data.properties.SetTexture(IrradianceID, (RTHandle)data.irradiance);
					data.properties.SetMatrixArray(WorldToDepthClipID, data.worldToDepthClip);
					data.properties.SetMatrixArray(DepthClipToWorldID, data.depthClipToWorld);
					context.cmd.DrawProcedural(Matrix4x4.identity, data.material, CompositeShaderPass,
						MeshTopology.Triangles, 3, 1, data.properties);
				});
			}
		}
	}
}
