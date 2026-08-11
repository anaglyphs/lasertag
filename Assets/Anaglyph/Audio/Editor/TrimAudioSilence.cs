#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEngine;

namespace Anaglyph.Editor
{
	/// Right-click an AudioClip and trim the silence off its head and tail.
	/// Always writes 16-bit PCM WAV next to the source; a compressed source is
	/// left alone and gains a .wav sibling, since we can't re-encode it.
	public static class TrimAudioSilence
	{
		private const string MenuPath = "Assets/Trim Audio Silence";

		private const float SilenceThresholdDb = -45f;
		private const float PaddingSeconds = 0.005f;

		[MenuItem(MenuPath, true)]
		private static bool ValidateTrim() => Selection.GetFiltered<AudioClip>(SelectionMode.Assets).Length > 0;

		[MenuItem(MenuPath)]
		private static void Trim()
		{
			AudioClip[] clips = Selection.GetFiltered<AudioClip>(SelectionMode.Assets);

			string[] paths = new string[clips.Length];
			for (int i = 0; i < clips.Length; i++)
				paths[i] = AssetDatabase.GetAssetPath(clips[i]);

			if (!EditorUtility.DisplayDialog("Trim Audio Silence",
				$"Trim leading and trailing silence below {SilenceThresholdDb} dB from " +
				$"{paths.Length} clip(s)?\n\nWAV sources are overwritten in place.", "Trim", "Cancel"))
				return;

			// Not batched with StartAssetEditing — each clip needs its reimport to
			// actually land before we can read samples back out of it.
			foreach (string path in paths)
				TrimClipAtPath(path);

			AssetDatabase.Refresh();
		}

		public static void TrimClipAtPath(string path)
		{
			AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
			if (importer == null)
				return;

			AudioImporterSampleSettings originalSettings = importer.defaultSampleSettings;
			bool needsReimport = originalSettings.loadType != AudioClipLoadType.DecompressOnLoad
				|| !originalSettings.preloadAudioData;

			// GetData only works on a decompressed, loaded clip
			if (needsReimport)
			{
				AudioImporterSampleSettings readable = originalSettings;
				readable.loadType = AudioClipLoadType.DecompressOnLoad;
				readable.preloadAudioData = true;
				importer.defaultSampleSettings = readable;
				importer.SaveAndReimport();
			}

			try
			{
				AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
				if (clip == null)
					return;

				float[] samples = new float[clip.samples * clip.channels];
				if (!clip.GetData(samples, 0))
				{
					Debug.LogWarning($"Could not read samples from {path}", clip);
					return;
				}

				int channels = clip.channels;
				int frequency = clip.frequency;
				string outputPath = Path.ChangeExtension(path, ".wav");

				if (!FindLoudRange(samples, channels, out int firstFrame, out int lastFrame))
				{
					Debug.LogWarning($"{Path.GetFileName(path)} is silent throughout — skipped.", clip);
					return;
				}

				int padding = Mathf.RoundToInt(PaddingSeconds * frequency);
				int frameCount = samples.Length / channels;
				firstFrame = Mathf.Max(0, firstFrame - padding);
				lastFrame = Mathf.Min(frameCount - 1, lastFrame + padding);

				int trimmedFrames = lastFrame - firstFrame + 1;
				if (trimmedFrames == frameCount && outputPath == path)
				{
					Debug.Log($"{Path.GetFileName(path)} has no silence to trim.", clip);
					return;
				}

				WriteWav(outputPath, samples, firstFrame * channels, trimmedFrames * channels, channels, frequency);

				float removedSeconds = (frameCount - trimmedFrames) / (float)frequency;
				Debug.Log($"Trimmed {removedSeconds:0.###}s of silence — {outputPath}");
			}
			finally
			{
				if (needsReimport)
				{
					importer.defaultSampleSettings = originalSettings;
					importer.SaveAndReimport();
				}
			}
		}

		/// First and last frame whose loudest channel exceeds the threshold
		private static bool FindLoudRange(float[] samples, int channels, out int firstFrame, out int lastFrame)
		{
			float threshold = Mathf.Pow(10f, SilenceThresholdDb / 20f);

			firstFrame = -1;
			lastFrame = -1;

			for (int i = 0; i < samples.Length; i++)
			{
				if (Mathf.Abs(samples[i]) < threshold)
					continue;

				int frame = i / channels;
				if (firstFrame < 0)
					firstFrame = frame;
				lastFrame = frame;
			}

			return firstFrame >= 0;
		}

		private static void WriteWav(string path, float[] samples, int offset, int count, int channels, int frequency)
		{
			const int bitsPerSample = 16;
			int byteRate = frequency * channels * bitsPerSample / 8;
			int dataBytes = count * bitsPerSample / 8;

			using FileStream stream = new FileStream(path, FileMode.Create);
			using BinaryWriter writer = new BinaryWriter(stream);

			writer.Write(new char[] { 'R', 'I', 'F', 'F' });
			writer.Write(36 + dataBytes);
			writer.Write(new char[] { 'W', 'A', 'V', 'E' });

			writer.Write(new char[] { 'f', 'm', 't', ' ' });
			writer.Write(16); // subchunk size
			writer.Write((ushort)1); // PCM
			writer.Write((ushort)channels);
			writer.Write(frequency);
			writer.Write(byteRate);
			writer.Write((ushort)(channels * bitsPerSample / 8)); // block align
			writer.Write((ushort)bitsPerSample);

			writer.Write(new char[] { 'd', 'a', 't', 'a' });
			writer.Write(dataBytes);

			for (int i = 0; i < count; i++)
				writer.Write((short)(Mathf.Clamp(samples[offset + i], -1f, 1f) * short.MaxValue));
		}
	}
}

#endif