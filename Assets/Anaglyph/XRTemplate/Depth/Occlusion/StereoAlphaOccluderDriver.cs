using System;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.DepthKit
{
	[ExecuteInEditMode]
	public class StereoAlphaOccluderDriver : MonoBehaviour
	{
		private static readonly int OcclusionColorMaskID = Shader.PropertyToID("_OcclusionColorMask");
		[SerializeField] private Material stereoAlphaOccluderMaterial;

		private void Start()
		{
#if UNITY_EDITOR
			stereoAlphaOccluderMaterial.SetFloat(OcclusionColorMaskID, XRSettings.enabled ? 15 : 0);
#else
			stereoAlphaOccluderMaterial.SetFloat(OcclusionColorMaskID, 15);
#endif
		}
	}
}