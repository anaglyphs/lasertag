using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.Text;

[InitializeOnLoad]
public static class TextCoreOpticalBaselineMenu
{
	private const string ApplyActionName = "Set TextCore Optical Baseline";
	private const string AssetMenuPath = "Assets/TextCore/Set Optical Baseline from Cap Height";
	private const string ContextMenuPath = "CONTEXT/FontAsset/Set Optical Baseline from Cap Height";
	private static readonly GUIContent ApplyButtonContent = new GUIContent(
		"Set Optical Baseline from Cap Height",
		"Centers the cap-height box within the font's ascent and descent line box.");

	static TextCoreOpticalBaselineMenu()
	{
		Editor.finishedDefaultHeaderGUI -= DrawFontAssetInspectorHeader;
		Editor.finishedDefaultHeaderGUI += DrawFontAssetInspectorHeader;
	}

	[MenuItem(AssetMenuPath)]
	private static void ApplyToSelectedFontAssets()
	{
		Apply(Selection.objects.OfType<FontAsset>());
	}

	[MenuItem(AssetMenuPath, true)]
	private static bool ValidateSelectedFontAssets()
	{
		return Selection.objects.OfType<FontAsset>().Any();
	}

	[MenuItem(ContextMenuPath)]
	private static void ApplyFromContextMenu(MenuCommand command)
	{
		Apply(GetSelectedFontAssets(command.context as FontAsset));
	}

	private static void DrawFontAssetInspectorHeader(UnityEditor.Editor editor)
	{
		var fontAssets = editor.targets.OfType<FontAsset>().ToArray();
		if (fontAssets.Length == 0)
			return;

		EditorGUILayout.Space(2);
		if (GUILayout.Button(ApplyButtonContent, EditorStyles.miniButton))
			Apply(fontAssets);
	}

	private static IEnumerable<FontAsset> GetSelectedFontAssets(FontAsset context)
	{
		var selectedFontAssets = Selection.objects.OfType<FontAsset>().Distinct().ToArray();
		if (context != null && !selectedFontAssets.Contains(context))
			return new[] { context };

		return selectedFontAssets;
	}

	private static void Apply(IEnumerable<FontAsset> fontAssets)
	{
		var changes = new List<BaselineChange>();
		foreach (var fontAsset in fontAssets.Where(fontAsset => fontAsset != null).Distinct())
		{
			if (!TryCalculateOpticalBaseline(fontAsset.faceInfo, out float baseline))
			{
				Debug.LogWarning($"Cannot calculate an optical baseline for {fontAsset.name}: its face metrics are invalid.", fontAsset);
				continue;
			}

			if (!Mathf.Approximately(fontAsset.faceInfo.baseline, baseline))
				changes.Add(new BaselineChange(fontAsset, baseline));
		}

		if (changes.Count == 0)
		{
			Debug.Log("Selected TextCore font assets already use their calculated optical baselines.");
			return;
		}

		Undo.RecordObjects(changes.Select(change => (Object)change.FontAsset).ToArray(), ApplyActionName);

		foreach (var change in changes)
		{
			var faceInfo = change.FontAsset.faceInfo;
			faceInfo.baseline = change.Baseline;
			change.FontAsset.faceInfo = faceInfo;
			EditorUtility.SetDirty(change.FontAsset);
			AssetDatabase.SaveAssetIfDirty(change.FontAsset);
		}

		Debug.Log($"Set the optical baseline on {changes.Count} TextCore font asset{(changes.Count == 1 ? string.Empty : "s")}.");
	}

	private static bool TryCalculateOpticalBaseline(FaceInfo faceInfo, out float baseline)
	{
		baseline = (faceInfo.ascentLine + faceInfo.descentLine - faceInfo.capLine) / 2f;
		return faceInfo.ascentLine > faceInfo.descentLine
			&& faceInfo.capLine > 0f
			&& !float.IsNaN(baseline)
			&& !float.IsInfinity(baseline);
	}

	private readonly struct BaselineChange
	{
		public BaselineChange(FontAsset fontAsset, float baseline)
		{
			FontAsset = fontAsset;
			Baseline = baseline;
		}

		public FontAsset FontAsset { get; }
		public float Baseline { get; }
	}
}
