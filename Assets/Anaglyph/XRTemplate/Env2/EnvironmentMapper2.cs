using Anaglyph.XRTemplate.DepthKit;
using System;
using UnityEngine;

namespace Anaglyph.XRTemplate
{
	public class EnvironmentMapper2 : MonoBehaviour
	{
		[SerializeField] private ComputeShader compute = null;
		
		private const int WIDTH = 512;
		private const int HEIGHT = 128;
		private const float VOX_PER_M = 10f;

		private static int ID(string str) => Shader.PropertyToID(str);
		private static readonly int _Volume = ID(nameof(_Volume));

		[SerializeField] private RenderTexture volume;

		private ComputeKernel Clear;
		private ComputeKernel Scan;

		private void Start()
		{
			Clear = new(compute, nameof(Clear));
			Clear.Set(_Volume, volume);

			Clear.Dispatch(WIDTH, HEIGHT, WIDTH);

			Scan = new(compute, nameof(Scan)); 
			Scan.Set(_Volume, volume);
		}

		private void FixedUpdate()
		{
			var depthTex = Shader.GetGlobalTexture(DepthKitDriver.agDepthTex_ID);

			if (depthTex == null)
				return;

			Scan.Set(DepthKitDriver.agDepthTex_ID, depthTex);
			Scan.Dispatch(WIDTH, HEIGHT, WIDTH);
		}
	}
}
