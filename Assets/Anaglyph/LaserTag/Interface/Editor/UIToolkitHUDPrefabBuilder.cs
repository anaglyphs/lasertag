using System;
using System.Collections.Generic;
using Anaglyph.Lasertag.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Editor
{
	internal static class UIToolkitHUDPrefabBuilder
	{
		// This migrates the HUD and mapper palette prefabs once, and remains
		// available for explicit rebuilds.
		private const string HUDPrefabPath =
			"Assets/Anaglyph/LaserTag/Interface/HUD/HUD.prefab";
		private const string HandHUDPrefabPath =
			"Assets/Anaglyph/LaserTag/Interface/HUD/Hand HUD.prefab";
		private const string PalettePrefabPath =
			"Assets/Anaglyph/LaserTag/Interface/Menu/Mapper palette.prefab";

		private const string HUDUIDirectory =
			"Assets/Anaglyph/LaserTag/Interface/HUD/UI Toolkit";
		private const string HUDDocumentPath = HUDUIDirectory + "/HUD.uxml";
		private const string HandHUDDocumentPath = HUDUIDirectory + "/HandHUD.uxml";

		private const string MenuUIDirectory =
			"Assets/Anaglyph/LaserTag/Interface/Menu/UI Toolkit";
		private const string PaletteDocumentPath =
			MenuUIDirectory + "/MapperPalette.uxml";
		private const string StyleSheetPath = MenuUIDirectory + "/LaserTagMenu.uss";
		private const string PanelSettingsPath =
			MenuUIDirectory + "/LaserTagWorldSpacePanelSettings.asset";

		// spawnable map object prefabs, as wired in the original uGUI palette
		private const string BlueBaseGuid = "a41f71a8e984b494097a21f11c3c8f3c";
		private const string RedBaseGuid = "9f38ed223dc9ead41a2a38e807eb4f63";
		private const string BlueFlagGuid = "5a37dcb5ba0e707478906978c7dcd0ca";
		private const string RedFlagGuid = "09550731d948e3b4b867b662e681b59b";
		private const string BlasterPickupGuid = "5f246ade89ff64940aaa55e503fdd7f7";
		private const string AutomaticPickupGuid = "82bf6a83110c3417cbd1eaf175435329";
		private const string DebugWallGuid = "901145a346bfd4e05b05ae98543fd712";
		private const string DebugAnchorHandleGuid = "f324c7323ecc14d8a8e5a985103ab75a";

		private const float HUDScale = 0.1f;
		private static readonly Vector2 HUDDocumentSize = new(800f, 600f);

		private const float HandHUDScale = 0.1f;
		private static readonly Vector2 HandHUDDocumentSize = new(200f, 150f);

		private const float PalettePanelScale = 0.05f;
		private static readonly Vector2 PaletteDocumentSize = new(300f, 500f);

		[InitializeOnLoadMethod]
		private static void ScheduleInitialBuild()
		{
			EditorApplication.delayCall += BuildIfNeeded;
		}

		[MenuItem("Tools/Anaglyph/Rebuild UI Toolkit HUD")]
		public static void Rebuild()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				Debug.LogWarning("The UI Toolkit HUD cannot be rebuilt in Play Mode.");
				return;
			}

			ImportUIAssets();
			PanelSettings panelSettings = LoadPanelSettings();

			RebuildHUD(panelSettings);
			RebuildHandHUD(panelSettings);
			RebuildPalette(panelSettings);
		}

		private static void BuildIfNeeded()
		{
			if (EditorApplication.isCompiling ||
				EditorApplication.isUpdating ||
				EditorApplication.isPlayingOrWillChangePlaymode)
			{
				EditorApplication.delayCall += BuildIfNeeded;
				return;
			}

			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HUDPrefabPath);
			if (prefab != null && prefab.GetComponent<UIDocument>() == null)
				Rebuild();
		}

		private static void ImportUIAssets()
		{
			string[] paths =
			{
				StyleSheetPath,
				HUDDocumentPath,
				HandHUDDocumentPath,
				PaletteDocumentPath
			};

			foreach (string assetPath in paths)
				AssetDatabase.ImportAsset(
					assetPath,
					ImportAssetOptions.ForceSynchronousImport);
		}

		private static PanelSettings LoadPanelSettings()
		{
			PanelSettings panelSettings =
				AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			if (panelSettings == null)
				throw new InvalidOperationException(
					$"Could not load panel settings at '{PanelSettingsPath}'. " +
					"Run 'Tools/Anaglyph/Rebuild UI Toolkit Menu' first.");

			return panelSettings;
		}

		private static void RebuildHUD(PanelSettings panelSettings)
		{
			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(HUDPrefabPath);
			try
			{
				RemoveAllChildren(prefabRoot);
				RemoveComponents(prefabRoot);

				prefabRoot.layer = GetUILayer();
				prefabRoot.transform.localScale = Vector3.one * HUDScale;

				ConfigureDocument(
					prefabRoot, HUDDocumentPath, panelSettings, HUDDocumentSize);

				CreateChild<DeathHUD>(prefabRoot.transform, "Death");
				CreateChild<CountdownHUD>(prefabRoot.transform, "Countdown");

				GameEndHUD gameEnd =
					CreateChild<GameEndHUD>(prefabRoot.transform, "Game End");
				CreateScoreLabel(
					gameEnd.transform, "Red Score", 1, "game-end-red-score");
				CreateScoreLabel(
					gameEnd.transform, "Blue Score", 2, "game-end-blue-score");

				CreateChild<ConnectionHUD>(prefabRoot.transform, "Connecting");

				PrefabUtility.SaveAsPrefabAsset(prefabRoot, HUDPrefabPath);
				Debug.Log($"Rebuilt '{HUDPrefabPath}' as a world-space UI Toolkit panel.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}

		private static void RebuildHandHUD(PanelSettings panelSettings)
		{
			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(HandHUDPrefabPath);
			try
			{
				RemoveAllChildren(prefabRoot);
				RemoveComponents(prefabRoot);

				prefabRoot.layer = GetUILayer();
				prefabRoot.transform.localScale = Vector3.one * HandHUDScale;

				// added after the document so their OnEnable runs once the
				// visual tree exists
				ConfigureDocument(
					prefabRoot, HandHUDDocumentPath, panelSettings, HandHUDDocumentSize);

				prefabRoot.AddComponent<GameHUD>();

				HandHUDPositioner positioner =
					prefabRoot.AddComponent<HandHUDPositioner>();
				SerializedObject serializedPositioner = new(positioner);
				serializedPositioner.FindProperty("horizontalOffset").floatValue = 0.15f;
				serializedPositioner.FindProperty("handSwapTime").floatValue = 0.3f;
				serializedPositioner.FindProperty("handSwapThresh").floatValue = 0.1f;
				serializedPositioner.ApplyModifiedPropertiesWithoutUndo();

				CreateScoreLabel(
					prefabRoot.transform, "Timer Red Score", 1, "timer-red-score");
				CreateScoreLabel(
					prefabRoot.transform, "Timer Blue Score", 2, "timer-blue-score");
				CreateScoreLabel(
					prefabRoot.transform, "Goal Red Score", 1, "goal-red-score");
				CreateScoreLabel(
					prefabRoot.transform, "Goal Blue Score", 2, "goal-blue-score");

				PrefabUtility.SaveAsPrefabAsset(prefabRoot, HandHUDPrefabPath);
				Debug.Log(
					$"Rebuilt '{HandHUDPrefabPath}' as a world-space UI Toolkit panel.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}

		private static void RebuildPalette(PanelSettings panelSettings)
		{
			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PalettePrefabPath);
			try
			{
				RemoveAllChildren(prefabRoot);
				RemoveComponents(prefabRoot, "HandSubject", "HandMover", "Palette");

				GameObject panel = new("Palette Panel")
				{
					layer = GetUILayer()
				};

				Transform panelTransform = panel.transform;
				panelTransform.SetParent(prefabRoot.transform, false);
				panelTransform.localPosition = Vector3.zero;
				panelTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
				panelTransform.localScale = Vector3.one * PalettePanelScale;

				ConfigureDocument(
					panel, PaletteDocumentPath, panelSettings, PaletteDocumentSize);

				UIToolkitPanelXRSetup xrSetup =
					panel.AddComponent<UIToolkitPanelXRSetup>();
				xrSetup.Configure();

				PaletteMenu paletteMenu = panel.AddComponent<PaletteMenu>();
				SetObjectReference(
					paletteMenu, "blueBasePrefab", LoadMapObject(BlueBaseGuid));
				SetObjectReference(
					paletteMenu, "redBasePrefab", LoadMapObject(RedBaseGuid));
				SetObjectReference(
					paletteMenu, "blueFlagPrefab", LoadMapObject(BlueFlagGuid));
				SetObjectReference(
					paletteMenu, "redFlagPrefab", LoadMapObject(RedFlagGuid));
				SetObjectReference(
					paletteMenu, "blasterPickupPrefab", LoadMapObject(BlasterPickupGuid));
				SetObjectReference(
					paletteMenu, "automaticPickupPrefab", LoadMapObject(AutomaticPickupGuid));
				SetObjectReference(
					paletteMenu, "debugWallPrefab", LoadMapObject(DebugWallGuid));
				SetObjectReference(
					paletteMenu, "debugAnchorHandlePrefab", LoadMapObject(DebugAnchorHandleGuid));

				PrefabUtility.SaveAsPrefabAsset(prefabRoot, PalettePrefabPath);
				Debug.Log(
					$"Rebuilt '{PalettePrefabPath}' with a world-space UI Toolkit panel.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
		}

		private static void RemoveAllChildren(GameObject root)
		{
			for (int i = root.transform.childCount - 1; i >= 0; i--)
				UnityEngine.Object.DestroyImmediate(
					root.transform.GetChild(i).gameObject);
		}

		private static void RemoveComponents(
			GameObject root, params string[] keepComponentNames)
		{
			// behaviours first so components they depend on (e.g. Canvas)
			// can be removed afterwards
			foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
			{
				if (behaviour == null)
					continue;

				if (Array.IndexOf(keepComponentNames, behaviour.GetType().Name) != -1)
					continue;

				UnityEngine.Object.DestroyImmediate(behaviour);
			}

			foreach (Component component in root.GetComponents<Component>())
			{
				if (component == null ||
					component is Transform ||
					component is MonoBehaviour)
					continue;

				UnityEngine.Object.DestroyImmediate(component);
			}
		}

		private static void ConfigureDocument(
			GameObject host,
			string visualTreePath,
			PanelSettings panelSettings,
			Vector2 documentSize)
		{
			VisualTreeAsset visualTree =
				AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(visualTreePath);
			if (visualTree == null)
				throw new InvalidOperationException(
					$"Could not load UI document at '{visualTreePath}'.");

			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = panelSettings;
			document.visualTreeAsset = visualTree;
			document.position = Position.Relative;
			document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
			document.worldSpaceSize = documentSize;
			document.pivotReferenceSize = PivotReferenceSize.Layout;
			document.pivot = Pivot.Center;
		}

		private static T CreateChild<T>(Transform parent, string name)
			where T : MonoBehaviour
		{
			GameObject child = new(name)
			{
				layer = GetUILayer()
			};

			child.transform.SetParent(parent, false);
			return child.AddComponent<T>();
		}

		private static void CreateScoreLabel(
			Transform parent, string name, byte team, string labelName)
		{
			ScoreLabel scoreLabel = CreateChild<ScoreLabel>(parent, name);

			SerializedObject serializedLabel = new(scoreLabel);
			serializedLabel.FindProperty("team").intValue = team;
			serializedLabel.FindProperty("labelName").stringValue = labelName;
			serializedLabel.ApplyModifiedPropertiesWithoutUndo();
		}

		private static MapObject LoadMapObject(string guid)
		{
			string assetPath = AssetDatabase.GUIDToAssetPath(guid);
			if (string.IsNullOrEmpty(assetPath))
				throw new InvalidOperationException(
					$"Could not resolve map object prefab with GUID '{guid}'.");

			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
			MapObject mapObject = prefab == null
				? null
				: prefab.GetComponentInChildren<MapObject>(true);
			if (mapObject == null)
				throw new InvalidOperationException(
					$"'{assetPath}' does not contain a MapObject component.");

			return mapObject;
		}

		private static void SetObjectReference(
			MonoBehaviour component,
			string propertyName,
			UnityEngine.Object value)
		{
			if (value == null)
				throw new InvalidOperationException(
					$"Required asset for '{propertyName}' could not be loaded.");

			SerializedObject serializedObject = new(component);
			SerializedProperty property = serializedObject.FindProperty(propertyName);
			if (property == null)
				throw new InvalidOperationException(
					$"Serialized property '{propertyName}' was not found on {component.GetType().Name}.");

			property.objectReferenceValue = value;
			serializedObject.ApplyModifiedPropertiesWithoutUndo();
		}

		private static int GetUILayer()
		{
			int layer = LayerMask.NameToLayer("UI");
			return layer >= 0 ? layer : 5;
		}
	}
}
