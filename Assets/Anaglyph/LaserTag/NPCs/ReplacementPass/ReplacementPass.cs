using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag.NPCs.ReplacementPass
{
	public class ReplacementPass : ScriptableRenderPass
	{
		Material overrideMaterial;

		LayerMask mask;
		string shaderTag;

		public ReplacementPass(Shader shader, LayerMask layerMask, string tag)
		{
			overrideMaterial = shader != null ? new Material(shader) : null;
			mask = layerMask;
			shaderTag = tag;
		}

		public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			ConfigureClear(ClearFlag.All, Color.black);
		}

		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			if (overrideMaterial == null)
				return;

			var sorting = new SortingSettings(renderingData.cameraData.camera)
			{
				criteria = SortingCriteria.CommonOpaque
			};

			var drawSettings = new DrawingSettings(new ShaderTagId(shaderTag), sorting)
			{
				overrideMaterial = overrideMaterial
			};

			var filterSettings = new FilteringSettings(RenderQueueRange.opaque, mask);

			context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filterSettings);
		}

		public override void OnCameraCleanup(CommandBuffer cmd)
		{
			// Do not release targetHandle here if it's a persistent RT
			// Only release it when replaced (in Setup)
		}
	}
}