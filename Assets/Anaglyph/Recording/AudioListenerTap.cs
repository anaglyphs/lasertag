using Unity.Collections;
using UnityEngine;

namespace Anaglyph.Recording
{
	/// <summary>Collects everything the AudioListener on this GameObject hears, for the main thread to take.</summary>
	[RequireComponent(typeof(AudioListener))]
	internal sealed class AudioListenerTap : MonoBehaviour
	{
		private const int MaxBufferedSeconds = 2;

		private readonly object gate = new();
		private float[] buffered;
		private int bufferedCount;

		public int Channels { get; private set; }

		public void Prepare(int sampleRate, int channels)
		{
			lock (gate)
			{
				Channels = channels;
				buffered = new float[sampleRate * channels * MaxBufferedSeconds];
				bufferedCount = 0;
			}
		}

		// audio thread
		private void OnAudioFilterRead(float[] data, int channels)
		{
			lock (gate)
			{
				if (buffered == null || channels != Channels) return;
				int count = Mathf.Min(data.Length, buffered.Length - bufferedCount);
				System.Array.Copy(data, 0, buffered, bufferedCount, count);
				bufferedCount += count;
			}
		}

		/// <summary>Moves what has been heard since the last call into <paramref name="destination"/>; returns the sample count.</summary>
		public int Take(NativeArray<float> destination)
		{
			lock (gate)
			{
				if (buffered == null) return 0;
				int count = Mathf.Min(bufferedCount, destination.Length);
				count -= count % Channels;
				NativeArray<float>.Copy(buffered, 0, destination, 0, count);
				bufferedCount -= count;
				System.Array.Copy(buffered, count, buffered, 0, bufferedCount);
				return count;
			}
		}
	}
}
