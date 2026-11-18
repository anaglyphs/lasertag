using Unity.Collections;

namespace Anaglyph.Recording
{
	public enum VideoEncoderState { Idle, Recording, Finishing, Saved, Failed }

	/// <summary>Separate audio tracks in the file. Values match the stream numbers in the native encoders.</summary>
	internal enum AudioTrack { Game = 1, Microphone = 2 }

	/// <summary>A platform's video file writer: H.264/HEVC video and AAC audio tracks into one file, saved to the device's photo library.</summary>
	internal interface IVideoEncoder
	{
		VideoEncoderState State { get; }
		string Error { get; }

		/// <summary>A <paramref name="microphoneSampleRate"/> of 0 leaves out the microphone track.</summary>
		bool Begin(string filePath, int width, int height, int framesPerSecond, int audioSampleRate, int audioChannels,
			int microphoneSampleRate, int microphoneChannels);

		/// <summary>One frame of RGBA32 pixels, bottom row first, drawn at <paramref name="seconds"/> after Begin.</summary>
		void AppendVideo(NativeArray<byte> rgba, double seconds);

		/// <summary>Interleaved float samples whose first sample plays at <paramref name="seconds"/> after Begin.</summary>
		void AppendAudio(AudioTrack track, NativeArray<float> samples, int sampleCount, double seconds);

		void Finish();
	}
}
