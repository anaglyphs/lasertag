// using UnityEngine;
// using UnityEngine.Rendering.Universal;
// using UnityEngine.Rendering;
// using System.Collections;
//
// namespace Anaglyph.XRTemplate
// {
// 	[DefaultExecutionOrder(500)]
// 	public class XRConfigurer : MonoBehaviour
// 	{
// 		//[SerializeField] private float renderScale = 1.0f;
// 		[SerializeField] private OVRManager.FoveatedRenderingLevel foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.Low;
// 		[SerializeField] private bool useDynamicFoveatedRendering = true;
// 		[SerializeField] private float framerateTarget = 72f;
//
// 		[SerializeField] private OVRManager.ProcessorPerformanceLevel suggestedCpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
// 		[SerializeField] private OVRManager.ProcessorPerformanceLevel suggestedGpuPerfLevel = OVRManager.ProcessorPerformanceLevel.SustainedHigh;
//
// 		//[Header("Should only be 0, 2, 4, or 8!")]
// 		//[SerializeField] private ushort antialiasingMsaa = 4;
//
// 		void Start()
// 		{
//
// 			StartCoroutine(WaitForOVRManagerInit());
// 		}
//
// 		private IEnumerator WaitForOVRManagerInit()
// 		{
// 			while (OVRManager.instance == null)
// 				yield return null;
//
// 			OVRManager.useDynamicFoveatedRendering = useDynamicFoveatedRendering;
// 			OVRManager.foveatedRenderingLevel = foveatedRenderingLevel;
// 			OVRManager.suggestedCpuPerfLevel = suggestedCpuPerfLevel;
// 			OVRManager.suggestedGpuPerfLevel = suggestedGpuPerfLevel;
//
// 			OVRPlugin.systemDisplayFrequency = framerateTarget;
// 		}
// 	}
// }
