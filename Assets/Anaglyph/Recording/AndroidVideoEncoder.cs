#if UNITY_ANDROID
using System;
using Unity.Collections;
using UnityEngine;

namespace Anaglyph.Recording
{
	/// <summary>MediaCodec and MediaMuxer, through VideoEncoder.java.</summary>
	internal sealed class AndroidVideoEncoder : IVideoEncoder
	{
		private const short MaxSample = short.MaxValue;

		private readonly AndroidJavaObject encoder;
		private readonly IntPtr appendVideo, appendAudio;
		private readonly jvalue[] videoArguments = new jvalue[2], audioArguments = new jvalue[4];
		private NativeArray<short> samples16;

		public AndroidVideoEncoder()
		{
			using AndroidJavaClass player = new("com.unity3d.player.UnityPlayer");
			using AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity");
			encoder = new AndroidJavaObject("com.anaglyph.recording.VideoEncoder", activity);
			appendVideo = AndroidJNIHelper.GetMethodID(encoder.GetRawClass(), "appendVideo", "(Ljava/nio/ByteBuffer;J)V");
			appendAudio = AndroidJNIHelper.GetMethodID(encoder.GetRawClass(), "appendAudio", "(ILjava/nio/ByteBuffer;IJ)V");
		}

		public VideoEncoderState State => (VideoEncoderState)encoder.Call<int>("getState");
		public string Error => encoder.Call<string>("getError");

		public bool Begin(string filePath, int width, int height, int framesPerSecond, int audioSampleRate, int audioChannels,
			int microphoneSampleRate, int microphoneChannels) =>
			encoder.Call<bool>("begin", filePath, width, height, framesPerSecond, audioSampleRate, audioChannels, microphoneSampleRate, microphoneChannels);

		public void AppendVideo(NativeArray<byte> rgba, double seconds)
		{
			IntPtr buffer = AndroidJNI.NewDirectByteBuffer(rgba);
			videoArguments[0].l = buffer;
			videoArguments[1].j = Micros(seconds);
			AndroidJNI.CallVoidMethod(encoder.GetRawObject(), appendVideo, videoArguments);
			AndroidJNI.DeleteLocalRef(buffer);
		}

		public void AppendAudio(AudioTrack track, NativeArray<float> samples, int sampleCount, double seconds)
		{
			if (samples16.Length < sampleCount)
			{
				if (samples16.IsCreated) samples16.Dispose();
				samples16 = new NativeArray<short>(samples.Length, Allocator.Persistent);
			}

			for (int i = 0; i < sampleCount; i++)
				samples16[i] = (short)(Mathf.Clamp(samples[i], -1, 1) * MaxSample);

			IntPtr buffer = AndroidJNI.NewDirectByteBuffer(samples16.Reinterpret<byte>(sizeof(short)));
			audioArguments[0].i = (int)track;
			audioArguments[1].l = buffer;
			audioArguments[2].i = sampleCount * sizeof(short);
			audioArguments[3].j = Micros(seconds);
			AndroidJNI.CallVoidMethod(encoder.GetRawObject(), appendAudio, audioArguments);
			AndroidJNI.DeleteLocalRef(buffer);
		}

		public void Finish() => encoder.Call("finish");

		private static long Micros(double seconds) => (long)(seconds * 1e6);
	}
}
#endif
