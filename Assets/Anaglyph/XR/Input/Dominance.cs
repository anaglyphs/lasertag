using System;
using UnityEngine;

namespace Anaglyph.XR.Input
{
	/// <summary>The player's dominant hand and eye, saved on this headset.</summary>
	public static class Dominance
	{
		private const string HandPref = "Dominance.Hand";
		private const string EyePref = "Dominance.Eye";

		private static Handedness? hand, eye;

		public static event Action Changed;

		public static Handedness Hand
		{
			get => hand ??= Load(HandPref);
			set => Save(ref hand, value, HandPref);
		}

		public static Handedness Eye
		{
			get => eye ??= Load(EyePref);
			set => Save(ref eye, value, EyePref);
		}

		private static Handedness Load(string pref) =>
			(Handedness)PlayerPrefs.GetInt(pref, (int)Handedness.Right);

		private static void Save(ref Handedness? field, Handedness value, string pref)
		{
			if (field == value) return;
			field = value;
			PlayerPrefs.SetInt(pref, (int)value);
			PlayerPrefs.Save();
			Changed?.Invoke();
		}
	}
}
