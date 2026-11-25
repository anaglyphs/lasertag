using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.XR.DepthKit.Occlusion
{
	public class DepthKitEnvironmentOcclusionFeature : ScriptableRendererFeature
	{
		public Shader zDepthShader;
		public Shader depthPrimeShader;
		public float relativeTexSize = 0.5f;
		public float rawDepthMaxDistance = 6;
		[Min(0.001f)] public float occlusionDepthSoftnessMeters = 0.05f;
		public LayerMask OcclusionMeshLayerMask;

		private DepthKitEnvironmentOcclusionPass depthKitEnvironmentOcclusionPass;
		private UnbindOcclusionPass unbindOcclusionPass;
		private Material depthMat;
		private Material primeMat;

		public override void Create()
		{
			DestroyMaterials();

			depthMat = new Material(zDepthShader);
			depthMat.EnableKeyword("AG_OCCLUSION_MATRICES");
			primeMat = new Material(depthPrimeShader);

			depthKitEnvironmentOcclusionPass =
				new DepthKitEnvironmentOcclusionPass(depthMat, primeMat, relativeTexSize, rawDepthMaxDistance,
					occlusionDepthSoftnessMeters, OcclusionMeshLayerMask.value)
				{
					renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
				};
			unbindOcclusionPass = new UnbindOcclusionPass { renderPassEvent = RenderPassEvent.AfterRendering };
		}

		protected override void Dispose(bool disposing)
		{
			DestroyMaterials();
			base.Dispose(disposing);
		}

		private void DestroyMaterials()
		{
			CoreUtils.Destroy(depthMat);
			CoreUtils.Destroy(primeMat);
		}

		// a play session starts with no camera's occlusion bound, even after earlier sessions in the editor
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void UnbindOcclusion() =>
			Shader.SetGlobalVector(DepthKitEnvironmentOcclusionPass.OcclusionTexSizeID, Vector4.zero);

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(depthKitEnvironmentOcclusionPass);
			// editor Scene view and preview cameras would otherwise leave their occlusion bound for game cameras without this feature
			if (renderingData.cameraData.cameraType != CameraType.Game)
				renderer.EnqueuePass(unbindOcclusionPass);
		}

		private class UnbindOcclusionPass : ScriptableRenderPass
		{
			private class PassData { }

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameContext)
			{
				using IUnsafeRenderGraphBuilder builder = graph.AddUnsafePass("Unbind Environment Occlusion", out PassData _);
				builder.AllowPassCulling(false);
				builder.AllowGlobalStateModification(true);
				builder.SetRenderFunc((PassData _, UnsafeGraphContext ctx) =>
					ctx.cmd.SetGlobalVector(DepthKitEnvironmentOcclusionPass.OcclusionTexSizeID, Vector4.zero));
			}
		}

		private class DepthKitEnvironmentOcclusionPass : ScriptableRenderPass
		{
			private const string PassName = "Environment Mesh Occlusion Feature (RenderGraph)";

			private static readonly int OcclusionTexID = Shader.PropertyToID("agOcclusionTex");
			private static readonly int MaxDistanceID = Shader.PropertyToID("_MaxDistance");
			private static readonly int OcclusionVPID = Shader.PropertyToID("agOcclusionVP");
			public static readonly int OcclusionTexSizeID = Shader.PropertyToID("agOcclusionTexSize");
			private static readonly int OcclusionDepthSoftnessID = Shader.PropertyToID("agOcclusionDepthSoftness");
			private static readonly ShaderTagId ShaderTag = new("UniversalForward");

			private readonly Material depthMat;
			private readonly Material rawDepthPrimeMat;
			private readonly float rawDepthMaxDistance;
			private readonly float relativeTexSize;
			private readonly float occlusionDepthSoftnessMeters;
			private readonly int layermask;
			private readonly Matrix4x4[] occlusionVP = new Matrix4x4[2];
			private readonly ComputeShader compute;

			public DepthKitEnvironmentOcclusionPass(Material depthMat, Material rawDepthPrimeMat,
				float relativeTexSize, float rawDepthMaxDistance, float occlusionDepthSoftnessMeters, int layermask)
			{
				this.depthMat = depthMat;
				this.rawDepthPrimeMat = rawDepthPrimeMat;
				this.relativeTexSize = relativeTexSize;
				this.rawDepthMaxDistance = rawDepthMaxDistance;
				this.occlusionDepthSoftnessMeters = Mathf.Max(occlusionDepthSoftnessMeters, 0.001f);
				this.layermask = layermask;
			}

			private class PassData
			{
				public TextureHandle occlusionTexHandle;
				public RendererListHandle rendererListHandle;
				public Material primeMat;
				public ComputeShader compute;
				public int2 texSize;
				public Matrix4x4[] occlusionVP;
				public float depthSoftnessMeters;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameContext)
			{
				using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(passName,
					       out PassData data))
				{
					UniversalRenderingData renderingData = frameContext.Get<UniversalRenderingData>();
					UniversalLightData lightData = frameContext.Get<UniversalLightData>();
					UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
					RenderTextureDescriptor camDesc = cameraData.cameraTargetDescriptor;

					data.texSize.x = Mathf.FloorToInt(camDesc.width * relativeTexSize);
					data.texSize.y = Mathf.FloorToInt(camDesc.height * relativeTexSize);


					TextureDesc occlusionTexDesc = new()
					{
						width = data.texSize.x,
						height = data.texSize.y,
						anisoLevel = 0,
						autoGenerateMips = false,
						colorFormat = GraphicsFormat.None,
						depthBufferBits = DepthBits.Depth16,
						dimension = TextureDimension.Tex2DArray,
						slices = 2,
						msaaSamples = MSAASamples.None,
						vrUsage = VRTextureUsage.TwoEyes
					};

					data.occlusionTexHandle = graph.CreateTexture(occlusionTexDesc);

					// render-texture convention on every platform, independent of how Unity
					// flips the bound projection for the current target
					bool stereo = cameraData.xr.enabled && cameraData.xr.singlePassEnabled;
					for (int eye = 0; eye < 2; eye++)
					{
						int view = stereo ? eye : 0;
						occlusionVP[eye] = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(view), true) *
						                   cameraData.GetViewMatrix(view);
					}
					data.occlusionVP = occlusionVP;
					data.depthSoftnessMeters = occlusionDepthSoftnessMeters;

					DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
						ShaderTag, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
					drawSettings.overrideMaterial = depthMat;
					drawSettings.overrideMaterialPassIndex = 0;

					FilteringSettings filterSettings = new(RenderQueueRange.opaque, layermask);

					RendererListParams listParams = new(renderingData.cullResults, drawSettings, filterSettings);
					data.rendererListHandle = graph.CreateRendererList(listParams);
					builder.UseRendererList(data.rendererListHandle);

					// composite the live sensor depth under the environment meshes.
					// agDepthTex is external to the render graph (set via
					// Shader.SetGlobalTexture on the CPU timeline), so it needs no
					// UseTexture tracking; skip while no sensor depth is bound. Only game cameras sit at the
					// headset's eye, where the prime's single-refinement ray search finds the right surface
					bool primeRawDepth = rawDepthPrimeMat != null && DepthKitDriver.DepthAvailable &&
					                     cameraData.cameraType == CameraType.Game;
					if (primeRawDepth)
						rawDepthPrimeMat.SetFloat(MaxDistanceID, rawDepthMaxDistance);
					data.primeMat = primeRawDepth ? rawDepthPrimeMat : null;

					builder.SetRenderAttachmentDepth(data.occlusionTexHandle, AccessFlags.Write);
					builder.AllowGlobalStateModification(true);
					builder.SetGlobalTextureAfterPass(data.occlusionTexHandle, OcclusionTexID);

					builder.SetRenderFunc((PassData passData, RasterGraphContext ctx) =>
					{
						ctx.cmd.SetGlobalMatrixArray(OcclusionVPID, passData.occlusionVP);
						ctx.cmd.SetGlobalVector(OcclusionTexSizeID, new Vector4(
							1f / passData.texSize.x, 1f / passData.texSize.y, passData.texSize.x, passData.texSize.y));
						ctx.cmd.SetGlobalFloat(OcclusionDepthSoftnessID, passData.depthSoftnessMeters);
						ctx.cmd.ClearRenderTarget(RTClearFlags.Depth, Color.black, 1f, 0);

						// instanceCount stays 1: URP wraps camera passes in
						// XRPass.StartSinglePass, whose SetInstanceMultiplier(2) doubles
						// this draw under Single Pass Instanced; the shader's stereo
						// macros route each instance to its eye's array slice
						if (passData.primeMat != null)
							ctx.cmd.DrawProcedural(Matrix4x4.identity, passData.primeMat, 0,
								MeshTopology.Triangles, 6, 1);

						ctx.cmd.DrawRendererList(passData.rendererListHandle);
					});
				}
			}
		}
	}
}