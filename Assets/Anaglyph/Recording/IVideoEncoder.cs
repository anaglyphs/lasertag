using Unity.Collections;

namespace Anaglyph.Recording
{
	public enum VideoEncoderState { Idle, Recording, Finishing, Saved, Failed }

	/// <summary>A platform's video file writer: H.264/HEVC video and AAC audio into one file, saved to the device's photo library.</summary>
	internal interface IVideoEncoder
	{
		VideoEncoderState State { get; }
		string Error { get; }

		bool Begin(string filePath, int width, int height, int framesPerSecond, int audioSampleRate, int audioChannels);

		/// <summary>One frame of RGBA32 pixels, bottom row first, drawn at <paramref name="seconds"/> after Begin.</summary>
		void AppendVideo(NativeArray<byte> rgba, double seconds);

		/// <summary>Interleaved float samples whose first sample plays at <paramref name="seconds"/> after Begin.</summary>
		void AppendAudio(NativeArray<float> samples, int sampleCount, double seconds);

		void Finish();
	}
}
