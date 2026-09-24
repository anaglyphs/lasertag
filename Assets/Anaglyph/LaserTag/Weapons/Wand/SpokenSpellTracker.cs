using System;

namespace Anaglyph.LaserTag.Weapons
{
	public sealed class SpokenSpellTracker
	{
		public static readonly string[] Phrases = { "fireball", "lightning", "shield" };
		private readonly int[] emittedCounts = new int[Phrases.Length];

		public void Reset() => Array.Clear(emittedCounts, 0, emittedCounts.Length);

		public void Process(string text, bool isFinal, Action<Wand.Spell> cast)
		{
			string[] words = (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
			int[] counts = new int[Phrases.Length];
			foreach (string word in words)
			{
				for (int i = 0; i < Phrases.Length; i++)
				{
					if (!string.Equals(word, Phrases[i], StringComparison.OrdinalIgnoreCase))
						continue;
					if (++counts[i] > emittedCounts[i])
					{
						emittedCounts[i] = counts[i];
						cast((Wand.Spell)i);
					}
				}
			}
			if (isFinal)
				Reset();
		}
	}
}
