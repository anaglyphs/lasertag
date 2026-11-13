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

		// echoes lose to direct sounds when Unity runs out of real voices
		private const int DirectPriority = 128;
		private const int ReflectionPriority = 200;
		private const int MaxReflectionVoices = 8;

		private static readonly List<AudioSource> allSources = new();
		private static readonly List<AudioLowPassFilter> allFilters = new();
		private static readonly List<SoundReflection> reflections = new();

		/// <summary>Moves and muffles pooled sounds that something stands in the way of. Null = none.</summary>
		public static ISoundRouting Routing { get; set; }

		/// <summary>Adds echoes of pooled sounds. Null = none.</summary>
		public static ISoundReflections Reflections { get; set; }

		private static int numPlaying = 0;
		private static int numReflectionVoices = 0;
		private static int poolIndex = 0;

		[RuntimeInitializeOnLoadMethod]
		private static void Init()
		{
			allSources.Clear();
			allFilters.Clear();
			numPlaying = 0;
			numReflectionVoices = 0;
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

		/// <param name="fullVolumeDistance">Distance the sound keeps full volume to; beyond it, volume halves per doubling of distance.</param>
		public static void Play(AudioClip clip, Vector3 pos, float vol = 1, float fullVolumeDistance = 1)
		{
			Vector3 heardFrom = pos;
			float muffle = 0;
			if (Routing != null && Routing.TryRoute(pos, out Vector3 apparentSource, out float routedMuffle))
			{
				heardFrom = apparentSource;
				muffle = routedMuffle;
			}

			PlayVoice(clip, heardFrom, vol, fullVolumeDistance, muffle, 0, false);

			if (Reflections == null) return;

			reflections.Clear();
			Reflections.FindReflections(pos, reflections);
			foreach (SoundReflection reflection in reflections)
			{
				if (numReflectionVoices >= MaxReflectionVoices) break;
				PlayVoice(clip, reflection.apparentSource, vol * reflection.gain, fullVolumeDistance, reflection.muffle,
					reflection.delaySeconds, true);
			}
		}

		private static async void PlayVoice(AudioClip clip, Vector3 pos, float vol, float fullVolumeDistance, float muffle,
			float delaySeconds,
			bool isReflection)
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
			allFilters[poolIndex].cutoffFrequency =
				ClearCutoffHz * Mathf.Pow(MuffledCutoffHz / ClearCutoffHz, Mathf.Clamp01(muffle));

			try
			{
				numPlaying++;
				if (isReflection) numReflectionVoices++;

				player.gameObject.SetActive(true);
				player.transform.position = pos;
				player.priority = isReflection ? ReflectionPriority : DirectPriority;
				player.clip = clip;
				player.volume = vol;
				player.minDistance = fullVolumeDistance;
				player.PlayDelayed(delaySeconds);

				await Awaitable.WaitForSecondsAsync(delaySeconds + clip.length);
				player.gameObject.SetActive(false);

				numPlaying--;
				if (isReflection) numReflectionVoices--;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}
