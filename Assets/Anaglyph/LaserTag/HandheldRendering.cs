using Anaglyph.XR;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// A phone's frame budget, set once it starts as a phone. The render pipeline asset is shared with
	/// Quest, so it is only touched in memory, on a real phone, never saved.
	/// </summary>
	public static class HandheldRendering
	{
		// a phone's camera image is at most this tall on its long side (ARKit 1920×1440): rendering the
		// world sharper than the feed behind it costs fill rate for nothing
		private const float MaxRenderedLongSide = 1920;
		// with vSync off, mobile players otherwise hold 30
		private const int TargetFrameRate = 60;

		public static void Apply()
		{
			Application.targetFrameRate = TargetFrameRate;
			Screen.sleepTimeout = SleepTimeout.NeverSleep;

			// either way up, either way round
			Screen.autorotateToPortrait = Screen.autorotateToPortraitUpsideDown = true;
			Screen.autorotateToLandscapeLeft = Screen.autorotateToLandscapeRight = true;
			Screen.orientation = ScreenOrientation.AutoRotation;

			if (HandheldAR.Runtime == HandheldAR.Provider.None) return;
			if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
			{
				float longSide = Mathf.Max(Screen.width, Screen.height);
				urp.renderScale = Mathf.Clamp(MaxRenderedLongSide / longSide, 0.5f, 1);
				Debug.Log($"[{nameof(HandheldRendering)}] {TargetFrameRate} fps, render scale {urp.renderScale:F2} of {Screen.width}×{Screen.height}");
			}
		}
	}
}
