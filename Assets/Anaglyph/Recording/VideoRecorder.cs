using System;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.Recording
{
	/// <summary>
	/// Records a render texture, what an AudioListener hears and, if allowed, the microphone into a
	/// video file in the device's photo library. The game and the microphone are separate audio
	/// tracks. The file has the texture's size; frames are taken at a steady rate however fast the
	/// game draws.
	/// </summary>
	public sealed class VideoRecorder : MonoBehaviour
	{
		private const int MaxFramesBeingRead = 3;
		private const int AudioScratchSeconds = 2;

		public static bool IsSupported =>
			Application.platform is RuntimePlatform.IPhonePlayer or RuntimePlatform.Android;

		/// <summary>
		/// Asks, the first time, for leave to add videos to the photo library (never to read it), so the
		/// question does not wait for the end of the first recording. Android needs no leave.
		/// </summary>
		public static void RequestSaveAccess()
		{
#if UNITY_IOS && !UNITY_EDITOR
			AppleVideoEncoder.RequestSaveAccess();
#endif
		}

		/// <summary>Asks, the first time, for leave to record the microphone into videos.</summary>
		public static void RequestMicrophoneAccess() => MicrophoneTap.RequestAccess();
		public static bool MicrophoneAllowed => MicrophoneTap.IsAllowed;

		public bool IsRecording { get; private set; }
		public bool IsRecordingMicrophone => IsRecording && microphone != null;
		public bool IsSaving { get; private set; }
		public double RecordedSeconds => IsRecording ? clock.Elapsed.TotalSeconds : 0;

		/// <summary>The file reached the photo library (true), or why it did not.</summary>
		public event Action<bool, string> Finished = delegate { };

		private readonly Stopwatch clock = new();
		private IVideoEncoder encoder;
		private RenderTexture source;
		private AudioListenerTap tap;
		private MicrophoneTap microphone;
		private double microphoneStartSeconds;
		private long microphoneFramesWritten;
		private NativeArray<float> audioScratch;
		private double frameInterval, nextFrameAt;
		private long audioSamplesWritten;
		private int audioSampleRate, audioChannels;
		private int framesBeingRead;
		private int session;

		public bool Begin(RenderTexture texture, int framesPerSecond, AudioListener listener, bool withMicrophone)
		{
			if (IsRecording || IsSaving || texture == null) return false;

			encoder ??= CreateEncoder();
			if (encoder == null)
			{
				Finished.Invoke(false, "Video recording is not available on this device");
				return false;
			}

			// first, as starting the microphone can change how the device plays sound
			microphone = withMicrophone ? MicrophoneTap.TryStart() : null;
			audioSampleRate = AudioSettings.outputSampleRate;
			audioChannels = ChannelCount(AudioSettings.speakerMode);
			string path = Path.Combine(Application.temporaryCachePath, $"lasertag-{DateTime.Now:yyyyMMdd-HHmmss}.mp4");
			if (!encoder.Begin(path, texture.width, texture.height, framesPerSecond, audioSampleRate, audioChannels,
				    microphone?.SampleRate ?? 0, microphone?.Channels ?? 0))
			{
				StopMicrophone();
				Finished.Invoke(false, encoder.Error);
				return false;
			}

			source = texture;
			frameInterval = 1.0 / framesPerSecond;
			nextFrameAt = 0;
			audioSamplesWritten = 0;
			microphoneFramesWritten = 0;
			framesBeingRead = 0;
			session++;

			if (listener != null)
			{
				if (!listener.TryGetComponent(out tap)) tap = listener.gameObject.AddComponent<AudioListenerTap>();
				if (!audioScratch.IsCreated)
					audioScratch = new NativeArray<float>(audioSampleRate * audioChannels * AudioScratchSeconds, Allocator.Persistent);
				tap.Prepare(audioSampleRate, audioChannels);
			}

			clock.Restart();
			IsRecording = true;
			CaptureFrames().RunUntilCancelled();
			return true;
		}

		public void End()
		{
			if (!IsRecording) return;
			IsRecording = false;
			clock.Stop();

			AsyncGPUReadback.WaitAllRequests();
			WriteHeardAudio();
			WriteMicrophone();
			StopMicrophone();
			if (tap != null) Destroy(tap);
			tap = null;
			source = null;

			encoder.Finish();
			IsSaving = true;
		}

		private async Awaitable CaptureFrames()
		{
			int capturedSession = session;
			while (true)
			{
				await Awaitable.EndOfFrameAsync(destroyCancellationToken);
				if (!IsRecording || capturedSession != session) return;

				double now = clock.Elapsed.TotalSeconds;
				if (now < nextFrameAt || framesBeingRead >= MaxFramesBeingRead) continue;
				// a late frame doesn't pull the ones after it early
				nextFrameAt = Math.Max(nextFrameAt + frameInterval, now - frameInterval / 2);

				int startedIn = session;
				framesBeingRead++;
				AsyncGPUReadback.Request(source, 0, TextureFormat.RGBA32, request =>
				{
					if (startedIn != session) return;
					framesBeingRead--;
					if (request.hasError || encoder.State != VideoEncoderState.Recording) return;
					encoder.AppendVideo(request.GetData<byte>(), now);
				});
			}
		}

		private void Update()
		{
			if (IsRecording)
			{
				WriteHeardAudio();
				WriteMicrophone();
				if (encoder.State == VideoEncoderState.Failed) End();
			}

			if (!IsSaving || encoder.State is VideoEncoderState.Recording or VideoEncoderState.Finishing) return;
			IsSaving = false;
			Finished.Invoke(encoder.State == VideoEncoderState.Saved, encoder.Error);
		}

		private void WriteHeardAudio()
		{
			if (tap == null) return;
			int count = tap.Take(audioScratch);
			if (count == 0) return;
			encoder.AppendAudio(AudioTrack.Game, audioScratch, count, (double)audioSamplesWritten / audioSampleRate);
			audioSamplesWritten += count / audioChannels;
		}

		private void WriteMicrophone()
		{
			if (microphone == null) return;
			int count = microphone.Take();
			if (count == 0) return;

			int frames = count / microphone.Channels;
			// the microphone's own start lags the clock's; its first samples were heard just now
			if (microphoneFramesWritten == 0)
				microphoneStartSeconds = Math.Max(0, clock.Elapsed.TotalSeconds - (double)frames / microphone.SampleRate);
			encoder.AppendAudio(AudioTrack.Microphone, microphone.Samples, count,
				microphoneStartSeconds + (double)microphoneFramesWritten / microphone.SampleRate);
			microphoneFramesWritten += frames;
		}

		private void StopMicrophone()
		{
			microphone?.Stop();
			microphone = null;
		}

		// a recording left open when the app is put away would never be closed into a playable file
		private void OnApplicationPause(bool paused)
		{
			if (paused) End();
		}

		private void OnDisable() => End();

		private void OnDestroy()
		{
			if (audioScratch.IsCreated) audioScratch.Dispose();
			StopMicrophone();
		}

		private static int ChannelCount(AudioSpeakerMode mode) => mode switch
		{
			AudioSpeakerMode.Mono => 1,
			AudioSpeakerMode.Quad => 4,
			AudioSpeakerMode.Surround => 5,
			AudioSpeakerMode.Mode5point1 => 6,
			AudioSpeakerMode.Mode7point1 => 8,
			_ => 2,
		};

		private static IVideoEncoder CreateEncoder()
		{
#if UNITY_IOS && !UNITY_EDITOR
			return new AppleVideoEncoder();
#elif UNITY_ANDROID && !UNITY_EDITOR
			return new AndroidVideoEncoder();
#else
			return null;
#endif
		}
	}
}
