using System.Collections.Generic;
using Anaglyph.XRTemplate.DepthKit;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Anaglyph.DepthKit
{
	public class EnvMeshOcclusionFeature : ScriptableRendererFeature
	{
		public static readonly List<Renderer> AllRenderers = new();

		public Material depthMat;
		public float relativeTexSize = 0.5f;

		private EnvMeshOcclusionPass envMeshOcclusionPass;

		private static readonly int OcclusionActiveID = Shader.PropertyToID("agOcclusionActive");

		public override void Create()
		{
			SetOcclusionShaderActive(false);

			envMeshOcclusionPass = new EnvMeshOcclusionPass(depthMat, relativeTexSize)
			{
				renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
			};

#if UNITY_EDITOR
			EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
		}

		protected override void Dispose(bool disposing)
		{
			SetOcclusionShaderActive(false);
#if UNITY_EDITOR
			EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
			base.Dispose(disposing);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(envMeshOcclusionPass);
		}

#if UNITY_EDITOR
		private void OnPlayModeChanged(PlayModeStateChange state)
		{
			switch (state)
			{
				case PlayModeStateChange.EnteredEditMode:
					SetOcclusionShaderActive(false);
					break;

				case PlayModeStateChange.EnteredPlayMode:
					SetOcclusionShaderActive(true);
					break;
			}
		}
#endif

		private void SetOcclusionShaderActive(bool active)
		{
			Shader.SetGlobalFloat(OcclusionActiveID, active ? 1.0f : 0.0f);
		}

		private class EnvMeshOcclusionPass : ScriptableRenderPass
		{
			private const string PassName = "Environment Mesh Occlusion Feature (RenderGraph)";

			private static readonly int OcclusionTexID = Shader.PropertyToID("agOcclusionTex");

			private readonly Material depthMat;
			private readonly float relativeTexSize;
			private readonly ComputeShader compute;

			public EnvMeshOcclusionPass(Material depthMat, float relativeTexSize)
			{
				this.depthMat = depthMat;
				this.relativeTexSize = relativeTexSize;
			}

			private class PassData
			{
				public TextureHandle occlusionTexHandle;
				public RendererListHandle rendererListHandle;
				public ComputeShader compute;
				public int2 texSize;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameContext)
			{
				using (IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass(passName,
					       out PassData data))
				{
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
						colorFormat = GraphicsFormat.R16_UNorm,
						depthBufferBits = DepthBits.Depth16,
						dimension = TextureDimension.Tex2DArray,
						slices = 2,
						msaaSamples = MSAASamples.None,
						vrUsage = VRTextureUsage.TwoEyes
					};

					data.occlusionTexHandle = graph.CreateTexture(occlusionTexDesc);

					builder.SetRenderAttachmentDepth(data.occlusionTexHandle, AccessFlags.ReadWrite);
					builder.AllowGlobalStateModification(true);
					builder.SetGlobalTextureAfterPass(data.occlusionTexHandle, OcclusionTexID);

					builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
					{
						ctx.cmd.ClearRenderTarget(true, true, Color.black);

						foreach (Renderer r in AllRenderers)
							ctx.cmd.DrawRenderer(r, depthMat);
					});
				}
			}
		}
	}
}