using System;
using UnityEngine;

namespace Anaglyph.DepthKit
{
    public class OcclusionMesh : MonoBehaviour
    {
	    private new Renderer renderer;
	    
	    private void OnEnable()
	    {
		    if(TryGetComponent(out renderer))
				MeshOcclusionFeature.AllRenderers.Add(renderer);
	    }

	    private void OnDisable()
	    {
		    MeshOcclusionFeature.AllRenderers.Remove(renderer);
	    }
    }
}
