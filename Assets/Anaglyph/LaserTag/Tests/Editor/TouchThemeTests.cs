using System.Collections;
using Anaglyph.LaserTag.Interface;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Tests
{
	public class TouchThemeTests
	{
		private const string SettingsHomePath = "Assets/Anaglyph/LaserTag/Interface/Main Menu/Settings/SettingsHomePage.uxml";
		private static readonly string[] XROnlyElementNames = { "dominant-eye-row", "dominant-hand-row" };
		private const string PalettePath = "Assets/Anaglyph/LaserTag/MapEditor/MapEditorPalette.uxml";
		private static readonly string[] PaletteLists = { "category-rail", "object-grid" };

		private TouchThemeTestWindow window;

		[SetUp]
		public void SetUp()
		{
			window = ScriptableObject.CreateInstance<TouchThemeTestWindow>();
			window.position = new Rect(100, 100, 800, 600);
			window.Show();
		}

		[TearDown]
		public void TearDown() => window.Close();

		private VisualElement LoadSettingsHomeWithTheme(ThemeStyleSheet theme)
		{
			window.rootVisualElement.styleSheets.Add(theme);
			VisualElement root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SettingsHomePath).CloneTree();
			window.rootVisualElement.Add(root);
			return root;
		}

		[UnityTest]
		public IEnumerator TouchThemeHidesXROnlyControls()
		{
			ThemeStyleSheet touchTheme = Resources.Load<ThemeStyleSheet>(HandheldScreenUI.TouchThemeResource);
			Assert.That(touchTheme, Is.Not.Null);
			VisualElement root = LoadSettingsHomeWithTheme(touchTheme);
			yield return null;
			foreach (string name in XROnlyElementNames)
				Assert.That(root.Q(name).resolvedStyle.display, Is.EqualTo(DisplayStyle.None), name);
		}

		[UnityTest]
		public IEnumerator RuntimeThemeShowsXROnlyControls()
		{
			VisualElement root = LoadSettingsHomeWithTheme(AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss"));
			yield return null;
			foreach (string name in XROnlyElementNames)
				Assert.That(root.Q(name).resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), name);
		}

		[UnityTest]
		public IEnumerator PaletteTurnsItsListsIntoRowsOnTouchScreens()
		{
			VisualElement root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PalettePath).CloneTree();
			window.rootVisualElement.Add(root);
			root.Q<Anaglyph.Menu.NavView>().GoToPage("objects-page");
			yield return null;
			foreach (string list in PaletteLists)
				Assert.That(root.Q<ScrollView>(list).mode, Is.EqualTo(ScrollViewMode.Vertical), list);
			Assert.That(root.Q("delete-object-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

			root.AddToClassList(HandheldScreenUI.TouchScreenUssClassName);
			yield return null;
			foreach (string list in PaletteLists)
				Assert.That(root.Q<ScrollView>(list).mode, Is.EqualTo(ScrollViewMode.Horizontal), list);
			Assert.That(root.Q("delete-object-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
		}

		[UnityTest]
		public IEnumerator PaletteIsATwoByTwoGridWithoutDoneExceptOnTouchScreens()
		{
			window.rootVisualElement.styleSheets.Add(
				AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>("Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss"));
			VisualElement root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PalettePath).CloneTree();
			root.style.width = 420;
			root.style.height = 500;
			window.rootVisualElement.Add(root);
			root.Q<Anaglyph.Menu.NavView>().GoToPage("objects-page");
			ScrollView grid = root.Q<ScrollView>("object-grid");
			Button[] items = new Button[4];
			for (int i = 0; i < items.Length; i++)
			{
				items[i] = new Button();
				items[i].AddToClassList("palette-item");
				VisualElement icon = new();
				icon.AddToClassList("palette-item-icon");
				items[i].Add(icon);
				Label label = new("Blue base");
				label.AddToClassList("palette-item-label");
				items[i].Add(label);
				grid.Add(items[i]);
			}
			VisualElement actions = root.Q("palette-actions");
			actions.AddToClassList("palette-actions--placing-objects");
			for (int frame = 0; frame < 5; frame++) yield return null;

			Assert.That(items[1].worldBound.y, Is.EqualTo(items[0].worldBound.y).Within(.5f));
			Assert.That(items[1].worldBound.x, Is.GreaterThan(items[0].worldBound.xMax));
			Assert.That(items[2].worldBound.y, Is.GreaterThanOrEqualTo(items[0].worldBound.yMax));
			Assert.That(items[2].worldBound.x, Is.EqualTo(items[0].worldBound.x).Within(.5f));
			Assert.That(items[3].worldBound.y, Is.EqualTo(items[2].worldBound.y).Within(.5f));
			Assert.That(items[3].worldBound.yMax, Is.LessThanOrEqualTo(grid.worldBound.yMax + .5f), "All four fit without scrolling.");
			Assert.That(items[3].worldBound.xMax, Is.LessThanOrEqualTo(grid.worldBound.xMax + .5f));
			Assert.That(root.Q("done-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
			VisualElement panel = root.Q<Anaglyph.Menu.NavPage>("objects-page").Q(className: "palette-root");
			Assert.That(panel.worldBound.yMax - items[3].worldBound.yMax, Is.LessThan(30), "No empty strip under the grid.");

			actions.RemoveFromClassList("palette-actions--placing-objects");
			yield return null;
			Assert.That(root.Q("done-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "Tag tools keep Done.");

			actions.AddToClassList("palette-actions--placing-objects");
			root.AddToClassList(HandheldScreenUI.TouchScreenUssClassName);
			yield return null;
			Assert.That(root.Q("done-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

			// a portrait phone: all four in one row, none past the screen's edge
			root.style.width = 359;
			root.style.height = StyleKeyword.Auto;
			for (int frame = 0; frame < 5; frame++) yield return null;
			foreach (Button item in items)
				Assert.That(item.worldBound.y, Is.EqualTo(items[0].worldBound.y).Within(.5f));
			Assert.That(items[0].worldBound.xMin, Is.GreaterThanOrEqualTo(grid.worldBound.xMin - .5f));
			Assert.That(items[3].worldBound.xMax, Is.LessThanOrEqualTo(grid.worldBound.xMax + .5f));
		}
	}

	public sealed class TouchThemeTestWindow : EditorWindow { }
}
