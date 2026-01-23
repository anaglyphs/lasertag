using Anaglyph.XRTemplate;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[System.Serializable]
public class OcclusionComputeFeature : ScriptableRendererFeature
{
	private class OcclusionComputePass : ScriptableRenderPass
	{
		public ComputeShader shader;
		public RenderTexture occlusionTex;
		public string kernelName = "OcclusionMarch";

		private static readonly int camViewID = Shader.PropertyToID("camView");
		private static readonly int camProjID = Shader.PropertyToID("camProj");
		private static readonly int camInvViewID = Shader.PropertyToID("camInvView");
		private static readonly int camInvProjID = Shader.PropertyToID("camInvProj");
		private static readonly int occlusionTexID = Shader.PropertyToID("agOcclusionTex");
		private static readonly int raymarchVolumeID = Shader.PropertyToID("raymarchVolume");

		private int kernelIndex = -1;

		public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
		{
			// No color/depth targets to configure since compute writes directly to a texture.
		}

		public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
		{
			Camera cam = renderingData.cameraData.camera;

			if (kernelIndex < 0) kernelIndex = shader.FindKernel(kernelName);

			// collect stereo matrices for both eyes
			Matrix4x4[] camViewMats = new Matrix4x4[2];
			Matrix4x4[] camInvViewMats = new Matrix4x4[2];
			Matrix4x4[] camProjMats = new Matrix4x4[2];
			Matrix4x4[] camInvProjMats = new Matrix4x4[2];

			for (int i = 0; i < 2; i++)
			{
				Camera.StereoscopicEye eye = (Camera.StereoscopicEye)i;
				Matrix4x4 viewMat = cam.GetStereoViewMatrix(eye);
				camViewMats[i] = viewMat;
				camInvViewMats[i] = viewMat.inverse;

				Matrix4x4 projMat = cam.GetStereoProjectionMatrix(eye);
				Matrix4x4 projMatGL = GL.GetGPUProjectionMatrix(projMat, false);
				camProjMats[i] = projMatGL;
				camInvProjMats[i] = projMatGL.inverse;
			}

			// set matrices on compute shader
			shader.SetMatrixArray(camViewID, camViewMats);
			shader.SetMatrixArray(camInvViewID, camInvViewMats);
			shader.SetMatrixArray(camProjID, camProjMats);
			shader.SetMatrixArray(camInvProjID, camInvProjMats);

			// Bind the target texture to the compute shader for the kernel
			shader.SetTexture(kernelIndex, occlusionTexID, occlusionTex);
			shader.SetTexture(kernelIndex, raymarchVolumeID, EnvironmentMapper.Instance.Volume);

			// Dispatch compute
			// NOTE: change numThreads to match your [numthreads(x,y,1)] declaration in the compute shader.
			int numThreadsX = 8;
			int numThreadsY = 8;
			int groupsX = Mathf.CeilToInt((float)occlusionTex.width / numThreadsX);
			int groupsY = Mathf.CeilToInt((float)occlusionTex.height / numThreadsY);

			// It's safe to use a CommandBuffer to record the dispatch; this keeps it on the GPU frame timeline
			CommandBuffer cmd = CommandBufferPool.Get("OcclusionComputeDispatch");
			cmd.DispatchCompute(shader, kernelIndex, groupsX, groupsY, 2);

			// make the occlusion texture available as a global shader texture (replicates your Shader.SetGlobalTexture call)
			cmd.SetGlobalTexture(occlusionTexID, occlusionTex);

			context.ExecuteCommandBuffer(cmd);
			CommandBufferPool.Release(cmd);
		}

		public override void FrameCleanup(CommandBuffer cmd)
		{
			// nothing to clean up
		}
	}

	[System.Serializable]
	public class OcclusionSettings
	{
		public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
		public ComputeShader computeShader = null;
		public RenderTexture occlusionTexture = null;
		public string kernelName = "OcclusionMarch";
	}

	public OcclusionSettings settings = new();

	private OcclusionComputePass m_ScriptablePass;

	public override void Create()
	{
		m_ScriptablePass = new OcclusionComputePass
		{
			shader = settings.computeShader,
			occlusionTex = settings.occlusionTexture,
			kernelName = settings.kernelName,
			renderPassEvent = settings.renderPassEvent
		};
	}

	// This is called every frame by the renderer; add the pass to the renderer
	public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
	{
		if (settings.computeShader == null || settings.occlusionTexture == null) return;
		renderer.EnqueuePass(m_ScriptablePass);
	}
}