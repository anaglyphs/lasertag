using UnityEngine;

namespace Anaglyph.Lasertag.Gallery
{
	[ExecuteAlways]
	public class ShaderConfig : MonoBehaviour
	{
		public Color wallColor = Color.white;
		public Color fogColorTop = Color.white;
		public Color fogColorBottom = Color.white;
		public float fogMaxDist = 10;

		private void Update()
		{
			Shader.SetGlobalColor(nameof(wallColor), wallColor);
			Shader.SetGlobalColor(nameof(fogColorTop), fogColorTop);
			Shader.SetGlobalColor(nameof(fogColorBottom), fogColorBottom);
			Shader.SetGlobalFloat(nameof(fogMaxDist), fogMaxDist);
		}
	}
}
