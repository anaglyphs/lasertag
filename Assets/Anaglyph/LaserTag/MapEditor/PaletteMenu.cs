using Anaglyph.Menu.UIToolkit;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.Lasertag
{
	[RequireComponent(typeof(UIDocument))]
	public class PaletteMenu : MonoBehaviour
	{
		[SerializeField] private MapObject blueBasePrefab;
		[SerializeField] private MapObject redBasePrefab;
		[SerializeField] private MapObject blueFlagPrefab;
		[SerializeField] private MapObject redFlagPrefab;
		[SerializeField] private MapObject blasterPickupPrefab;
		[SerializeField] private MapObject automaticPickupPrefab;
		[SerializeField] private MapObject debugWallPrefab;
		[SerializeField] private MapObject debugAnchorHandlePrefab;

		private UIToolkitNavPages navView;
		private UIToolkitNavPage homePage;
		private UIToolkitNavPage debugPage;
		private Button debugObjectsButton;

		private void OnEnable()
		{
			UIDocument document = GetComponent<UIDocument>();
			VisualElement root = document?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"PaletteMenu requires an enabled UIDocument with a visual tree.");

			// must happen before anything subscribes to Button.clicked
			root.MakeButtonsActOnPress();

			navView = new UIToolkitNavPages(Require<VisualElement>(root, "pages"));
			homePage = navView.AddPage("home-page", false);
			debugPage = navView.AddPage("debug-page");

			BindSpawnButton(root, "nothing-button", null);
			BindSpawnButton(root, "blue-base-button", blueBasePrefab);
			BindSpawnButton(root, "red-base-button", redBasePrefab);
			BindSpawnButton(root, "blue-flag-button", blueFlagPrefab);
			BindSpawnButton(root, "red-flag-button", redFlagPrefab);
			BindSpawnButton(root, "blaster-pickup-button", blasterPickupPrefab);
			BindSpawnButton(root, "automatic-pickup-button", automaticPickupPrefab);
			BindSpawnButton(root, "debug-wall-button", debugWallPrefab);
			BindSpawnButton(root, "debug-anchor-button", debugAnchorHandlePrefab);

			debugObjectsButton = Require<Button>(root, "debug-objects-button");
			debugObjectsButton.clicked += debugPage.NavigateHere;

			AnaglyphDebugging.DebugModeChanged += OnDebugModeChanged;
			OnDebugModeChanged(AnaglyphDebugging.DebugMode);

			navView.Start(homePage);
		}

		private void OnDisable()
		{
			AnaglyphDebugging.DebugModeChanged -= OnDebugModeChanged;
			navView?.Dispose();
			navView = null;
		}

		private void OnDebugModeChanged(bool debugMode)
		{
			debugObjectsButton.style.display =
				debugMode ? DisplayStyle.Flex : DisplayStyle.None;
		}

		private void BindSpawnButton(VisualElement root, string buttonName, MapObject mapObjPrefab)
		{
			Require<Button>(root, buttonName).clicked += () => SetSpawnObject(mapObjPrefab);
		}

		private static void SetSpawnObject(MapObject mapObjPrefab)
		{
			MapEditorTool[] tools = FindObjectsByType<MapEditorTool>(
				FindObjectsInactive.Include, FindObjectsSortMode.None);

			foreach (MapEditorTool tool in tools)
				tool.SetSpawnObject(mapObjPrefab);
		}

		private static T Require<T>(VisualElement root, string name)
			where T : VisualElement
		{
			T element = root.Q<T>(name);
			if (element == null)
				throw new InvalidOperationException(
					$"Required UI Toolkit element '{name}' ({typeof(T).Name}) was not found.");

			return element;
		}
	}
}
