using Anaglyph.Recording;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Camera mode on a phone's screen, laid out as a camera app: the view being filmed fitted to the
	/// screen, a record button on the phone's physical bottom edge with Done beside it, and across
	/// the top the running time, the video's size and frame rate, which a tap switches between
	/// 1080p at 60 and 4K at 30 where the phone offers both, and whether the microphone is recorded.
	/// </summary>
	public sealed class CameraModeScreen
	{
		private const string Table = "HUDMenu";
		private const float BarThickness = 96, EdgeMargin = 12;
		private const float MessageSeconds = 5;
		private const int UltraHighDefinitionLongSide = 3840;

		private readonly CameraMode mode;
		private readonly VisualElement root, view, bar, status, record;
		private readonly Button done;
		private readonly Label time, size, microphone, message;
		private float messageUntil;

		public CameraModeScreen(CameraMode mode, VisualElement parent)
		{
			this.mode = mode;

			root = new VisualElement { name = "camera-mode" };
			root.AddToClassList("camera-mode");
			parent.Add(root);

			view = new VisualElement { name = "camera-mode-view", pickingMode = PickingMode.Ignore };
			view.AddToClassList("camera-mode-view");
			root.Add(view);

			message = new Label { name = "camera-mode-message", pickingMode = PickingMode.Ignore };
			message.AddToClassList("camera-mode-message");
			root.Add(message);

			status = new VisualElement { name = "camera-mode-status", pickingMode = PickingMode.Ignore };
			status.AddToClassList("camera-mode-status");
			root.Add(status);
			time = Chip("camera-mode-time");
			size = Chip("camera-mode-size");
			size.RegisterCallback<ClickEvent>(_ => mode.SetQuality(
				mode.Quality == CameraMode.VideoQuality.UltraHD30 ? CameraMode.VideoQuality.FullHD60 : CameraMode.VideoQuality.UltraHD30));
			microphone = Chip("camera-mode-microphone");
			microphone.RegisterCallback<ClickEvent>(_ => mode.SetRecordMicrophone(!mode.RecordMicrophone));

			bar = new VisualElement { name = "camera-mode-bar" };
			bar.AddToClassList("camera-mode-bar");
			root.Add(bar);

			done = new Button(() => mode.SetActive(false)) { name = "camera-mode-done" };
			done.AddToClassList("camera-mode-done");
			Slot().Add(done);

			record = new VisualElement { name = "camera-mode-record" };
			record.AddToClassList("camera-mode-record");
			VisualElement core = new() { pickingMode = PickingMode.Ignore };
			core.AddToClassList("camera-mode-record-core");
			record.Add(core);
			record.RegisterCallback<ClickEvent>(_ => OnRecordPressed());
			Slot().Add(record);

			// keeps the record button in the middle
			Slot();

			mode.Recorder.Finished += OnRecordingFinished;
			Anaglyph.MenuCopy.Changed += Label;
			Label();
			root.style.display = DisplayStyle.None;
		}

		public void Dispose()
		{
			mode.Recorder.Finished -= OnRecordingFinished;
			Anaglyph.MenuCopy.Changed -= Label;
		}

		/// <summary>Leaves camera mode, as Done does; nothing while a video is being recorded.</summary>
		public void Back()
		{
			if (!mode.Recorder.IsRecording) mode.SetActive(false);
		}

		private Label Chip(string name)
		{
			Label chip = new() { name = name };
			chip.AddToClassList("chip");
			status.Add(chip);
			return chip;
		}

		private VisualElement Slot()
		{
			VisualElement slot = new();
			slot.AddToClassList("camera-mode-bar-slot");
			bar.Add(slot);
			return slot;
		}

		private void Label() => done.text = Anaglyph.MenuCopy.Get(Table, "camera.done");

		private void OnRecordPressed()
		{
			if (!VideoRecorder.IsSupported)
			{
				Show("camera.unavailable");
				return;
			}

			mode.ToggleRecording();
		}

		private void OnRecordingFinished(bool saved, string error)
		{
			if (!saved) Debug.LogWarning($"[{nameof(CameraModeScreen)}] video not saved: {error}");
			Show(saved ? "camera.saved" : "camera.failed", saved ? null : error);
		}

		// the device's own reason goes under ours, in the device's words
		private void Show(string key, string reason = null)
		{
			message.text = string.IsNullOrEmpty(reason) ? Anaglyph.MenuCopy.Get(Table, key) : $"{Anaglyph.MenuCopy.Get(Table, key)}\n{reason}";
			messageUntil = Time.unscaledTime + MessageSeconds;
		}

		/// <summary>The safe area's insets in panel units, and which way the phone is turned.</summary>
		public void Layout(float left, float top, float right, float bottom, ScreenOrientation orientation)
		{
			// the bar keeps to the phone's physical bottom edge, however the screen is turned
			bool alongSide = orientation is ScreenOrientation.LandscapeLeft or ScreenOrientation.LandscapeRight;
			bar.style.flexDirection = orientation switch
			{
				ScreenOrientation.LandscapeLeft => FlexDirection.ColumnReverse,
				ScreenOrientation.LandscapeRight => FlexDirection.Column,
				ScreenOrientation.PortraitUpsideDown => FlexDirection.RowReverse,
				_ => FlexDirection.Row,
			};
			bar.style.width = alongSide ? BarThickness : StyleKeyword.Auto;
			bar.style.height = alongSide ? StyleKeyword.Auto : BarThickness;
			bar.style.left = orientation == ScreenOrientation.LandscapeLeft ? StyleKeyword.Auto : left + EdgeMargin;
			bar.style.right = orientation == ScreenOrientation.LandscapeRight ? StyleKeyword.Auto : right + EdgeMargin;
			bar.style.top = orientation == ScreenOrientation.Portrait ? StyleKeyword.Auto : top + EdgeMargin;
			bar.style.bottom = orientation == ScreenOrientation.PortraitUpsideDown ? StyleKeyword.Auto : bottom + EdgeMargin;

			// the status reads across the top of the screen, or the bottom when the bar has the top
			bool barOnTop = orientation == ScreenOrientation.PortraitUpsideDown;
			status.style.left = left + EdgeMargin;
			status.style.right = right + EdgeMargin;
			status.style.top = barOnTop ? StyleKeyword.Auto : top + EdgeMargin;
			status.style.bottom = barOnTop ? bottom + EdgeMargin : StyleKeyword.Auto;
		}

		public void Refresh()
		{
			root.style.display = mode.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
			if (!mode.IsActive) return;

			VideoRecorder recorder = mode.Recorder;
			record.EnableInClassList("camera-mode-record--recording", recorder.IsRecording);
			record.EnableInClassList("camera-mode-record--busy", recorder.IsSaving);
			done.style.visibility = recorder.IsRecording ? Visibility.Hidden : Visibility.Visible;

			int seconds = (int)recorder.RecordedSeconds;
			time.text = $"{seconds / 60:00}:{seconds % 60:00}";
			time.EnableInClassList("chip--critical", recorder.IsRecording);
			time.EnableInClassList("chip--unknown", !recorder.IsRecording);

			Vector2Int video = mode.VideoSize;
			int longSide = Mathf.Max(video.x, video.y), shortSide = Mathf.Min(video.x, video.y);
			size.text = $"{(longSide >= UltraHighDefinitionLongSide ? "4K" : shortSide + "p")} · {mode.FramesPerSecond}";
			bool canSwitch = mode.UltraHDAvailable && !recorder.IsRecording && !recorder.IsSaving;
			size.EnableInClassList("camera-mode-chip--switchable", canSwitch);
			size.EnableInClassList("chip--unknown", !canSwitch);

			bool microphoneBlocked = mode.RecordMicrophone && !VideoRecorder.MicrophoneAllowed;
			microphone.text = Anaglyph.MenuCopy.Get(Table, !mode.RecordMicrophone ? "camera.mic.off" : microphoneBlocked ? "camera.mic.denied" : "camera.mic.on");
			bool canSwitchMicrophone = !recorder.IsRecording && !recorder.IsSaving;
			microphone.EnableInClassList("camera-mode-chip--switchable", canSwitchMicrophone && !microphoneBlocked);
			microphone.EnableInClassList("chip--low", microphoneBlocked);
			microphone.EnableInClassList("chip--unknown", !canSwitchMicrophone && !microphoneBlocked);

			message.style.display = Time.unscaledTime < messageUntil ? DisplayStyle.Flex : DisplayStyle.None;

			FitView();
		}

		// the whole view, as large as the screen allows
		private void FitView()
		{
			RenderTexture filmed = mode.View;
			Vector2 screen = root.layout.size;
			if (filmed == null || screen.x <= 0 || screen.y <= 0) return;

			if (view.style.backgroundImage.value.renderTexture != filmed)
				view.style.backgroundImage = Background.FromRenderTexture(filmed);

			float scale = Mathf.Min(screen.x / filmed.width, screen.y / filmed.height);
			Vector2 fitted = new Vector2(filmed.width, filmed.height) * scale;
			view.style.width = fitted.x;
			view.style.height = fitted.y;
			view.style.left = (screen.x - fitted.x) / 2;
			view.style.top = (screen.y - fitted.y) / 2;
		}
	}
}
