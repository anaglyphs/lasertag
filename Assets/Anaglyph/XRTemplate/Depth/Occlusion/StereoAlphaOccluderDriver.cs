using System;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.DepthKit
{
	[ExecuteInEditMode]
	public class StereoAlphaOccluderDriver : MonoBehaviour
	{
		[SerializeField] private Material stereoAlphaOccluderMaterial;

		private void Start()
		{
#if UNITY_EDITOR
			stereoAlphaOccluderMaterial.SetFloat("_OcclusionColorMask", XRSettings.enabled ? 15 : 0);
#else
			stereoAlphaOccluderMaterial.SetFloat("_OcclusionColorMask", 15);
#endif
		}
	}
}