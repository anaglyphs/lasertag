using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph
{
	public class AudioPool : MonoBehaviour
	{
		public static AudioPool Instance { get; private set; }
		[SerializeField] private int initialNumSources = 10;
		private readonly List<AudioSource> allSources = new();

		private int numPlaying = 0;
		private int poolIndex = 0;

		private void Awake()
		{
			Instance = this;
			for (int i = 0; i < initialNumSources; i++) InstantiateNewSource();
		}

		private void InstantiateNewSource()
		{
			GameObject sourceObj = new($"Source {allSources.Count}");
			AudioSource source = sourceObj.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 1.0f;
			source.dopplerLevel = 0.0f;
			source.gameObject.transform.SetParent(transform);
			allSources.Add(source);
		}

		public static void Play(AudioClip clip, Vector3 pos, float vol = 1)
		{
			Instance._Play(clip, pos, vol);
		}

		private async void _Play(AudioClip clip, Vector3 pos, float vol = 1)
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
				player.transform.position = pos;
				player.PlayOneShot(clip, vol);

				await Awaitable.WaitForSecondsAsync(clip.length, destroyCancellationToken);
				destroyCancellationToken.ThrowIfCancellationRequested();

				numPlaying--;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}