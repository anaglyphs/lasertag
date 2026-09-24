using Anaglyph.Speech;
using UnityEngine;

namespace Anaglyph.LaserTag.Weapons
{
	public class WandVoiceInput : MonoBehaviour
	{
		[SerializeField] private VoskSpeechRecognizer recognizer;
		private readonly SpokenSpellTracker spells = new();
		private bool paused;

		private void Awake() => recognizer.Phrases = SpokenSpellTracker.Phrases;
		private void OnEnable() => recognizer.TranscriptionUpdated += OnTranscription;

		private void OnDisable()
		{
			recognizer.TranscriptionUpdated -= OnTranscription;
			recognizer.enabled = false;
			spells.Reset();
		}

		private void Update()
		{
			bool listening = !paused && Wand.ActiveWands.Count > 0 && WeaponsManagement.CanFire;
			if (recognizer.enabled == listening)
				return;
			spells.Reset();
			recognizer.enabled = listening;
		}

		private void OnApplicationPause(bool isPaused)
		{
			paused = isPaused;
			if (paused)
			{
				recognizer.enabled = false;
				spells.Reset();
			}
		}

		private void OnTranscription(string text, bool isFinal)
		{
			if (paused || !WeaponsManagement.CanFire)
			{
				spells.Reset();
				return;
			}
			spells.Process(text, isFinal, Cast);
		}

		private static void Cast(Wand.Spell spell)
		{
			foreach (Wand wand in Wand.ActiveWands)
				wand.Cast(spell);
		}
	}
}
