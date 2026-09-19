using Anaglyph.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Anaglyph.LaserTag.Tests
{
	public class IndicatorRendererTests
	{
		private const int Size = 256, Layer = 30;
		private static int nextTestLocation;
		private GameObject owner;
		private Camera camera;
		private RenderTexture target;
		private Texture2D pixels;
		private IndicatorRenderer indicators;
		private Vector3 Origin => camera.transform.position;

		[SetUp]
		public void SetUp()
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Requires a graphics device.");
			owner = new GameObject("Indicator rendering test") { hideFlags = HideFlags.HideAndDontSave };
			camera = owner.AddComponent<Camera>();
			camera.enabled = false;
			camera.transform.position = new Vector3(10000 + 100 * nextTestLocation++, 10000, 10000);
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.black;
			camera.cullingMask = 1 << Layer;
			camera.nearClipPlane = .1f;
			target = new RenderTexture(Size, Size, 24);
			camera.targetTexture = target;
			pixels = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
			indicators = new IndicatorRenderer();
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(owner);
			Object.DestroyImmediate(target);
			Object.DestroyImmediate(pixels);
		}

		private void Render(Camera source = null)
		{
			(source ? source : camera).Render();
			RenderTexture previous = RenderTexture.active;
			try
			{
				RenderTexture.active = target;
				pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
				pixels.Apply();
			}
			finally { RenderTexture.active = previous; }
		}

		[TestCase(false, 3f, 1f)]
		[TestCase(false, 6f, 1f)]
		[TestCase(true, 3f, 1f)]
		[TestCase(false, 3f, .5f)]
		public void LineWidthIsStableAcrossDepthProjectionAndViewport(bool orthographic, float depth, float viewportScale)
		{
			camera.orthographic = orthographic;
			camera.orthographicSize = 2;
			camera.rect = new Rect((1 - viewportScale) * .5f, (1 - viewportScale) * .5f, viewportScale, viewportScale);
			indicators.DrawLine(camera, Origin + new Vector3(-1, 0, depth), Origin + new Vector3(1, 0, depth),
				Color.cyan, 6, false, Layer);
			Render();
			int thickness = 0;
			for (int y = 0; y < Size; y++) if (pixels.GetPixel(Size / 2, y).g > .1f) thickness++;
			Assert.That(thickness, Is.InRange(5, 8));
		}

		[Test]
		public void LinesBehindCameraAndZeroLengthLinesAreInvisible()
		{
			indicators.DrawLine(camera, Origin + new Vector3(-1, 0, -3), Origin + new Vector3(1, 0, -3), Color.white, 6, false, Layer);
			indicators.DrawLine(camera, Origin + Vector3.forward, Origin + Vector3.forward, Color.white, 6, false, Layer);
			Render();
			foreach (var pixel in pixels.GetPixels32()) Assert.That(pixel.r, Is.Zero);
		}

		[Test]
		public void IndicatorsOnlyRenderThroughTheRequestedCamera()
		{
			indicators.DrawLine(camera, Origin + new Vector3(-1, 0, 3), Origin + new Vector3(1, 0, 3), Color.white, 6, false, Layer);
			var other = new GameObject("Other indicator test camera") { hideFlags = HideFlags.HideAndDontSave };
			try
			{
				var otherCamera = other.AddComponent<Camera>();
				otherCamera.CopyFrom(camera);
				otherCamera.transform.position = Origin;
				Render(otherCamera);
				foreach (var pixel in pixels.GetPixels32()) Assert.That(pixel.r, Is.Zero);
			}
			finally { Object.DestroyImmediate(other); }
		}

		[Test]
		public void OverlayLinesRemainVisibleBehindOpaqueGeometry()
		{
			var opaque = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
			try
			{
				indicators.DrawQuad(camera, opaque, Matrix4x4.TRS(Origin + Vector3.forward * 2, Quaternion.identity, Vector3.one * 4), Color.black, Layer);
				indicators.DrawLine(camera, Origin + new Vector3(-1, -.3f, 3), Origin + new Vector3(1, -.3f, 3), Color.cyan, 6, true, Layer);
				indicators.DrawLine(camera, Origin + new Vector3(-1, .3f, 3), Origin + new Vector3(1, .3f, 3), Color.yellow, 6, false, Layer);
				Render();
				int yellow = 0, cyan = 0;
				foreach (var pixel in pixels.GetPixels32())
				{
					if (pixel.r > 20 && pixel.g > 20) yellow++;
					if (pixel.b > 20 && pixel.g > 20) cyan++;
				}
				Assert.That(yellow, Is.GreaterThan(100));
				Assert.That(cyan, Is.Zero);
			}
			finally { Object.DestroyImmediate(opaque); }
		}
	}
}
