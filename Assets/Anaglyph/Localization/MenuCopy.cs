using System;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.SmartFormat.PersistentVariables;
using UnityEngine.UIElements;

namespace Anaglyph
{
	/// <summary>Native Localization access for controller-owned text. Static text binds in UXML.</summary>
	public static class MenuCopy
	{
		public static event Action Changed = delegate { };

		static MenuCopy() => LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;

		private static void OnLocaleChanged(Locale _) => Changed.Invoke();

		public static string Get(string table, string key) => key == null ? null :
			LocalizationSettings.StringDatabase.GetLocalizedString(table, key);

		public static string Format(string table, string key, params object[] arguments) =>
			LocalizationSettings.StringDatabase.GetLocalizedString(table, key, arguments);

		public static LocalizedString String(string table, string key, params object[] arguments) =>
			new LocalizedString(table, key) { Arguments = arguments };

		public static void SetVariable(TextElement element, string name, string value)
		{
			var binding = element.GetBinding("text") as LocalizedString;
			if (binding == null || !binding.TryGetValue(name, out var variable) || variable is not StringVariable text)
				throw new InvalidOperationException($"Missing localized text variable '{name}' on '{element.name}'.");
			text.Value = value;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Reset() => Changed = delegate { };
	}
}
