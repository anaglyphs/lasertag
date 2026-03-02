using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Serialization;
using UnityEngine.XR;

namespace Anaglyph.DepthKit
{
	public class MeshOcclusionFeature : ScriptableRendererFeature
	{
		public static readonly List<Renderer> AllRenderers = new();

		private RenderTexture occlusionTex;
		public Material depthMat;
		public float relativeTexSize = 0.5f;

		private Pass pass;

		public override void Create()
		{
			int width = XRSettings.eyeTextureWidth;
			int height = XRSettings.eyeTextureHeight;

			width = Mathf.FloorToInt(width * relativeTexSize);
			height = Mathf.FloorToInt(height * relativeTexSize);

			occlusionTex = new RenderTexture(width, height, 0, GraphicsFormat.None, 1)
			{
				depthStencilFormat = GraphicsFormat.D16_UNorm,
				dimension = TextureDimension.Tex2DArray,
				volumeDepth = 2,
				enableRandomWrite = true
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(pass);
		}

		private class Pass : ScriptableRenderPass
		{
			private const string PassName = "Mesh Occlusion Feature (RenderGraph)";

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