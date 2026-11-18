using Unity.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace Anaglyph.Recording
{
	/// <summary>Records the default microphone into a looping clip and hands over what is new since the last call.</summary>
	internal sealed class MicrophoneTap
	{
		private const int LoopSeconds = 4;
		private const int PreferredSampleRate = 48000;

		public static bool IsAllowed =>
#if UNITY_ANDROID && !UNITY_EDITOR
			Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
			Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif

		public static void RequestAccess()
		{
			if (IsAllowed) return;
#if UNITY_ANDROID && !UNITY_EDITOR
			Permission.RequestUserPermission(Permission.Microphone);
#else
			Application.RequestUserAuthorization(UserAuthorization.Microphone);
#endif
		}

		public int SampleRate => clip.frequency;
		public int Channels => clip.channels;
		/// <summary>Interleaved samples filled by <see cref="Take"/>.</summary>
		public NativeArray<float> Samples { get; }

		private readonly AudioClip clip;
		private int readFrame;

		private MicrophoneTap(AudioClip clip)
		{
			this.clip = clip;
			Samples = new NativeArray<float>(clip.samples * clip.channels, Allocator.Persistent);
		}

		/// <summary>Null when the microphone is not allowed, missing, or fails to start.</summary>
		public static MicrophoneTap TryStart()
		{
			if (!IsAllowed || Microphone.devices.Length == 0) return null;
			Microphone.GetDeviceCaps(null, out int minRate, out int maxRate);
			bool anyRate = minRate == 0 && maxRate == 0;
			AudioClip clip = Microphone.Start(null, true, LoopSeconds, anyRate ? PreferredSampleRate : Mathf.Clamp(PreferredSampleRate, minRate, maxRate));
			return clip != null ? new MicrophoneTap(clip) : null;
		}

		/// <summary>Moves what was heard since the last call into <see cref="Samples"/>; returns the sample count.</summary>
		public int Take()
		{
			int recordedFrame = Microphone.GetPosition(null);
			int frames = (recordedFrame - readFrame + clip.samples) % clip.samples;
			if (frames == 0) return 0;

			int framesBeforeWrap = Mathf.Min(frames, clip.samples - readFrame);
			clip.GetData(Samples.AsSpan()[..(framesBeforeWrap * Channels)], readFrame);
			if (frames > framesBeforeWrap)
				clip.GetData(Samples.AsSpan()[(framesBeforeWrap * Channels)..(frames * Channels)], 0);
			readFrame = recordedFrame;
			return frames * Channels;
		}

		public void Stop()
		{
			Microphone.End(null);
			Object.Destroy(clip);
			Samples.Dispose();
		}
	}
}
