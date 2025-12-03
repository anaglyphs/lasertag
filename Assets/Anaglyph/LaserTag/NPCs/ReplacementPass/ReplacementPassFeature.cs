using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag.NPCs.ReplacementPass
{
	public class ReplacementPassFeature : ScriptableRendererFeature
	{
		[System.Serializable]
		public class Settings
		{
			public Shader overrideShader;
			public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;
			public LayerMask layerMask = ~0;
			public string shaderTag = "UniversalForward";
		}

		public Settings settings = new Settings();

		ReplacementPass pass;

		public override void Create()
		{
			pass = new ReplacementPass(
				settings.overrideShader,
				settings.layerMask,
				settings.shaderTag
			);
			pass.renderPassEvent = settings.passEvent;
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (settings.overrideShader == null)
				return;
			
			renderer.EnqueuePass(pass);
		}
	}
}