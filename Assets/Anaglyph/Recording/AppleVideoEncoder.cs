#if UNITY_IOS
using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Anaglyph.Recording
{
	/// <summary>AVAssetWriter, through AnaglyphVideoEncoder.mm.</summary>
	internal sealed class AppleVideoEncoder : IVideoEncoder
	{
		[DllImport("__Internal")] private static extern bool AnaglyphVideoEncoderBegin(string path, int width, int height, int framesPerSecond, int sampleRate, int channels);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderAppendVideo(ref byte rgbaBottomRowFirst, double seconds);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderAppendAudio(ref float interleaved, int frameCount, double seconds);
		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderFinish();
		[DllImport("__Internal")] private static extern int AnaglyphVideoEncoderState();
		[DllImport("__Internal")] private static extern IntPtr AnaglyphVideoEncoderError();

		[DllImport("__Internal")] private static extern void AnaglyphVideoEncoderRequestSaveAccess();

		private int channels;

		public static void RequestSaveAccess() => AnaglyphVideoEncoderRequestSaveAccess();

		public VideoEncoderState State => (VideoEncoderState)AnaglyphVideoEncoderState();
		public string Error => Marshal.PtrToStringUTF8(AnaglyphVideoEncoderError());

		public bool Begin(string filePath, int width, int height, int framesPerSecond, int audioSampleRate, int audioChannels)
		{
			channels = audioChannels;
			return AnaglyphVideoEncoderBegin(filePath, width, height, framesPerSecond, audioSampleRate, audioChannels);
		}

		public void AppendVideo(NativeArray<byte> rgba, double seconds) =>
			AnaglyphVideoEncoderAppendVideo(ref MemoryMarshal.GetReference(rgba.AsReadOnlySpan()), seconds);

		public void AppendAudio(NativeArray<float> samples, int sampleCount, double seconds) =>
			AnaglyphVideoEncoderAppendAudio(ref MemoryMarshal.GetReference(samples.AsReadOnlySpan()), sampleCount / channels, seconds);

		public void Finish() => AnaglyphVideoEncoderFinish();
	}
}
#endif
