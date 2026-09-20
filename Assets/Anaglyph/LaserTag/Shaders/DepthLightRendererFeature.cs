using Anaglyph.XR.DepthKit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag.Shaders
{
	[DisallowMultipleRendererFeature("Depth Lighting")]
	public class DepthLightingRendererFeature : ScriptableRendererFeature
	{
		[SerializeField, Tooltip("Fullscreen depth lighting shader. Requires Forward+ and Render Graph.")]
		private Shader shader;
		[SerializeField] private RenderingLayerMask renderingLayers = RenderingLayerMask.defaultRenderingLayerMask;

		private Material material;
		private DepthLightingPass pass;
		private static bool globallyEnabled = true;

		public static void SetGloballyEnabled(bool value) => globallyEnabled = value;

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => globallyEnabled = true;

		public override void Create()
		{
			pass?.Dispose();
			pass = null;
			CoreUtils.Destroy(material);
			material = shader != null ? CoreUtils.CreateEngineMaterial(shader) : null;
			if (material == null) return;

			pass = new DepthLightingPass(material)
			{
				renderPassEvent = RenderPassEvent.AfterRenderingSkybox
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (pass == null || !globallyEnabled || !DepthKitDriver.DepthAvailable) return;
			if (renderingData.cameraData.cameraType != CameraType.Game ||
			    renderingData.cameraData.renderType != CameraRenderType.Base) return;

			pass.renderingLayers = renderingLayers.value;
			renderer.EnqueuePass(pass);
		}

		protected override void Dispose(bool disposing)
		{
			pass?.Dispose();
			pass = null;
			CoreUtils.Destroy(material);
			material = null;
		}

		private sealed class DepthLightingPass : ScriptableRenderPass
		{
			private static readonly int RenderingLayersID = Shader.PropertyToID("_EnvironmentRenderingLayers");
			private static readonly int WorldToDepthClipID = Shader.PropertyToID("_WorldToDepthClip");
			private static readonly int DepthClipToWorldID = Shader.PropertyToID("_DepthClipToWorld");

			private readonly Material material;
			private readonly MaterialPropertyBlock properties = new();
			private RTHandle depthHandle;
			private RTHandle normalHandle;

			public uint renderingLayers;

			public DepthLightingPass(Material material)
			{
				this.material = material;
				profilingSampler = new ProfilingSampler("Depth Lighting");
			}

			private sealed class PassData
			{
				public Material material;
				public MaterialPropertyBlock properties;
				public Texture depth;
				public Texture normals;
				public uint renderingLayers;
				public readonly Matrix4x4[] worldToDepthClip = new Matrix4x4[2];
				public readonly Matrix4x4[] depthClipToWorld = new Matrix4x4[2];
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				DepthKitDriver source = DepthKitDriver.Instance;
				if (source == null || source.DepthTex is not RenderTexture depth || source.NormTex == null) return;

				UniversalLightData lights = frameData.Get<UniversalLightData>();
				if (lights.additionalLightsCount == 0) return;
				bool hasLocalLight = false;
				foreach (VisibleLight light in lights.visibleLights)
				{
					if (light.lightType is not (LightType.Point or LightType.Spot)) continue;
					hasLocalLight = true;
					break;
				}
				if (!hasLocalLight) return;

				UpdateHandle(ref depthHandle, depth);
				UpdateHandle(ref normalHandle, source.NormTex);

				UniversalResourceData resources = frameData.Get<UniversalResourceData>();
				UniversalCameraData camera = frameData.Get<UniversalCameraData>();

				using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<PassData>(
					"Depth Lighting", out PassData data, profilingSampler);

				data.material = material;
				data.properties = properties;
				data.depth = depth;
				data.normals = source.NormTex;
				data.renderingLayers = renderingLayers;
				for (int eye = 0; eye < 2; eye++)
				{
					data.worldToDepthClip[eye] = source.Proj[eye] * source.View[eye];
					data.depthClipToWorld[eye] = source.ViewInv[eye] * source.ProjInv[eye];
				}

				builder.UseTexture(graph.ImportTexture(depthHandle), AccessFlags.Read);
				builder.UseTexture(graph.ImportTexture(normalHandle), AccessFlags.Read);
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
					data.properties.SetTexture(DepthKitDriver.depthTexID, data.depth);
					data.properties.SetTexture(DepthKitDriver.normTexID, data.normals);
					data.properties.SetInteger(RenderingLayersID, unchecked((int)data.renderingLayers));
					data.properties.SetMatrixArray(WorldToDepthClipID, data.worldToDepthClip);
					data.properties.SetMatrixArray(DepthClipToWorldID, data.depthClipToWorld);
					context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0,
						MeshTopology.Triangles, 3, 1, data.properties);
				});
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
	}
}
