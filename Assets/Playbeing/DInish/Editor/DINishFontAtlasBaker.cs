using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Playbeing.DINish.Editor
{
    /// <summary>
    /// Bakes the DINish TextCore assets with the settings used by DINish-Regular SDF.
    /// Keeping the assets Dynamic means the pre-baked glyphs are available immediately,
    /// while any later missing glyphs can still be added at runtime.
    /// </summary>
    internal static class DINishFontAtlasBaker
    {
        private const string RootPath = "Assets/Playbeing/DInish";
        private const float AutoSizedPointSize = 99f;
        private const int AtlasSize = 1024;
        private const int Padding = 9;
        private const string ExtendedAsciiSequence = "32 - 126, 160 - 255, 8192 - 8303, 8364, 8482, 9633";

        [MenuItem("Tools/DINish/Bake Extended ASCII Atlases")]
        private static void BakeExtendedAsciiAtlases()
        {
            var assetGuids = AssetDatabase.FindAssets("t:FontAsset", new[] { RootPath });
            var characterSet = GetExtendedAsciiCharacters();
            var unavailableGlyphCounts = new List<string>();

            try
            {
                for (var i = 0; i < assetGuids.Length; i++)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(assetGuids[i]);
                    var fontAsset = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
                    if (fontAsset == null || fontAsset.sourceFontFile == null)
                    {
                        unavailableGlyphCounts.Add($"{assetPath}: missing FontAsset or source font");
                        continue;
                    }

                    EditorUtility.DisplayProgressBar(
                        "Baking DINish TextCore Atlases",
                        fontAsset.name,
                        (float)i / assetGuids.Length);

                    ApplySettings(fontAsset);
                    fontAsset.ClearFontAssetData();

                    fontAsset.TryAddCharacters(characterSet, out var missingCharacters, false);
                    if (missingCharacters.Length > 0)
                    {
                        unavailableGlyphCounts.Add(
                            $"{fontAsset.name}: {characterSet.Length - missingCharacters.Length}/{characterSet.Length} requested code points are present");
                    }

                    EditorUtility.SetDirty(fontAsset);
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(
                $"Baked Extended ASCII atlases for {assetGuids.Length} DINish TextCore font assets.\n" +
                "Some code points are not provided by DINish and were skipped:\n" +
                string.Join("\n", unavailableGlyphCounts));
        }

        private static void ApplySettings(FontAsset fontAsset)
        {
            var settings = fontAsset.fontAssetCreationEditorSettings;
            settings.pointSizeSamplingMode = 0; // Auto Sizing
            settings.pointSize = AutoSizedPointSize;
            settings.padding = Padding;
            settings.paddingMode = 2; // Pixels
            settings.packingMode = 0; // Fast
            settings.atlasWidth = AtlasSize;
            settings.atlasHeight = AtlasSize;
            settings.characterSetSelectionMode = 1; // Extended ASCII
            settings.characterSequence = ExtendedAsciiSequence;
            settings.renderMode = (int)GlyphRenderMode.SDFAA;
            settings.includeFontFeatures = false;

            fontAsset.fontAssetCreationEditorSettings = settings;
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fontAsset.isMultiAtlasTexturesEnabled = true;
            fontAsset.getFontFeatures = false;

            var serializedFontAsset = new SerializedObject(fontAsset);
            serializedFontAsset.FindProperty("m_AtlasWidth").intValue = AtlasSize;
            serializedFontAsset.FindProperty("m_AtlasHeight").intValue = AtlasSize;
            serializedFontAsset.FindProperty("m_AtlasPadding").intValue = Padding;
            serializedFontAsset.FindProperty("m_AtlasRenderMode").intValue = (int)GlyphRenderMode.SDFAA;
            serializedFontAsset.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
            serializedFontAsset.ApplyModifiedPropertiesWithoutUndo();
        }

        private static uint[] GetExtendedAsciiCharacters()
        {
            var characters = new List<uint>();
            AddRange(characters, 32, 126);
            AddRange(characters, 160, 255);
            AddRange(characters, 8192, 8303);
            characters.Add(8364); // Euro sign
            characters.Add(8482); // Trademark sign
            characters.Add(9633); // Black square
            return characters.ToArray();
        }

        private static void AddRange(List<uint> characters, uint first, uint last)
        {
            for (var character = first; character <= last; character++)
                characters.Add(character);
        }
    }
}
