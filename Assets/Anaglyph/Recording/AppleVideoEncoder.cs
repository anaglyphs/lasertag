#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Anaglyph.Recording
{
	/// <summary>AVAssetWriter, through AnaglyphVideoEncoder.mm.</summary>
	internal sealed class AppleVideoEncoder : IVideoEncoder
	{
		[DllImport("__Internal")] private static extern bool AnaglyphVideoEncoderBegin(string path, int width, int height, int framesPerSecond, int sampleRate, int channels, int microphoneSampleRate, int microphoneChannels);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderAppendVideo(ref byte rgbaBottomRowFirst, double seconds);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderAppendAudio(int track, ref float interleaved, int frameCount, double seconds);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderFinish();
		[DllImport("__Internal")] private static extern int AnaglyphVideoEncoderState();
		[DllImport("__Internal")] private static extern IntPtr AnaglyphVideoEncoderError();

		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderRequestSaveAccess();

		private readonly int[] channelsByTrack = new int[3];

		public static void RequestSaveAccess() => AnaglyphVideoEncoderRequestSaveAccess();

		public VideoEncoderState State => (VideoEncoderState)AnaglyphVideoEncoderState();
		public string Error => Marshal.PtrToStringUTF8(AnaglyphVideoEncoderError());

		public bool Begin(string filePath, int width, int height, int framesPerSecond, int audioSampleRate, int audioChannels,
			int microphoneSampleRate, int microphoneChannels)
		{
			channelsByTrack[(int)AudioTrack.Game] = audioChannels;
			channelsByTrack[(int)AudioTrack.Microphone] = microphoneChannels;
			return AnaglyphVideoEncoderBegin(filePath, width, height, framesPerSecond, audioSampleRate, audioChannels, microphoneSampleRate, microphoneChannels);
		}

		public void AppendVideo(NativeArray<byte> rgba, double seconds) =>
			AnaglyphVideoEncoderAppendVideo(ref MemoryMarshal.GetReference(rgba.AsReadOnlySpan()), seconds);

		public void AppendAudio(AudioTrack track, NativeArray<float> samples, int sampleCount, double seconds) =>
			AnaglyphVideoEncoderAppendAudio((int)track, ref MemoryMarshal.GetReference(samples.AsReadOnlySpan()), sampleCount / channelsByTrack[(int)track], seconds);

		public void Finish() => AnaglyphVideoEncoderFinish();
	}
}
#endif
