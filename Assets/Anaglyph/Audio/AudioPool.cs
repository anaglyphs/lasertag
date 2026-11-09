using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.Audio
{
	[DefaultExecutionOrder(-50)]
	public static class AudioPool
	{
		private const int InitSourceCount = 10;
		private const float ClearCutoffHz = 22000f;
		private const float MuffledCutoffHz = 800f;
		private static readonly List<AudioSource> allSources = new();
		private static readonly List<AudioLowPassFilter> allFilters = new();

		/// <summary>Moves and muffles pooled sounds that something stands in the way of. Null = none.</summary>
		public static ISoundRouting Routing { get; set; }

		private static int numPlaying = 0;
		private static int poolIndex = 0;

		[RuntimeInitializeOnLoadMethod]
		private static void Init()
		{
			allSources.Clear();
			allFilters.Clear();
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
			allFilters.Add(sourceObj.AddComponent<AudioLowPassFilter>());
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

			float muffle = 0;
			if (Routing != null && Routing.TryRoute(pos, out Vector3 apparentSource, out float routedMuffle))
			{
				pos = apparentSource;
				muffle = routedMuffle;
			}
			allFilters[poolIndex].cutoffFrequency =
				ClearCutoffHz * Mathf.Pow(MuffledCutoffHz / ClearCutoffHz, Mathf.Clamp01(muffle));

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