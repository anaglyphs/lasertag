// using System;
// using UnityEngine;
// using UnityEngine.Experimental.Rendering;
// using UnityEngine.Rendering;
//
// namespace Anaglyph.LaserTag.NPCs.ReplacementPass
// {
// 	public class OcclusionTexRenderCam : MonoBehaviour
// 	{
// 		[SerializeField] private Camera mainCam;
// 		[SerializeField] private Camera auxCam;
//
// 		[SerializeField] private LayerMask layerMask;
// 		[SerializeField] private RenderTexture targetTex;
// 		
// 	
// 		void OnEnable()
// 		{
// 			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
// 		}
//
// 		private async void Start()
// 		{
// 			RenderTextureDescriptor desc = new();
// 			desc.width = 1024;
// 			desc.height = 1024;
// 			desc.dimension = TextureDimension.Tex2DArray;
// 			desc.graphicsFormat = GraphicsFormat.R16G16B16A16_UNorm;
// 			desc.depthStencilFormat = GraphicsFormat.D16_UNorm;
// 			desc.vrUsage = VRTextureUsage.TwoEyes;
// 			desc.volumeDepth = 2;
// 			desc.msaaSamples = 1;
// 			desc.mipCount = 1;
//
// 			targetTex = new RenderTexture(desc);
//
// 			await Awaitable.NextFrameAsync();
// 			
// 			Shader.SetGlobalTexture("agOcclusionTex", targetTex);
// 			
// 			auxCam.targetTexture = targetTex;
// 		}
//
// 		void OnDisable()
// 		{
// 			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
// 		}
//
// 		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
// 		{
// 			auxCam.CopyFrom(mainCam);
// 			
// 			auxCam.usePhysicalProperties = false;
// 			
// 			auxCam.cullingMask = layerMask;
// 			auxCam.depth = -10;
//
// 			auxCam.usePhysicalProperties = false;
// 			auxCam.projectionMatrix = mainCam.projectionMatrix;
// 			
// 			auxCam.fieldOfView = mainCam.fieldOfView;
// 			auxCam.cameraType = CameraType.VR;
// 			// auxCam.gateFit = Camera.GateFitMode.None;
//
// 			auxCam.gateFit = Camera.GateFitMode.Horizontal;
// 			
// 			auxCam.SetStereoProjectionMatrix(
// 				Camera.StereoscopicEye.Left,
// 				mainCam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
// 			);
//
// 			auxCam.SetStereoProjectionMatrix(
// 				Camera.StereoscopicEye.Right,
// 				mainCam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)
// 			);
// 			
// 			// auxCam.stereoTargetEye = StereoTargetEyeMask.Both;
// 		}
// 	}
// }
