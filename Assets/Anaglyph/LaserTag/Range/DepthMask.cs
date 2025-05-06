using Meta.XR.EnvironmentDepth;
using System;
using UnityEngine;

namespace Anaglyph.Lasertag.Range
{
    public class DepthMask : MonoBehaviour
    {
		private EnvironmentDepthManager depthManager;

		private void Awake()
		{
			depthManager = FindAnyObjectByType<EnvironmentDepthManager>(FindObjectsInactive.Include);
			TryGetComponent(out MeshFilter meshFilter);
			GetComponent<MeshRenderer>().enabled = false;

			depthManager.MaskMeshFilters.Add(meshFilter);
		}
	}
}
