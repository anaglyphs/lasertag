using Anaglyph.LaserTag.Matches;
using UnityEngine;

namespace Anaglyph.LaserTag.Player
{
	/// <summary>
	/// Muffles everything the local player hears as their health drops, fully muffled when dead.
	/// </summary>
	public class HealthMuffledHearing : MonoBehaviour
	{
		private const float ClearCutoffHz = 22000f;

		[SerializeField, Min(10f)] private float deadCutoffHz = 500f;

		private AudioListener listener;
		private AudioLowPassFilter listenerMuffle;

		private void Update()
		{
			MainPlayer player = MainPlayer.Instance;
			float muffle = player != null && player.IsInPlay
				? 1f - Mathf.Clamp01(player.Health / MatchSettings.MaxHealth)
				: 0f;

			ApplyMuffle(muffle);
		}

		private void OnDisable() => ApplyMuffle(0f);

		private void ApplyMuffle(float muffle)
		{
			if (listener == null || !listener.isActiveAndEnabled)
			{
				if (listenerMuffle != null) Destroy(listenerMuffle);
				listenerMuffle = null;
				listener = FindAnyObjectByType<AudioListener>();
			}

			if (listener == null)
				return;

			if (listenerMuffle == null)
			{
				if (muffle <= 0f) return;
				listenerMuffle = listener.gameObject.AddComponent<AudioLowPassFilter>();
			}

			listenerMuffle.enabled = muffle > 0f;
			listenerMuffle.cutoffFrequency = ClearCutoffHz * Mathf.Pow(deadCutoffHz / ClearCutoffHz, muffle);
		}
	}
}
