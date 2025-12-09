using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Anaglyph.DepthKit
{
	public class MeshOcclusionFeature : ScriptableRendererFeature
	{
		public static readonly List<Renderer> AllRenderers = new();

		public RenderTexture depthTexOut;
		public Material depthMat;

		private Pass pass;

		public override void Create()
		{
			depthTexOut.vrUsage = VRTextureUsage.TwoEyes;
			pass = new Pass(depthTexOut, depthMat);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(pass);
		}

		private class Pass : ScriptableRenderPass
		{
			private const string PassName = "Occlusion Feature (RenderGraph)";

			private readonly RenderTexture depthTexOut;
			private readonly Material depthMat;

			private static readonly int OcclusionTexID = Shader.PropertyToID("agOcclusionTex");

			private class PassData
			{
				public TextureHandle DepthTexHandle;
			}

			public Pass(RenderTexture depthTexOut, Material depthMat)
			{
				this.depthTexOut = depthTexOut;
				this.depthMat = depthMat;

				renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				using IRasterRenderGraphBuilder builder =
					graph.AddRasterRenderPass(PassName, out PassData passData);

				passData.DepthTexHandle = graph.ImportTexture(RTHandles.Alloc(depthTexOut));

				builder.SetRenderAttachmentDepth(passData.DepthTexHandle, AccessFlags.Write);
				builder.AllowGlobalStateModification(true);
				builder.SetGlobalTextureAfterPass(passData.DepthTexHandle, OcclusionTexID);

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