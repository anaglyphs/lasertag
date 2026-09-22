#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Anaglyph.Menu;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Tests
{
	public class NavViewTests
	{
		private GameObject documentObject;
		private PanelSettings settings;
		private RenderTexture texture;
		private NavView nav;
		private NavPage home, details, modal, warning;
		private Button homeButton;
		private Toggle homeToggle;
		private VisualElement root;

		[UnitySetUp]
		public IEnumerator SetUp()
		{
			settings = ScriptableObject.CreateInstance<PanelSettings>();
			settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
				"Assets/Anaglyph/LaserTag/Interface/LaserTagRuntimeTheme.tss");
			texture = new RenderTexture(640, 480, 0);
			settings.targetTexture = texture;
			documentObject = new GameObject("NavView test") { hideFlags = HideFlags.HideAndDontSave };
			var document = documentObject.AddComponent<UIDocument>();
			document.panelSettings = settings;
			root = document.rootVisualElement;
			root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
				"Assets/Anaglyph/LaserTag/Interface/LaserTagStyle.uss"));
			nav = new NavView { FirstPageName = "home", TransitionDurationMs = 1000 };
			nav.style.width = 500;
			nav.style.height = 300;
			nav.style.borderLeftWidth = 2;
			nav.style.borderTopWidth = 2;
			nav.style.paddingLeft = 4;
			nav.style.paddingTop = 4;
			home = new NavPage { name = "home" };
			details = new NavPage { name = "details" };
			modal = new NavPage { name = "modal", ModalUserDismissible = true };
			warning = new NavPage { name = "warning" };
			homeButton = new Button { text = "Open" };
			homeToggle = new Toggle("Toggle");
			home.Add(homeButton);
			home.Add(homeToggle);
			details.Add(new Button { text = "Details" });
			modal.Add(new Button { text = "Modal" });
			warning.Add(new Button { text = "Warning" });
			nav.Add(home);
			nav.Add(details);
			nav.Add(modal);
			nav.Add(warning);
			root.Add(nav);
			yield return Layout();
			Assert.That(nav.panel.contextType, Is.EqualTo(ContextType.Player));
			Assert.That(home.layout.width, Is.GreaterThan(0));
		}

		[TearDown]
		public void TearDown()
		{
			Object.DestroyImmediate(documentObject);
			Object.DestroyImmediate(settings);
			Object.DestroyImmediate(texture);
		}

		[UnityTest]
		public IEnumerator ForwardAndBackSlideInOppositeDirectionsWithImmediateEvents()
		{
			var events = new List<string>();
			home.NavigatingAway += () => events.Add("away");
			details.NavigatingHere += () => events.Add("here");
			nav.Changed += _ => events.Add("changed");
			Rect homeBounds = home.layout;
			nav.GoToPage(details);
			Assert.That(events, Is.EqualTo(new[] { "away", "here", "changed" }));
			Assert.That(nav.CurrentPage, Is.SameAs(details));
			Assert.That(nav.History, Is.EqualTo(new[] { home, details }));
			Assert.That(details.style.translate.value.x.value, Is.EqualTo(homeBounds.width));
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			yield return Layout();
			Assert.That(home.layout.x, Is.EqualTo(homeBounds.x).Within(1));
			Assert.That(home.layout.y, Is.EqualTo(homeBounds.y).Within(1));
			Assert.That(home.layout.width, Is.EqualTo(homeBounds.width).Within(1));
			Assert.That(home.layout.height, Is.EqualTo(homeBounds.height).Within(1));
			Assert.That(details.layout.size, Is.EqualTo(homeBounds.size));
			Assert.That(home.resolvedStyle.translate.x, Is.LessThan(0));
			yield return Settled();
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(details.resolvedStyle.translate, Is.EqualTo(Vector3.zero));
			bool back = false;
			details.NavigatingBack += () => back = true;
			nav.GoBack();
			Assert.That(back, Is.True);
			Assert.That(home.style.translate.value.x.value, Is.LessThan(0));
			yield return Layout();
			Assert.That(details.resolvedStyle.translate.x, Is.GreaterThan(0));
			yield return Settled();
			Assert.That(nav.CurrentPage, Is.SameAs(home));
			Assert.That(nav.History.Count, Is.EqualTo(1));
		}

		[UnityTest]
		public IEnumerator RepeatedPointerPressesNavigateForwardAndBackOnFirstClick()
		{
			nav.TransitionDurationMs = 20;
			var forward = new NavButton { text = "Next", TargetPageName = "details" };
			var back = new NavButton { text = "Back" };
			home.Add(forward);
			details.Add(back);
			yield return Layout();

			for (int visit = 0; visit < 3; visit++)
			{
				Press(forward);
				Assert.That(nav.CurrentPage, Is.SameAs(details), $"Forward press on visit {visit}");
				yield return Settled();
				Press(back);
				Assert.That(nav.CurrentPage, Is.SameAs(home), $"Back press on visit {visit}");
				yield return Settled();
			}
		}

		[UnityTest]
		public IEnumerator MenuStylesheetDurationOverridesFallbackAndClearsWithStyle()
		{
			nav.TransitionDurationMs = 0;
			root.AddToClassList("menu-root");
			yield return Layout();
			nav.GoToPage(details);
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			yield return Settled();

			root.RemoveFromClassList("menu-root");
			yield return Layout();
			nav.GoBack();
			Assert.That(details.style.display.value, Is.EqualTo(DisplayStyle.None));
		}

		[UnityTest]
		public IEnumerator ModalsSlideVerticallyAndRespectPriorityAndReturnPage()
		{
			nav.PresentModal(modal, 10, details);
			Assert.That(modal.style.translate.value.y.value, Is.EqualTo(home.layout.height));
			Assert.That(home.style.translate.value, Is.EqualTo(new Translate(0, 0)));
			nav.PresentModal(warning, 0);
			Assert.That(nav.CurrentPage, Is.SameAs(modal));
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			yield return Layout();
			Assert.That(modal.resolvedStyle.backgroundColor.a, Is.EqualTo(1));
			float offset = modal.style.translate.value.y.value;
			nav.PresentModal(modal, 10, details);
			Assert.That(modal.style.translate.value.y.value, Is.EqualTo(offset));
			nav.DismissModal(warning);
			Assert.That(nav.CurrentPage, Is.SameAs(modal));
			yield return Settled();
			nav.GoBack();
			Assert.That(nav.CurrentPage, Is.SameAs(details));
			Assert.That(details.style.translate.value, Is.EqualTo(new Translate(0, 0)));
			Assert.That(nav.hierarchy[nav.hierarchy.childCount - 1], Is.SameAs(modal));
			yield return Layout();
			Assert.That(modal.resolvedStyle.translate.y, Is.GreaterThan(0));
			yield return Settled();
			Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(nav.hierarchy[2], Is.SameAs(modal));
			Assert.That(nav.History, Is.EqualTo(new[] { home, details }));
		}

		[UnityTest]
		public IEnumerator OutgoingInputIsBlockedWithoutDisablingControlsOrDataEvents()
		{
			var disabled = new Button();
			disabled.SetEnabled(false);
			home.Add(disabled);
			int inputs = 0, changes = 0, incomingInputs = 0;
			homeButton.RegisterCallback<PointerDownEvent>(_ => inputs++);
			homeButton.RegisterCallback<ClickEvent>(_ => inputs++);
			homeButton.RegisterCallback<KeyDownEvent>(_ => inputs++);
			homeButton.RegisterCallback<NavigationSubmitEvent>(_ => inputs++);
			homeButton.RegisterCallback<WheelEvent>(_ => inputs++);
			homeToggle.RegisterValueChangedCallback(_ => changes++);
			details.RegisterCallback<NavigationSubmitEvent>(_ => incomingInputs++);
			homeButton.Focus();
			homeButton.CapturePointer(PointerId.mousePointerId);
			nav.GoToPage(details);
			Assert.That(homeButton.enabledInHierarchy, Is.True);
			Assert.That(homeToggle.enabledInHierarchy, Is.True);
			Assert.That(disabled.enabledSelf, Is.False);
			Assert.That(homeButton.ClassListContains(VisualElement.disabledUssClassName), Is.False);
			Assert.That(homeButton.focusable, Is.False);
			Assert.That(nav.panel.GetCapturingElement(PointerId.mousePointerId), Is.Null);
			Send<PointerDownEvent>(homeButton);
			Send<ClickEvent>(homeButton);
			Send<KeyDownEvent>(homeButton);
			Send<NavigationSubmitEvent>(homeButton);
			Send<WheelEvent>(homeButton);
			Send<NavigationSubmitEvent>(details);
			homeToggle.value = true;
			Assert.That(inputs, Is.Zero);
			Assert.That(incomingInputs, Is.EqualTo(1));
			Assert.That(changes, Is.EqualTo(1));
			yield return Layout();
			Assert.That(nav.focusController.focusedElement, Is.SameAs(details));
			var ring = new VisualElementFocusRing(nav);
			var nextFocus = ring.GetNextFocusable(details, VisualElementFocusChangeDirection.right);
			Assert.That(details.Contains((VisualElement)nextFocus), Is.True, nextFocus?.ToString());
			yield return Settled();
			Assert.That(homeButton.focusable, Is.True);
			Assert.That(disabled.enabledSelf, Is.False);
		}

		[UnityTest]
		public IEnumerator RapidNavigationAndReentrantCallbacksKeepNewestPage()
		{
			nav.GoToPage(details);
			nav.GoBack();
			nav.PresentModal(modal);
			nav.PresentModal(warning, 20);
			nav.DismissModal(modal);
			nav.GoBack();
			Assert.That(nav.CurrentPage, Is.SameAs(warning));
			yield return Settled();
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(details.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(modal.style.display.value, Is.EqualTo(DisplayStyle.None));
			nav.DismissModal(warning);
			yield return Settled();
			details.NavigatingHere += () => nav.GoToPage(home);
			var changes = new List<NavPage>();
			nav.Changed += changes.Add;
			nav.GoToPage(details);
			Assert.That(nav.CurrentPage, Is.SameAs(home));
			Assert.That(changes, Is.EqualTo(new[] { home }));
			yield return Settled();
			Assert.That(details.style.display.value, Is.EqualTo(DisplayStyle.None));
		}

		[UnityTest]
		public IEnumerator DetachingRestoresPresentationAndFocusAndCancelsScheduledWork()
		{
			home.style.left = 7;
			home.style.translate = new Translate(3, 4);
			nav.style.minHeight = 100;
			yield return Layout();
			nav.GoToPage(details);
			nav.RemoveFromHierarchy();
			Assert.That(home.style.position.keyword, Is.EqualTo(StyleKeyword.Null));
			Assert.That(home.style.left.value.value, Is.EqualTo(7));
			Assert.That(home.style.translate.value, Is.EqualTo(new Translate(3, 4)));
			Assert.That(nav.style.minHeight.value.value, Is.EqualTo(100));
			Assert.That(nav.style.maxHeight.keyword, Is.EqualTo(StyleKeyword.Null));
			Assert.That(homeButton.focusable, Is.True);
			Assert.That(nav.CurrentPage, Is.Null);
			root.Add(nav);
			yield return Settled();
			Assert.That(nav.CurrentPage, Is.SameAs(home));
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(details.style.display.value, Is.EqualTo(DisplayStyle.None));
		}

		[UnityTest]
		public IEnumerator ModalPresentationPreservesNestedNavigation()
		{
			var nested = new NavView { FirstPageName = "one" };
			var one = new NavPage { name = "one" };
			var two = new NavPage { name = "two" };
			nested.Add(one);
			nested.Add(two);
			home.Add(nested);
			yield return Layout();
			nested.GoToPage(two);
			nav.PresentModal(modal);
			yield return Settled();
			nav.DismissModal(modal);
			yield return Settled();
			Assert.That(nested.CurrentPage, Is.SameAs(two));
			Assert.That(nested.History, Is.EqualTo(new[] { one, two }));
			Assert.That(two.ParentView, Is.SameAs(nested));
		}

		[UnityTest]
		public IEnumerator ZeroDurationAndHiddenViewNavigateImmediately()
		{
			Assert.That(home.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
			nav.TransitionDurationMs = 0;
			nav.GoToPage(details);
			Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(details.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
			nav.style.display = DisplayStyle.None;
			yield return Layout();
			nav.TransitionDurationMs = 220;
			nav.GoBack();
			Assert.That(details.style.display.value, Is.EqualTo(DisplayStyle.None));
			Assert.That(home.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
		}

		[UnityTest]
		public IEnumerator TransitionCompletesWhileTimeScaleIsZero()
		{
			float previousScale = Time.timeScale;
			try
			{
				Time.timeScale = 0;
				nav.GoToPage(details);
				yield return Settled();
				Assert.That(home.style.display.value, Is.EqualTo(DisplayStyle.None));
				Assert.That(details.resolvedStyle.translate, Is.EqualTo(Vector3.zero));
			}
			finally { Time.timeScale = previousScale; }
		}

		[UnityTest]
		public IEnumerator GameMenuKeepsItsViewportAndUsesAnOpaqueModalSurface()
		{
			root.Clear();
			var game = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml").CloneTree();
			game.style.flexGrow = 1;
			root.Add(game);
			nav = game.Q<NavView>();
			nav.TransitionDurationMs = 1000;
			yield return Layout();
			Vector2 viewport = nav.layout.size;
			var panel = game.Q("game-menu-panel");
			var tabs = game.Q("game-menu-tabs");
			Assert.That(panel.layout.width, Is.EqualTo(400).Within(.5f));
			Assert.That(panel.layout.height, Is.EqualTo(400).Within(.5f));
			Assert.That(tabs.worldBound.yMin, Is.EqualTo(panel.worldBound.yMax).Within(.1f));
			foreach (var tabName in new[] { "maps-tab", "match-tab" })
			{
				var tab = game.Q<Button>(tabName);
				var picked = root.panel.Pick(tab.worldBound.center);
				Assert.That(picked == tab || tab.Contains(picked), Is.True, tabName);
			}
			var first = nav.CurrentPage;
			var maps = game.Q<NavView>("maps-nav");
			var match = game.Q<NavView>("match-nav");
			maps.GoToPage("space-details");
			maps.AddToClassList("game-menu-hidden");
			match.RemoveFromClassList("game-menu-hidden");
			yield return Layout();
			Assert.That(nav.layout.size, Is.EqualTo(viewport));
			var error = nav.GetPage("error-modal");
			nav.PresentModal(error);
			yield return Layout();
			Assert.That(error.resolvedStyle.backgroundColor, Is.EqualTo((Color)new Color32(12, 0, 49, 255)));
			Assert.That(error.resolvedStyle.translate.y, Is.GreaterThan(0));
			yield return Settled();
			nav.DismissModal(error);
			yield return Settled();
			Assert.That(nav.CurrentPage, Is.SameAs(first));
			Assert.That(maps.CurrentPage.name, Is.EqualTo("space-details"));
			Assert.That(match.CurrentPage.name, Is.EqualTo("match-page"));
		}

		private static void Press(Button button)
		{
			using (var down = PointerDownEvent.GetPooled(new Event
				{ type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center }))
			{
				down.target = button;
				button.SendEvent(down);
			}
			using (var up = PointerUpEvent.GetPooled(new Event
				{ type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center }))
			{
				up.target = button;
				button.SendEvent(up);
			}
		}

		private static void Send<T>(VisualElement target) where T : EventBase<T>, new()
		{
			using T evt = EventBase<T>.GetPooled();
			evt.target = target;
			target.SendEvent(evt);
		}

		private IEnumerator Layout()
		{
			for (int i = 0; i < 3; i++)
			{
				EditorApplication.QueuePlayerLoopUpdate();
				root.panel?.Pick(Vector2.zero);
				yield return null;
			}
		}

		private IEnumerator Settled()
		{
			double until = Time.realtimeSinceStartupAsDouble + nav.TransitionDurationMs / 1000f + 3;
			while (HasOutgoingPage() && Time.realtimeSinceStartupAsDouble < until)
			{
				EditorApplication.QueuePlayerLoopUpdate();
				yield return null;
			}
			yield return Layout();
			Assert.That(HasOutgoingPage(), Is.False, "Transition did not finish.");
		}

		private bool HasOutgoingPage()
		{
			foreach (VisualElement child in nav.Children())
				if (child is NavPage page && page != nav.CurrentPage && page.style.display.value == DisplayStyle.Flex)
					return true;
			return false;
		}
	}
}
#endif
