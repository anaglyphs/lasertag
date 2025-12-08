using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

namespace Anaglyph.DepthKit
{
	public class OcclusionRenderGraphFeature : ScriptableRendererFeature
	{
		public ComputeShader shader;
		public RenderTexture volume;
		public RenderTexture depthTexOut;
		
		private OcclusionPass pass;

		public override void Create()
		{
			pass = new OcclusionPass
			{
				Compute = shader,
				DepthTexOut = depthTexOut,
				Volume = volume,
			};
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			renderer.EnqueuePass(pass);
		}
		
		class OcclusionPass : ScriptableRenderPass
		{
			private const string PassName = "Occlusion Compute (RenderGraph)";

			public ComputeShader Compute;
			public RenderTexture DepthTexOut;
			public RenderTexture Volume;

			private static readonly int CamViewID = Shader.PropertyToID("camView");
			private static readonly int CamProjID = Shader.PropertyToID("camProj");
			private static readonly int CamInvViewID = Shader.PropertyToID("camInvView");
			private static readonly int CamInvProjID = Shader.PropertyToID("camInvProj");
			private static readonly int OcclusionTexID = Shader.PropertyToID("agOcclusionTex");
			private static readonly int VolumeID = Shader.PropertyToID("raymarchVolume");

			private int kernel;
			
			private class PassData
			{
				public ComputeShader Compute;
				public int Kernel;
				public TextureHandle DepthTexHandle;
				public TextureHandle VolumeHandle;

				public Matrix4x4[] View;
				public Matrix4x4[] InvView;
				public Matrix4x4[] Proj;
				public Matrix4x4[] InvProj;
			}

			public OcclusionPass()
			{
				renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				if (cameraData.isPreviewCamera) return;
				if (!Compute) return;
				
				Camera cam = cameraData.camera;

				if (kernel == 0) kernel = Compute.FindKernel("OcclusionMarch");

				using IComputeRenderGraphBuilder builder = graph.AddComputePass(PassName, out PassData passData);
				
				passData.View = new Matrix4x4[2];
				passData.InvView = new Matrix4x4[2];
				passData.Proj = new Matrix4x4[2];
				passData.InvProj = new Matrix4x4[2];

				for (int i = 0; i < 2; i++)
				{
					Camera.StereoscopicEye eye = (Camera.StereoscopicEye)i;
					
					Matrix4x4 view = cam.GetStereoViewMatrix(eye);
					Matrix4x4 proj = GL.GetGPUProjectionMatrix(
						cam.GetStereoProjectionMatrix(eye), false
					);

					passData.View[i] = view;
					passData.InvView[i] = view.inverse;
					passData.Proj[i] = proj;
					passData.InvProj[i] = proj.inverse;
				}

				passData.Compute = Compute;
				passData.Kernel = kernel;
				passData.DepthTexHandle = graph.ImportTexture(RTHandles.Alloc(DepthTexOut));
				passData.VolumeHandle = graph.ImportTexture(RTHandles.Alloc(Volume));

				builder.UseTexture(passData.VolumeHandle, AccessFlags.Read);
				builder.UseTexture(passData.DepthTexHandle, AccessFlags.Write);
				builder.AllowGlobalStateModification(true);

				builder.SetRenderFunc((PassData data, ComputeGraphContext ctx) =>
				{
					int width = DepthTexOut.width;
					int height = DepthTexOut.height;

					ctx.cmd.SetComputeMatrixArrayParam(data.Compute, CamViewID, data.View);
					ctx.cmd.SetComputeMatrixArrayParam(data.Compute, CamInvViewID, data.InvView);
					ctx.cmd.SetComputeMatrixArrayParam(data.Compute, CamProjID, data.Proj);
					ctx.cmd.SetComputeMatrixArrayParam(data.Compute, CamInvProjID, data.InvProj);

					ctx.cmd.SetComputeTextureParam(data.Compute, data.Kernel, VolumeID, data.VolumeHandle);
					ctx.cmd.SetComputeTextureParam(data.Compute, data.Kernel, OcclusionTexID, data.DepthTexHandle);

					int tx = Mathf.CeilToInt(width / 8.0f);
					int ty = Mathf.CeilToInt(height / 8.0f);

					ctx.cmd.DispatchCompute(data.Compute, data.Kernel, tx, ty, 2);
					
					ctx.cmd.SetGlobalTexture(OcclusionTexID, data.DepthTexHandle);
				});
			}
		}
	}
}
