using System;
using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.DepthKit.Meshing
{
	public class Occlusion : MonoBehaviour
	{
		[SerializeField] private ComputeShader compute;

		private ushort[] a;

		private static EnvironmentMapper mapper => EnvironmentMapper.Instance;

		private void Start()
		{
			RenderTexture occlusionTex = mapper.OcclusionTex;
			a = new ushort[occlusionTex.width * occlusionTex.height];
			Loop();
		}

		private void OnEnable()
		{
			if(didStart)
				Loop();
		}

		private async void Loop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(0.1f);

				await EnvironmentMapper.Instance.ComputeOcclusionTexture(a);
			}
		}

		public static void FrustumRays(Camera cam, int xCount, int yCount, Ray[] rays)
		{
			int index = 0;

			for (int y = 0; y < yCount; y++)
			{
				float vy = yCount == 1 ? 0.5f : (float)y / (yCount - 1);

				for (int x = 0; x < xCount; x++)
				{
					float vx = xCount == 1 ? 0.5f : (float)x / (xCount - 1);

					Vector3 viewportPoint = new(vx, vy, 0f);
					rays[index++] = cam.ViewportPointToRay(viewportPoint);
				}
			}
		}
	}
}