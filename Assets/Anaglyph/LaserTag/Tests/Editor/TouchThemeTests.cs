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
	}

	public sealed class TouchThemeTestWindow : EditorWindow { }
}
