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
			for (int i = 0; i < initialNumSources; i++)
			{
				InstantiateNewSource();
			}
		}

		private void InstantiateNewSource()
		{
			GameObject sourceObj = new GameObject($"Source {allSources.Count}");
			var source = sourceObj.AddComponent<AudioSource>();
			source.playOnAwake = false;
			source.spatialBlend = 1.0f;
			source.gameObject.transform.SetParent(transform);
			source.gameObject.SetActive(false); ;
			allSources.Add(source);
		}

		public static void Play(AudioClip clip, Vector3 pos, float vol = 1) 
			=> Instance.ActivateAndPlay(clip, pos, vol);

		private async void ActivateAndPlay(AudioClip clip, Vector3 pos, float vol = 1)
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
				foreach (var _ in allSources)
				{
					poolIndex = (poolIndex + 1) % allSources.Count;
					if (!allSources[poolIndex].gameObject.activeInHierarchy)
						break;
				}
			}
			
			AudioSource player = allSources[poolIndex];
			
			numPlaying++;
			player.transform.position = pos;
			player.gameObject.SetActive(true);
			player.PlayOneShot(clip, vol);
			
			await Awaitable.WaitForSecondsAsync(clip.length);
			
			player.gameObject.SetActive(false);
			numPlaying--;
		}
	}
}