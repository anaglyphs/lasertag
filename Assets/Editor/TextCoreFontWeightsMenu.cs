using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

public static class TextCoreFontWeightsMenu
{
    private const string MenuPath = "Assets/TextCore/Populate Font Weights from Names";
    private static readonly Regex FontNamePattern = new Regex(
        @"^(?<family>.+?)[\s_-]+(?<weight>ExtraLight|UltraLight|SemiBold|DemiBold|ExtraBold|UltraBold|Thin|Light|Regular|Normal|Book|Medium|Bold|Heavy|Black|[1-9]00)?[\s_-]*(?<italic>Italic|Oblique)?(?<suffix>\s+SDF(?:\s+.*)?)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [MenuItem(MenuPath)]
    private static void PopulateSelected()
    {
        foreach (var font in Selection.objects.OfType<FontAsset>())
            Populate(font);
    }

    [MenuItem(MenuPath, true)]
    private static bool ValidateSelection() => Selection.objects.OfType<FontAsset>().Any();

    [MenuItem("CONTEXT/FontAsset/Populate Font Weights from Names")]
    private static void PopulateContext(MenuCommand command)
    {
        if (command.context is FontAsset font)
            Populate(font);
    }

    private static void Populate(FontAsset font)
    {
        var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(font))?.Replace('\\', '/');
        if (string.IsNullOrEmpty(folder) || !TryParseName(font.name, out _, out _, out _))
        {
            Debug.LogWarning($"Cannot populate {font.name}: expected a saved font named Family-WeightItalic SDF.", font);
            return;
        }

        var matches = FindMatches(font.name, folder);
        var serializedFont = new SerializedObject(font);
        var table = serializedFont.FindProperty("m_FontWeightTable");
        int changed = 0;
        foreach (var match in matches)
        {
            var slot = table.GetArrayElementAtIndex(match.Key / 2);
            var field = slot.FindPropertyRelative(match.Key % 2 == 0 ? "regularTypeface" : "italicTypeface");
            if (field.objectReferenceValue == match.Value)
                continue;

            field.objectReferenceValue = match.Value;
            changed++;
        }

        if (changed > 0)
        {
            Undo.SetCurrentGroupName("Populate Font Weights from Names");
            serializedFont.ApplyModifiedProperties();
            AssetDatabase.SaveAssetIfDirty(font);
        }
        Debug.Log($"{font.name}: matched {matches.Count} weight/style slots; updated {changed}. Unmatched slots were preserved.", font);
    }

    private static Dictionary<int, FontAsset> FindMatches(string fontName, string folder)
    {
        var matches = new Dictionary<int, FontAsset>();
        var ambiguousSlots = new HashSet<int>();
        if (!TryParseName(fontName, out var family, out var suffix, out _))
            return matches;

        foreach (var guid in AssetDatabase.FindAssets("t:FontAsset", new[] { folder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetDirectoryName(path)?.Replace('\\', '/') != folder)
                continue;

            var candidate = AssetDatabase.LoadAssetAtPath<FontAsset>(path);
            if (candidate == null || !TryParseName(candidate.name, out var candidateFamily, out var candidateSuffix, out int slot)
                || !string.Equals(family, candidateFamily, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(suffix, candidateSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (matches.ContainsKey(slot))
                ambiguousSlots.Add(slot);
            else
                matches.Add(slot, candidate);
        }

        foreach (int slot in ambiguousSlots)
        {
            matches.Remove(slot);
            Debug.LogWarning($"{fontName}: multiple matches for weight {slot / 2 * 100} ({(slot % 2 == 0 ? "regular" : "italic")}); slot left unchanged.");
        }
        return matches;
    }

    private static bool TryParseName(string name, out string family, out string suffix, out int slot)
    {
        var match = FontNamePattern.Match(name);
        family = match.Groups["family"].Value;
        suffix = match.Groups["suffix"].Value;
        slot = 0;
        if (!match.Success || (!match.Groups["weight"].Success && !match.Groups["italic"].Success))
            return false;

        int weight = match.Groups["weight"].Value.ToLowerInvariant() switch
        {
            "thin" => 1,
            "extralight" or "ultralight" => 2,
            "light" => 3,
            "" or "regular" or "normal" or "book" => 4,
            "medium" => 5,
            "semibold" or "demibold" => 6,
            "bold" => 7,
            "extrabold" or "ultrabold" or "heavy" => 8,
            "black" => 9,
            var numeric => int.Parse(numeric) / 100
        };
        slot = weight * 2 + (match.Groups["italic"].Success ? 1 : 0);
        return true;
    }
}
