using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.Rendering
{
	/// <summary>Submits indicators for one frame. Callers own their visibility and lifetime.</summary>
	public sealed class IndicatorRenderer
	{
		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
		private static readonly int LineWidthId = Shader.PropertyToID("_LineWidth");
		private static readonly int ViewportSizeId = Shader.PropertyToID("_ViewportSize");
		private readonly MaterialPropertyBlock properties = new();
		private Mesh quad;
		private Material overlayLineMaterial, depthLineMaterial;

		private Mesh Quad => quad ? quad : quad = Resources.Load<Mesh>("Indicators/Quad");

		public void DrawMesh(Camera camera, Mesh mesh, Material material, Matrix4x4 transform,
			Color color, int layer = 0, int submesh = 0)
		{
			if (!camera || !mesh || !material) return;
			properties.Clear();
			properties.SetColor(BaseColorId, color);
			Submit(camera, mesh, material, transform, layer, submesh);
		}

		public void DrawQuad(Camera camera, Material material, Matrix4x4 transform, Color color, int layer = 0) =>
			DrawMesh(camera, Quad, material, transform, color, layer);

		/// <summary>Draws a line with width in camera viewport pixels.</summary>
		public void DrawLine(Camera camera, Vector3 start, Vector3 end, Color color,
			float widthPixels = 2, bool depthTest = true, int layer = 0)
		{
			if (!camera || widthPixels <= 0 || camera.pixelWidth <= 0 || camera.pixelHeight <= 0) return;
			Vector3 cameraForward = camera.transform.forward;
			Vector3 cameraPosition = camera.transform.position;
			float startDepth = Vector3.Dot(start - cameraPosition, cameraForward);
			float endDepth = Vector3.Dot(end - cameraPosition, cameraForward);
			float near = camera.nearClipPlane;
			if (startDepth < near && endDepth < near) return;
			if (startDepth < near) start = Vector3.Lerp(start, end, (near - startDepth) / (endDepth - startDepth));
			else if (endDepth < near) end = Vector3.Lerp(end, start, (near - endDepth) / (startDepth - endDepth));
			Vector3 delta = end - start;
			float length = delta.magnitude;
			if (length < .00001f) return;

			if (!overlayLineMaterial) overlayLineMaterial = Resources.Load<Material>("Indicators/OverlayLine");
			if (!depthLineMaterial) depthLineMaterial = Resources.Load<Material>("Indicators/DepthLine");
			Material material = depthTest ? depthLineMaterial : overlayLineMaterial;
			if (!Quad || !material) return;
			properties.Clear();
			properties.SetColor(BaseColorId, color);
			properties.SetFloat(LineWidthId, widthPixels);
			properties.SetVector(ViewportSizeId, new Vector4(camera.pixelWidth, camera.pixelHeight, 0, 0));
			Matrix4x4 transform = Matrix4x4.TRS((start + end) * .5f,
				Quaternion.FromToRotation(Vector3.right, delta / length), new Vector3(length, 1, 1));
			float worldWidth = (widthPixels + 1) * 2 / (Mathf.Abs(camera.projectionMatrix.m11) * camera.pixelHeight);
			if (!camera.orthographic) worldWidth *= Mathf.Max(near, Mathf.Max(startDepth, endDepth));
			Bounds bounds = new(start, Vector3.zero);
			bounds.Encapsulate(end);
			bounds.Expand(worldWidth);
			Submit(camera, Quad, material, transform, layer, 0, bounds);
		}

		private void Submit(Camera camera, Mesh mesh, Material material, Matrix4x4 transform,
			int layer, int submesh, Bounds? bounds = null)
		{
			RenderParams parameters = new(material)
			{
				camera = camera,
				layer = layer,
				matProps = properties,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				lightProbeUsage = LightProbeUsage.Off
			};
			if (bounds.HasValue) parameters.worldBounds = bounds.Value;
			Graphics.RenderMesh(in parameters, mesh, submesh, transform);
		}
	}
}
