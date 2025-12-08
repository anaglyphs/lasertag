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
				// UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				// UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
				// UniversalLightData lightData = frameData.Get<UniversalLightData>();

				using var builder = graph.AddRasterRenderPass<PassData>(PassName, out var passData);

				builder.AllowGlobalStateModification(true);
				
				passData.DepthTexHandle = graph.ImportTexture(RTHandles.Alloc(depthTexOut));
				
				builder.SetRenderAttachmentDepth(passData.DepthTexHandle, AccessFlags.Write);
				
				builder.SetGlobalTextureAfterPass(passData.DepthTexHandle, OcclusionTexID);

				builder.SetRenderFunc((PassData _, RasterGraphContext ctx) =>
				{
					ctx.cmd.ClearRenderTarget(true, true, Color.black);

					foreach (Renderer r in AllRenderers)
						ctx.cmd.DrawRenderer(r, depthMat);
				});
			}
		}
	}
}
