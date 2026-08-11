using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	[DefaultExecutionOrder(-50)]
	public static class AudioPool
	{
		private const int InitSourceCount = 10;
		private static readonly List<AudioSource> allSources = new();

		private static int numPlaying = 0;
		private static int poolIndex = 0;

		[RuntimeInitializeOnLoadMethod]
		private static void Init()
		{
			allSources.Clear();
			for (int i = 0; i < InitSourceCount; i++) InstantiateNewSource();
		}

		private static void InstantiateNewSource()
		{
			GameObject sourceObj = new($"Audiosource {allSources.Count}");
			AudioSource source = sourceObj.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 1.0f;
			source.dopplerLevel = 0.0f;
			source.gameObject.SetActive(false);
			allSources.Add(source);
		}

		public static void Play(AudioClip clip, Vector3 pos, float vol = 1)
		{
			_Play(clip, pos, vol);
		}

		private static async void _Play(AudioClip clip, Vector3 pos, float vol = 1)
		{
			if (numPlaying == allSources.Count)
			{
				// all AudioSources are playing
				// Instantiate a new one
				InstantiateNewSource();
				poolIndex = allSources.Count - 1;
			}
			else
			{
				// find next available AudioSource
				foreach (AudioSource _ in allSources)
				{
					poolIndex = (poolIndex + 1) % allSources.Count;
					if (!allSources[poolIndex].isPlaying)
						break;
				}
			}

			AudioSource player = allSources[poolIndex];

			try
			{
				numPlaying++;
				player.gameObject.SetActive(true);
				player.transform.position = pos;
				player.PlayOneShot(clip, vol);

				await Awaitable.WaitForSecondsAsync(clip.length);
				player.gameObject.SetActive(false);

				numPlaying--;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}