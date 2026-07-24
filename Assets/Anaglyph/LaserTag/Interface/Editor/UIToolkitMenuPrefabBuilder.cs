using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag.Editor
{
	internal static class UIToolkitMenuPrefabBuilder
	{
		// This migrates the live menu prefab once, and remains available for explicit rebuilds.
		private const string MenuPrefabPath =
			"Assets/Anaglyph/LaserTag/Interface/Menu/Menu.prefab";
		private const string UIToolkitDirectory =
			"Assets/Anaglyph/LaserTag/Interface/Menu/UI Toolkit";
		private const string PanelSettingsPath =
			UIToolkitDirectory + "/LaserTagWorldSpacePanelSettings.asset";
		private const string RuntimeThemePath =
			UIToolkitDirectory + "/LaserTagRuntimeTheme.tss";
		private const string StyleSheetPath =
			UIToolkitDirectory + "/LaserTagMenu.uss";
		private const string MultiplayerDocumentPath =
			UIToolkitDirectory + "/MultiplayerMenu.uxml";
		private const string GameDocumentPath =
			UIToolkitDirectory + "/GameMenu.uxml";
		private const string SettingsDocumentPath =
			UIToolkitDirectory + "/SettingsMenu.uxml";

		private const string RelaySettingPath =
			"Assets/Anaglyph/LaserTag/Settings/relay.asset";
		private const string AprilTagSettingPath =
			"Assets/Anaglyph/LaserTag/Settings/tag alignment.asset";
		private const string AprilTagSizeSettingPath =
			"Assets/Anaglyph/LaserTag/Settings/tag size.asset";
		private const string LightEffectsSettingPath =
			"Assets/Anaglyph/LaserTag/Settings/light effects.asset";
		private const string BoundarySettingPath =
			"Assets/Anaglyph/LaserTag/Settings/boundary.asset";
		private const string BuildNumberPath =
			"Assets/Anaglyph/BuildNumber.asset";

		private const int DocumentWidth = 400;
		private const int DocumentHeight = 500;
		private const float DocumentScale = 0.15f;
		private const float PanelRadius = 1.5f;
		private const float PanelAngleRadians = 0.4f;

		[InitializeOnLoadMethod]
		private static void ScheduleInitialBuild()
		{
			EditorApplication.delayCall += BuildIfNeeded;
		}

		[MenuItem("Tools/Anaglyph/Rebuild UI Toolkit Menu")]
		public static void Rebuild()
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				Debug.LogWarning("The UI Toolkit menu cannot be rebuilt in Play Mode.");
				return;
			}

			ImportUIAssets();
			PanelSettings panelSettings = GetOrCreatePanelSettings();

			GameObject prefabRoot = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
			try
			{
				UnityEngine.Object hostOnRelaySetting =
					ReadObjectReference(
						prefabRoot.GetComponentInChildren<MultiplayerMenu>(true),
						"hostOnRelaySetting") ??
					LoadObject(RelaySettingPath);
				UnityEngine.Object useAprilTagsSetting =
					ReadObjectReference(
						prefabRoot.GetComponentInChildren<MultiplayerMenu>(true),
						"useAprilTagsSetting") ??
					LoadObject(AprilTagSettingPath);
				UnityEngine.Object aprilTagSizeSetting =
					ReadObjectReference(
						prefabRoot.GetComponentInChildren<MultiplayerMenu>(true),
						"aprilTagSizeSetting") ??
					LoadObject(AprilTagSizeSettingPath);

				RemoveLegacyContent(prefabRoot);
				ConfigureInput(prefabRoot);

				SettingsMenu settingsMenu = CreatePanel<SettingsMenu>(
					prefabRoot.transform,
					"Settings Panel",
					SettingsDocumentPath,
					panelSettings,
					-PanelAngleRadians);
				MultiplayerMenu multiplayerMenu = CreatePanel<MultiplayerMenu>(
					prefabRoot.transform,
					"Connection Panel",
					MultiplayerDocumentPath,
					panelSettings,
					0f);
				CreatePanel<GameMenu>(
					prefabRoot.transform,
					"Game Panel",
					GameDocumentPath,
					panelSettings,
					PanelAngleRadians);

				SetObjectReference(multiplayerMenu, "hostOnRelaySetting", hostOnRelaySetting);
				SetObjectReference(multiplayerMenu, "useAprilTagsSetting", useAprilTagsSetting);
				SetObjectReference(multiplayerMenu, "aprilTagSizeSetting", aprilTagSizeSetting);
				SetObjectReference(multiplayerMenu, "buildNumber", LoadObject(BuildNumberPath));

				SetObjectReference(
					settingsMenu,
					"lightEffectsSetting",
					LoadObject(LightEffectsSettingPath));
				SetObjectReference(
					settingsMenu,
					"boundarySetting",
					LoadObject(BoundarySettingPath));
				SetObjectReference(settingsMenu, "buildNumber", LoadObject(BuildNumberPath));

				PrefabUtility.SaveAsPrefabAsset(prefabRoot, MenuPrefabPath);
				Debug.Log(
					$"Rebuilt '{MenuPrefabPath}' with three world-space UI Toolkit panels.");
			}
			finally
			{
				PrefabUtility.UnloadPrefabContents(prefabRoot);
			}
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

			GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
			if (prefab != null && prefab.GetComponent<UIToolkitMenuInputSetup>() == null)
				Rebuild();
		}

		private static void ImportUIAssets()
		{
			string[] paths =
			{
				RuntimeThemePath,
				StyleSheetPath,
				MultiplayerDocumentPath,
				GameDocumentPath,
				SettingsDocumentPath
			};

			foreach (string assetPath in paths)
				AssetDatabase.ImportAsset(
					assetPath,
					ImportAssetOptions.ForceSynchronousImport);
		}

		private static PanelSettings GetOrCreatePanelSettings()
		{
			PanelSettings panelSettings =
				AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			if (panelSettings == null)
			{
				panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
				panelSettings.name = "LaserTag World Space Panel Settings";
				AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
			}

			ThemeStyleSheet theme =
				AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(RuntimeThemePath);
			if (theme == null)
				throw new InvalidOperationException(
					$"Could not load runtime theme at '{RuntimeThemePath}'.");

			panelSettings.themeStyleSheet = theme;
			panelSettings.renderMode = PanelRenderMode.WorldSpace;
			panelSettings.scaleMode = PanelScaleMode.ConstantPhysicalSize;
			panelSettings.scale = 1f;

			SerializedObject serializedPanelSettings =
				new SerializedObject(panelSettings);
			SerializedProperty pixelsPerUnit =
				serializedPanelSettings.FindProperty("m_PixelsPerUnit");
			SerializedProperty colliderUpdateMode =
				serializedPanelSettings.FindProperty("m_ColliderUpdateMode");
			SerializedProperty colliderIsTrigger =
				serializedPanelSettings.FindProperty("m_ColliderIsTrigger");
			if (pixelsPerUnit == null ||
				colliderUpdateMode == null ||
				colliderIsTrigger == null)
			{
				throw new InvalidOperationException(
					"PanelSettings no longer exposes the required world-space fields.");
			}

			pixelsPerUnit.floatValue = 100f;
			colliderUpdateMode.intValue = 1; // Keep the explicitly configured collider.
			colliderIsTrigger.boolValue = true;
			serializedPanelSettings.ApplyModifiedPropertiesWithoutUndo();
			AssetDatabase.SaveAssetIfDirty(panelSettings);
			return panelSettings;
		}

		private static void RemoveLegacyContent(GameObject prefabRoot)
		{
			for (int i = prefabRoot.transform.childCount - 1; i >= 0; i--)
				UnityEngine.Object.DestroyImmediate(
					prefabRoot.transform.GetChild(i).gameObject);

			foreach (MonoBehaviour component in prefabRoot.GetComponents<MonoBehaviour>())
			{
				string componentName = component != null
					? component.GetType().Name
					: string.Empty;
				if (component == null ||
					component is UIToolkitMenuInputSetup ||
					componentName == "MenuToggle" ||
					componentName == "PanelInputConfiguration" ||
					componentName == "XRUIToolkitManager")
				{
					continue;
				}

				UnityEngine.Object.DestroyImmediate(component);
			}
		}

		private static void ConfigureInput(GameObject prefabRoot)
		{
			UIToolkitMenuInputSetup setup =
				prefabRoot.GetComponent<UIToolkitMenuInputSetup>();
			if (setup == null)
				setup = prefabRoot.AddComponent<UIToolkitMenuInputSetup>();

			PanelInputConfiguration input =
				setup.GetComponent<PanelInputConfiguration>();
			input.processWorldSpaceInput = true;
			input.interactionLayers = 1 << GetUILayer();
			input.maxInteractionDistance = float.PositiveInfinity;
			input.defaultEventCameraIsMainCamera = true;
			input.panelInputRedirection =
				PanelInputConfiguration.PanelInputRedirection.Never;
			input.autoCreatePanelComponents = true;
		}

		private static T CreatePanel<T>(
			Transform parent,
			string name,
			string visualTreePath,
			PanelSettings panelSettings,
			float angle)
			where T : MonoBehaviour
		{
			VisualTreeAsset visualTree =
				AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(visualTreePath);
			if (visualTree == null)
				throw new InvalidOperationException(
					$"Could not load UI document at '{visualTreePath}'.");

			GameObject panel = new(name)
			{
				layer = GetUILayer()
			};

			Transform panelTransform = panel.transform;
			panelTransform.SetParent(parent, false);

			Vector3 position = new(
				Mathf.Sin(angle) * PanelRadius,
				0f,
				Mathf.Cos(angle) * PanelRadius);
			panelTransform.localPosition = position;
			panelTransform.localRotation =
				Quaternion.LookRotation(position.normalized, Vector3.up);
			panelTransform.localScale = Vector3.one * DocumentScale;

			UIDocument document = panel.AddComponent<UIDocument>();
			document.panelSettings = panelSettings;
			document.visualTreeAsset = visualTree;
			document.position = Position.Relative;
			document.worldSpaceSizeMode = UIDocument.WorldSpaceSizeMode.Fixed;
			document.worldSpaceSize = new Vector2(DocumentWidth, DocumentHeight);
			document.pivotReferenceSize = PivotReferenceSize.Layout;
			document.pivot = Pivot.Center;

			UIToolkitPanelXRSetup xrSetup = panel.AddComponent<UIToolkitPanelXRSetup>();
			xrSetup.Configure();

			return panel.AddComponent<T>();
		}

		private static int GetUILayer()
		{
			int layer = LayerMask.NameToLayer("UI");
			return layer >= 0 ? layer : 5;
		}

		private static UnityEngine.Object ReadObjectReference(
			MonoBehaviour component,
			string propertyName)
		{
			if (component == null)
				return null;

			SerializedProperty property =
				new SerializedObject(component).FindProperty(propertyName);
			return property?.objectReferenceValue;
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

		private static UnityEngine.Object LoadObject(string assetPath)
		{
			return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
		}
	}
}
