using System;
using UnityEngine;

namespace Anaglyph.DepthKit
{
	public class OcclusionMesh : MonoBehaviour
	{
		private new Renderer renderer;

		private void OnEnable()
		{
			if (TryGetComponent(out renderer))
				EnvMeshOcclusionFeature.AllRenderers.Add(renderer);
		}

		private void OnDisable()
		{
			EnvMeshOcclusionFeature.AllRenderers.Remove(renderer);
		}
	}
}