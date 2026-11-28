using System;
using Anaglyph.LaserTag.Player;
using Anaglyph.Recording;
using Anaglyph.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// Turns a phone into a camera filming the game. While active the phone is not a player (no avatar,
	/// damage, team or part in starting a match), and the game is drawn into a texture the size of the
	/// video: 16:9 across the camera image, upright or sideways as the phone is held. Two qualities:
	/// 1080p at 60 frames a second on the camera image the session already runs, or, where the AR
	/// runtime offers a sharper image, that image (up to 4K) at 30.
	/// </summary>
	public sealed class CameraMode : MonoBehaviour
	{
		public enum VideoQuality { FullHD60, UltraHD30 }

		private const string QualityPref = "CameraMode.Quality";
		private const string MicrophonePref = "CameraMode.Microphone";
		private const int FullHDLongSide = 1920, UltraHDLongSide = 3840;
		private const float WideAspect = 16f / 9f;
		private const int EditorLongSide = 1920;
		// the renderer without features: all the screen camera does is clear
		private const int FeaturelessRendererIndex = 1;

		public static CameraMode Instance { get; private set; }

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics() => Instance = null;

		public bool IsActive { get; private set; }

		public VideoQuality Quality { get; private set; }
		/// <summary>Whether videos get a microphone track, when the phone allows it.</summary>
		public bool RecordMicrophone { get; private set; }
		/// <summary>Whether this phone's AR runtime offers a camera image sharper than 1080p.</summary>
		public bool UltraHDAvailable { get; private set; }
		public int FramesPerSecond => Quality == VideoQuality.UltraHD30 ? 30 : 60;
		private int MaxLongSide => Quality == VideoQuality.UltraHD30 ? UltraHDLongSide : FullHDLongSide;
		public event Action Changed = delegate { };

		/// <summary>What is being filmed, at the video's size. Null while inactive.</summary>
		public RenderTexture View { get; private set; }
		public VideoRecorder Recorder { get; private set; }

		private Camera gameCamera, screenClearCamera;
		private ARCameraManager cameraManager;
		private HandheldCameraBackground cameraBackground;
		private XRCameraConfiguration? configurationBefore;
		private float renderScaleBefore;
		private Vector2Int cameraImageSize;
		private bool orientationHeld;
		private static readonly int DisplayTransform = Shader.PropertyToID("_UnityDisplayTransform");

		private void Awake()
		{
			Instance = this;
			Recorder = gameObject.AddComponent<VideoRecorder>();
			Quality = (VideoQuality)PlayerPrefs.GetInt(QualityPref, (int)VideoQuality.FullHD60);
			RecordMicrophone = PlayerPrefs.GetInt(MicrophonePref, 1) == 1;
		}

		private void OnDestroy()
		{
			SetActive(false);
			if (Instance == this) Instance = null;
		}

		public void SetActive(bool active)
		{
			if (IsActive == active) return;
			gameCamera = MainXRRig.Camera;
			if (active && gameCamera == null) return;
			IsActive = active;

			if (active) Enter();
			else Exit();
			PlayerAvatarSpawner.Instance?.SetIsParticipating(!active);
			// a camera is on no team, and one that is not participating is given none
			PlayerAvatar local = PlayerAvatar.Local;
			if (active && local != null && local.Team != 0) local.TeamOwner.teamSync.Value = 0;
			Changed.Invoke();
		}

		/// <summary>Kept for the next time the app runs. Ignored while recording, and for a quality this phone lacks.</summary>
		public void SetQuality(VideoQuality quality)
		{
			if (Quality == quality || Recorder.IsRecording || Recorder.IsSaving) return;
			if (IsActive && quality == VideoQuality.UltraHD30 && !UltraHDAvailable) return;
			Quality = quality;
			PlayerPrefs.SetInt(QualityPref, (int)quality);
			if (IsActive) UseCameraImageForQuality();
			Changed.Invoke();
		}

		/// <summary>Kept for the next time the app runs. Ignored while recording.</summary>
		public void SetRecordMicrophone(bool record)
		{
			if (RecordMicrophone == record || Recorder.IsRecording || Recorder.IsSaving) return;
			RecordMicrophone = record;
			PlayerPrefs.SetInt(MicrophonePref, record ? 1 : 0);
			if (record) VideoRecorder.RequestMicrophoneAccess();
			Changed.Invoke();
		}

		public void ToggleRecording()
		{
			if (!IsActive || Recorder.IsSaving) return;
			if (Recorder.IsRecording)
			{
				Recorder.End();
				return;
			}

			if (View == null) return;
			if (!Recorder.Begin(View, FramesPerSecond, FindAnyObjectByType<AudioListener>(), RecordMicrophone)) return;
			// the video keeps the shape it started with
			Screen.orientation = Screen.orientation;
			orientationHeld = true;
		}

		private void Enter()
		{
			VideoRecorder.RequestSaveAccess();
			if (RecordMicrophone) VideoRecorder.RequestMicrophoneAccess();
			gameCamera.TryGetComponent(out cameraManager);
			gameCamera.TryGetComponent(out cameraBackground);
			cameraImageSize = Vector2Int.zero;

			if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
			{
				renderScaleBefore = urp.renderScale;
				urp.renderScale = 1;
			}

			if (cameraManager != null)
			{
				cameraManager.frameReceived += OnCameraFrame;
				UltraHDAvailable = SharpestCameraImage() is { width: > FullHDLongSide };
				if (!UltraHDAvailable) Quality = VideoQuality.FullHD60;
				UseCameraImageForQuality();
			}

			GameObject screenClear = new("Camera Mode Screen Clear");
			screenClear.transform.SetParent(transform, false);
			screenClearCamera = screenClear.AddComponent<Camera>();
			screenClearCamera.clearFlags = CameraClearFlags.SolidColor;
			screenClearCamera.backgroundColor = Color.black;
			screenClearCamera.cullingMask = 0;
			screenClearCamera.depth = gameCamera.depth + 1;
			screenClear.AddComponent<UniversalAdditionalCameraData>().SetRenderer(FeaturelessRendererIndex);

			FitViewToVideo();
		}

		private void Exit()
		{
			Recorder.End();
			ReleaseOrientation();

			if (cameraManager != null)
			{
				cameraManager.frameReceived -= OnCameraFrame;
				if (configurationBefore.HasValue) TrySetConfiguration(configurationBefore.Value);
				configurationBefore = null;
			}

			if (gameCamera != null)
			{
				gameCamera.targetTexture = null;
				gameCamera.ResetProjectionMatrix();
			}

			if (View != null) Destroy(View);
			View = null;
			if (screenClearCamera != null) Destroy(screenClearCamera.gameObject);

			if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
				urp.renderScale = renderScaleBefore;
		}

		private void Update()
		{
			if (!IsActive) return;

			if (!Recorder.IsRecording)
			{
				ReleaseOrientation();
				FitViewToVideo();
			}
		}

		private void ReleaseOrientation()
		{
			if (!orientationHeld) return;
			orientationHeld = false;
			Screen.orientation = ScreenOrientation.AutoRotation;
		}

		// ARKit offers its camera image in several sizes and shows the one chosen. ARCore's choices are
		// sizes of the image handed to the CPU; what it shows stays the same.
		private XRCameraConfiguration? SharpestCameraImage()
		{
			if (cameraManager == null || HandheldAR.Runtime != HandheldAR.Provider.ARKit) return null;

			using NativeArray<XRCameraConfiguration> configurations = cameraManager.GetConfigurations(Allocator.Temp);
			XRCameraConfiguration? best = null;
			foreach (XRCameraConfiguration candidate in configurations)
				if (candidate.width <= UltraHDLongSide && (!best.HasValue || IsSharper(candidate, best.Value)))
					best = candidate;
			return best;
		}

		// 4K films on the sharpest camera image; 1080p on the image the session ran before camera mode
		private void UseCameraImageForQuality()
		{
			if (cameraManager == null) return;

			if (Quality == VideoQuality.FullHD60)
			{
				if (configurationBefore.HasValue && TrySetConfiguration(configurationBefore.Value)) configurationBefore = null;
				return;
			}

			XRCameraConfiguration? best = SharpestCameraImage(), current = cameraManager.currentConfiguration;
			if (!best.HasValue || best.Equals(current)) return;
			if (TrySetConfiguration(best.Value)) configurationBefore ??= current;
		}

		private static bool IsSharper(XRCameraConfiguration a, XRCameraConfiguration b)
		{
			if (a.width != b.width) return a.width > b.width;
			float aOffWide = Mathf.Abs((float)a.width / a.height - WideAspect), bOffWide = Mathf.Abs((float)b.width / b.height - WideAspect);
			if (!Mathf.Approximately(aOffWide, bOffWide)) return aOffWide < bOffWide;
			return (a.framerate ?? 0) > (b.framerate ?? 0);
		}

		private bool TrySetConfiguration(XRCameraConfiguration configuration)
		{
			try
			{
				cameraManager.currentConfiguration = configuration;
				poseAtImageChange = gameCamera.transform.localPosition;
				framesSinceImageChange = 0;
				Debug.Log($"[{nameof(CameraMode)}] camera image {configuration.width}×{configuration.height} at {configuration.framerate} fps");
				return true;
			}
			catch (Exception exception)
			{
				Debug.LogWarning($"[{nameof(CameraMode)}] camera image stays as it is: {exception.Message}");
				return false;
			}
		}

		/// <summary>The video's size in pixels: 16:9 across the camera image, turned as the phone is held.</summary>
		public Vector2Int VideoSize
		{
			get
			{
				Vector2Int image = cameraImageSize.x > 0 ? cameraImageSize : new Vector2Int(EditorLongSide, Mathf.RoundToInt(EditorLongSide / WideAspect));
				int longSide = Even(Mathf.Min(image.x, MaxLongSide));
				int shortSide = Even(Mathf.Min(image.y, Mathf.RoundToInt(longSide / WideAspect)));
				return Screen.width >= Screen.height ? new Vector2Int(longSide, shortSide) : new Vector2Int(shortSide, longSide);
			}
		}

		private static int Even(int pixels) => pixels - pixels % 2;

		private void FitViewToVideo()
		{
			Vector2Int size = VideoSize;
			if (View != null && View.width == size.x && View.height == size.y) return;

			if (View != null)
			{
				gameCamera.targetTexture = null;
				Destroy(View);
			}

			View = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32) { name = "Camera Mode View" };
			gameCamera.targetTexture = View;
			Changed.Invoke();
		}

		// The AR runtime fits the camera image and the game's projection to the screen. The view has
		// another shape, so both are refitted: the view shows `shown` times as much of the scene along
		// each axis as the screen would.
		private void OnCameraFrame(ARCameraFrameEventArgs frame)
		{
			if (frame.textures != null && frame.textures.Count > 0 && frame.textures[0] != null)
			{
				Texture image = frame.textures[0];
				cameraImageSize = new Vector2Int(Mathf.Max(image.width, image.height), Mathf.Min(image.width, image.height));
			}

			ReportTrackingAcrossImageChange();
			if (View == null || cameraImageSize.x == 0) return;

			float imageAspect = (float)cameraImageSize.x / cameraImageSize.y;
			if (Screen.width < Screen.height) imageAspect = 1 / imageAspect;
			Vector2 onScreen = ShownFraction((float)Screen.width / Screen.height, imageAspect);
			Vector2 inView = ShownFraction((float)View.width / View.height, imageAspect);
			Vector2 shown = inView / onScreen;

			if (frame.projectionMatrix.HasValue)
			{
				Matrix4x4 projection = frame.projectionMatrix.Value;
				projection[0, 0] /= shown.x;
				projection[0, 2] /= shown.x;
				projection[1, 1] /= shown.y;
				projection[1, 2] /= shown.y;
				gameCamera.projectionMatrix = projection;
				gameCamera.fieldOfView = Mathf.Atan(1 / projection[1, 1]) * 2 * Mathf.Rad2Deg;
			}

			Material backgroundMaterial = cameraBackground != null ? cameraBackground.Material : null;
			if (frame.displayMatrix.HasValue && backgroundMaterial != null)
			{
				// the background shaders read image coordinates as (u, v, 1) × matrix
				Matrix4x4 display = frame.displayMatrix.Value;
				for (int column = 0; column < 4; column++)
				{
					float u = display[0, column], v = display[1, column];
					display[0, column] = u * shown.x;
					display[1, column] = v * shown.y;
					display[2, column] += u * (1 - shown.x) / 2 + v * (1 - shown.y) / 2;
				}
				backgroundMaterial.SetMatrix(DisplayTransform, display);
			}
		}

		// A jump here means the AR runtime restarted its tracking for the new camera image: whatever is
		// scanned after it lands away from what was scanned before, until alignment pulls it back.
		private const int FramesToWatchAfterImageChange = 90;
		private const float TrackingJumpMeters = 0.1f;
		private Vector3 poseAtImageChange;
		private int framesSinceImageChange = FramesToWatchAfterImageChange;
		private float largestMoveSinceImageChange;

		private void ReportTrackingAcrossImageChange()
		{
			if (framesSinceImageChange >= FramesToWatchAfterImageChange) return;
			if (framesSinceImageChange == 0) largestMoveSinceImageChange = 0;
			largestMoveSinceImageChange = Mathf.Max(largestMoveSinceImageChange, Vector3.Distance(gameCamera.transform.localPosition, poseAtImageChange));
			if (++framesSinceImageChange < FramesToWatchAfterImageChange) return;

			string verdict = largestMoveSinceImageChange > TrackingJumpMeters ? "tracking may have restarted" : "tracking carried on";
			string message = $"[{nameof(CameraMode)}] {verdict}: the camera moved up to {largestMoveSinceImageChange:F2} m in the {FramesToWatchAfterImageChange} frames after its image changed; " +
			                 $"image {cameraImageSize.x}×{cameraImageSize.y}, screen {Screen.width}×{Screen.height}, video {View?.width}×{View?.height}, session {ARSession.state}";
			if (largestMoveSinceImageChange > TrackingJumpMeters) Debug.LogWarning(message);
			else Debug.Log(message);
		}

		// how much of the camera image, across and up, a viewport of this shape shows when the image fills it
		private static Vector2 ShownFraction(float viewportAspect, float imageAspect) =>
			viewportAspect >= imageAspect
				? new Vector2(1, imageAspect / viewportAspect)
				: new Vector2(viewportAspect / imageAspect, 1);
	}
}
