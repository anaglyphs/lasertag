using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System;

public class ProceduralDrawFeature : ScriptableRendererFeature
{
	public RenderPassEvent order = RenderPassEvent.BeforeRenderingTransparents;

	private ProceduralDrawPass pass;

	public static event Action<RasterCommandBuffer> Draw = delegate { };

	public override void Create()
	{
		pass = new();
		pass.renderPassEvent = order;
	}

	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		renderer.EnqueuePass(pass);
	}

	class ProceduralDrawPass : ScriptableRenderPass
	{
		private class PassData
		{
			// Create a field to store the list of objects to draw
			public RendererListHandle rendererListHandle;
		}

		public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
		{
			using (var builder = renderGraph.AddRasterRenderPass<PassData>("Redraw objects", out var passData))
			{
				// Get the data needed to create the list of objects to draw
				UniversalRenderingData renderingData = frameContext.Get<UniversalRenderingData>();
				UniversalCameraData cameraData = frameContext.Get<UniversalCameraData>();
				UniversalLightData lightData = frameContext.Get<UniversalLightData>();
				SortingCriteria sortFlags = cameraData.defaultOpaqueSortFlags;
				RenderQueueRange renderQueueRange = RenderQueueRange.opaque;
				FilteringSettings filterSettings = new FilteringSettings(renderQueueRange, ~0);

				// Redraw only objects that have their LightMode tag set to UniversalForward 
				ShaderTagId shadersToOverride = new ShaderTagId("UniversalForward");

				// Create drawing settings
				DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(shadersToOverride, renderingData, cameraData, lightData, sortFlags);

				// Create the list of objects to draw
				var rendererListParameters = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);

				// Convert the list to a list handle that the render graph system can use
				passData.rendererListHandle = renderGraph.CreateRendererList(rendererListParameters);

				// Set the render target as the color and depth textures of the active camera texture
				UniversalResourceData resourceData = frameContext.Get<UniversalResourceData>();
				builder.UseRendererList(passData.rendererListHandle);
				builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
				builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);

				builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
			}
		}

		static void ExecutePass(PassData data, RasterGraphContext context)
		{
			Draw.Invoke(context.cmd);
		}
	}
}