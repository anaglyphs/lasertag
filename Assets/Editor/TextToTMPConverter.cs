#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.EditorTools
{
	/// <summary>
	/// One-shot utility to replace classic UnityEngine.UI.Text components with
	/// TextMeshProUGUI, copying across the properties that map cleanly. Operates
	/// directly on prefab assets so nested/variant prefabs stay intact.
	///
	/// This does NOT re-link [SerializeField] TMP_Text references on other scripts
	/// (e.g. GameHUD.timerLabel) — Unity drops those the moment the field type
	/// changes from Text to TMP_Text. The console output lists every GameObject it
	/// converted so you can re-drag the handful of broken slots in the Inspector.
	///
	/// There is no Undo — rely on version control. Commit/stash before running.
	/// </summary>
	public static class TextToTMPConverter
	{
		// Zector SDF — the project's HUD font. Mapped onto every converted label.
		private const string FontAssetPath =
			"Assets/Anaglyph/LaserTag/Interface/Fonts/zector/Zector SDF.asset";

		// The prefabs that still hold classic Text components (no scenes affected).
		private static readonly string[] KnownPrefabs =
		{
			"Assets/Anaglyph/LaserTag/Interface/HUD/HUD.prefab",
			"Assets/Anaglyph/LaserTag/Interface/HUD/Hand HUD.prefab",
			"Assets/Anaglyph/LaserTag/Interface/Menu/Console Panel.prefab",
			"Assets/Anaglyph/LaserTag/Interface/Menu/Settings Panel.prefab",
		};

		[MenuItem("Tools/TMP Conversion/Convert Known Lasertag Prefabs")]
		private static void ConvertKnownPrefabs()
		{
			if (!EditorUtility.DisplayDialog("Convert Text → TextMeshPro",
				$"This will rewrite {KnownPrefabs.Length} prefab assets in place, replacing every " +
				"UnityEngine.UI.Text with TextMeshProUGUI.\n\nThere is no Undo — make sure your work " +
				"is committed first.\n\nProceed?", "Convert", "Cancel"))
				return;

			int total = 0;
			foreach (string path in KnownPrefabs)
				total += ConvertPrefabAsset(path);

			AssetDatabase.SaveAssets();
			Debug.Log($"[TextToTMP] Done. Converted {total} Text component(s) across {KnownPrefabs.Length} prefab(s). " +
				"Re-link any now-empty TMP_Text fields in the Inspector (see logs above).");
		}

		[MenuItem("Tools/TMP Conversion/Convert Selected Prefabs")]
		private static void ConvertSelectedPrefabs()
		{
			int total = 0;
			int count = 0;
			foreach (Object obj in Selection.objects)
			{
				string path = AssetDatabase.GetAssetPath(obj);
				if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
					continue;

				total += ConvertPrefabAsset(path);
				count++;
			}

			if (count == 0)
			{
				Debug.LogWarning("[TextToTMP] No prefab assets selected in the Project window.");
				return;
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[TextToTMP] Done. Converted {total} Text component(s) across {count} selected prefab(s).");
		}

		private static int ConvertPrefabAsset(string path)
		{
			TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
			if (font == null)
			{
				Debug.LogError($"[TextToTMP] Could not load TMP font asset at \"{FontAssetPath}\". Aborting \"{path}\".");
				return 0;
			}

			GameObject root = PrefabUtility.LoadPrefabContents(path);
			if (root == null)
			{
				Debug.LogError($"[TextToTMP] Could not load prefab contents at \"{path}\".");
				return 0;
			}

			int converted = 0;
			try
			{
				// Collect first; we mutate the hierarchy (destroy + add) as we go.
				Text[] texts = root.GetComponentsInChildren<Text>(true);
				foreach (Text text in texts)
				{
					string goName = GetHierarchyPath(text.transform, root.transform);
					Convert(text, font);
					Debug.Log($"[TextToTMP] {System.IO.Path.GetFileName(path)} → converted \"{goName}\"");
					converted++;
				}

				if (converted > 0)
					PrefabUtility.SaveAsPrefabAsset(root, path);
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(root);
			}

			return converted;
		}

		private static void Convert(Text text, TMP_FontAsset font)
		{
			// Snapshot the source properties before destroying the component.
			GameObject go = text.gameObject;
			string str = text.text;
			float fontSize = text.fontSize;
			Color color = text.color;
			FontStyle fontStyle = text.fontStyle;
			TextAnchor anchor = text.alignment;
			bool enabled = text.enabled;
			bool raycastTarget = text.raycastTarget;
			bool maskable = text.maskable;
			bool richText = text.supportRichText;
			bool bestFit = text.resizeTextForBestFit;
			int minSize = text.resizeTextMinSize;
			int maxSize = text.resizeTextMaxSize;

			// Text and TextMeshProUGUI are both Graphics that fight over the
			// CanvasRenderer, so the old one must go before the new one is added.
			Object.DestroyImmediate(text, true);

			TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
			tmp.font = font;
			tmp.text = str;
			tmp.fontSize = fontSize;
			tmp.color = color;
			tmp.fontStyle = MapStyle(fontStyle);
			tmp.alignment = MapAlignment(anchor);
			tmp.enabled = enabled;
			tmp.raycastTarget = raycastTarget;
			tmp.maskable = maskable;
			tmp.richText = richText;

			if (bestFit)
			{
				tmp.enableAutoSizing = true;
				tmp.fontSizeMin = minSize;
				tmp.fontSizeMax = maxSize;
			}
		}

		private static FontStyles MapStyle(FontStyle style)
		{
			switch (style)
			{
				case FontStyle.Bold: return FontStyles.Bold;
				case FontStyle.Italic: return FontStyles.Italic;
				case FontStyle.BoldAndItalic: return FontStyles.Bold | FontStyles.Italic;
				default: return FontStyles.Normal;
			}
		}

		private static TextAlignmentOptions MapAlignment(TextAnchor anchor)
		{
			switch (anchor)
			{
				case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
				case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
				case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
				case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
				case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
				case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
				case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
				case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
				case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
				default: return TextAlignmentOptions.Center;
			}
		}

		private static string GetHierarchyPath(Transform t, Transform root)
		{
			var parts = new List<string>();
			while (t != null && t != root)
			{
				parts.Insert(0, t.name);
				t = t.parent;
			}
			parts.Insert(0, root.name);
			return string.Join("/", parts);
		}
	}
}
#endif
