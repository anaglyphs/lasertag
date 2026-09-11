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

		public static string Get(string panel, string key) => key == null ? null :
			LocalizationSettings.StringDatabase.GetLocalizedString(panel + "Menu", key);

		public static string Format(string panel, string key, params object[] arguments) =>
			LocalizationSettings.StringDatabase.GetLocalizedString(panel + "Menu", key, arguments);

		public static LocalizedString String(string panel, string key, params object[] arguments) =>
			new LocalizedString(panel + "Menu", key) { Arguments = arguments };

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
