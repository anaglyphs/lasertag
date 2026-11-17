using System;
using System.Runtime.InteropServices;
using AOT;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR
{
	/// <summary>
	/// On a phone, draws the camera feed in place of ARCameraBackground: after the opaques, under them by the
	/// alpha they wrote, as a headset's passthrough compositor does, styled by PassthroughStylingFeature.
	/// Headsets keep ARCameraBackground.
	/// </summary>
	[RequireComponent(typeof(Camera), typeof(ARCameraManager))]
	public class HandheldCameraBackground : MonoBehaviour
	{
		private static readonly int DisplayTransformId = Shader.PropertyToID("_UnityDisplayTransform");

		[SerializeField, FormerlySerializedAs("arKitStyledShader")]
		private Shader backgroundShader;

		/// <summary>The feed's material on a phone, otherwise null. Its display transform is set from each camera frame.</summary>
		public Material Material { get; private set; }

		private Camera gameCamera;
		private ARCameraManager cameraManager;
		private DrawFeedPass drawFeedPass;
		private bool hasFeed;

		private void Awake()
		{
			if (!HandheldAR.IsActive || backgroundShader == null) return;

			gameCamera = GetComponent<Camera>();
			cameraManager = GetComponent<ARCameraManager>();
			if (TryGetComponent(out ARCameraBackground arCameraBackground)) arCameraBackground.enabled = false;

			Material = new Material(backgroundShader);
			if (HandheldAR.Runtime == HandheldAR.Provider.ARKit) Material.EnableKeyword("HANDHELD_ARKIT");
			else if (HandheldAR.Runtime == HandheldAR.Provider.ARCore) Material.EnableKeyword("HANDHELD_ARCORE");
			drawFeedPass = new DrawFeedPass(Material);

			// the feed fills in wherever the frame is left transparent
			gameCamera.clearFlags = CameraClearFlags.SolidColor;
			gameCamera.backgroundColor = Color.clear;
		}

		private void OnEnable()
		{
			if (Material == null) return;
			cameraManager.frameReceived += OnCameraFrame;
			RenderPipelineManager.beginCameraRendering += EnqueueFeedForThisCamera;
		}

		private void OnDisable()
		{
			if (Material == null) return;
			cameraManager.frameReceived -= OnCameraFrame;
			RenderPipelineManager.beginCameraRendering -= EnqueueFeedForThisCamera;
			renderThreadCameraSubsystem = null;
		}

		private void OnDestroy()
		{
			if (Material != null) Destroy(Material);
		}

		private void OnCameraFrame(ARCameraFrameEventArgs frame)
		{
			for (int i = 0; i < frame.textures.Count; i++)
				Material.SetTexture(frame.propertyNameIds[i], frame.textures[i]);
			if (frame.displayMatrix.HasValue)
				Material.SetMatrix(DisplayTransformId, frame.displayMatrix.Value);
			if (frame.projectionMatrix.HasValue)
			{
				gameCamera.projectionMatrix = frame.projectionMatrix.Value;
				gameCamera.fieldOfView = Mathf.Atan(1 / gameCamera.projectionMatrix[1, 1]) * 2 * Mathf.Rad2Deg;
			}

			renderThreadCameraSubsystem = cameraManager.subsystem;
			hasFeed = frame.textures.Count > 0;
		}

		private void EnqueueFeedForThisCamera(ScriptableRenderContext context, Camera renderingCamera)
		{
			if (renderingCamera != gameCamera || !hasFeed) return;
			renderingCamera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(drawFeedPass);
		}

		// The provider's work right before its images are drawn, on the render thread: ARKit releases the
		// previous frame's textures, ARCore waits for the image's GL fence
		private delegate void RenderThreadEvent(int eventId);

		private static XRCameraSubsystem renderThreadCameraSubsystem;
		private static readonly RenderThreadEvent BeforeFeedDraw = OnBeforeFeedDraw;
		private static readonly IntPtr BeforeFeedDrawPointer = Marshal.GetFunctionPointerForDelegate(BeforeFeedDraw);

		[MonoPInvokeCallback(typeof(RenderThreadEvent))]
		private static void OnBeforeFeedDraw(int eventId) => renderThreadCameraSubsystem?.OnBeforeBackgroundRender(eventId);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ForgetCameraSubsystem() => renderThreadCameraSubsystem = null;

		private sealed class DrawFeedPass : ScriptableRenderPass
		{
			private readonly Material material;

			public DrawFeedPass(Material material)
			{
				this.material = material;
				renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
				profilingSampler = new ProfilingSampler("Handheld Camera Background");
			}

			private sealed class PassData
			{
				public Material material;
			}

			public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
			{
				UniversalResourceData resources = frameData.Get<UniversalResourceData>();
				using IRasterRenderGraphBuilder builder = graph.AddRasterRenderPass<PassData>(
					"Handheld Camera Background", out PassData data, profilingSampler);

				data.material = material;
				builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
				// matching the opaque pass's attachments keeps this merged into its native render pass
				builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
				{
					context.cmd.IssuePluginEvent(BeforeFeedDrawPointer, 0);
					context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
				});
			}
		}
	}
}
