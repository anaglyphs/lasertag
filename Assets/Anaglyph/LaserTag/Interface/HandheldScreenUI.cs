using Anaglyph.LaserTag.Interface.HUD;
using Anaglyph.LaserTag.MapEditor;
using Anaglyph.LaserTag.MapEditor.Tools;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// A phone's interface: the menu panels, HUD and score flat on the screen, one panel at a time
	/// chosen from a tab bar across the bottom, a square in the corner that shows and hides them, and a
	/// dot in the middle of the screen where the blaster fires. The panels and the bar fill the screen
	/// inside its safe area (clear of notches, rounded corners and the home indicator). One panel unit
	/// is one density-independent pixel, so the panels' 48-unit controls are a fingertip tall on any screen.
	/// Interfaces that follow controllers are put away. Camera mode, entered from a tab beside the panels'
	/// own, replaces all of it with its own screen. Editing a map's objects replaces all but the dot with
	/// the map editor's palette along the bottom of the screen. Standing in their own base, the player
	/// picks a weapon from a row like the palette's along the bottom, clear of the menu button.
	/// </summary>
	public sealed class HandheldScreenUI : MonoBehaviour
	{
		// Android's baseline density: a unit is a dp
		private const float UnitDpi = 160;
		// what a texture target (no screen to measure) is drawn at, like a 2x screen
		private const float TexturePixelsPerUnit = 2;
		private const float ControlSize = 48;
		private const float Margin = 8;
		private const float BarGap = 8;
		// Android's gesture navigation strip isn't part of its safe area: the bottom keeps at least this clear of it
		private const float GestureEdge = 16;
		// the blaster sight's look: a white dot in a team-colored halo
		private const float ReticleSize = 10;
		private const float ReticleCoreSize = 4;
		// hides elements tagged .xr-only
		public const string TouchThemeResource = "LaserTagTouchTheme";
		// on each document's root, so a style sheet can tell the screen from the headset
		public const string TouchScreenUssClassName = "touch-screen";
		private const string NeutralPanelUssClassName = "neutral-panel";

		private static readonly Color ToggleFill = new(20 / 255f, 20 / 255f, 20 / 255f);
		private static readonly Color ToggleEdge = new(200 / 255f, 200 / 255f, 200 / 255f);

		private MainMenuController menu;
		private PanelSettings screenPanel;

		/// <summary>The panel every screen document shares; a test may point it at a texture.</summary>
		public PanelSettings ScreenPanel => screenPanel;
		public MainMenuController Menu => menu;

		/// <summary>A test's safe area, in the pixels of the texture the panel draws into, as a notched phone reports it.</summary>
		public Rect? SafeAreaForTests { get; set; }

		/// <summary>A test's screen density for a texture target, pixels per unit, as Screen.dpi / 160 gives on a device.</summary>
		public float? PixelsPerUnitForTests { get; set; }

		private UIDocument overlay;
		private VisualElement tabBar, toggle, reticle;
		private Button[] tabs;
		private Button cameraTab;
		private CameraMode cameraMode;
		private CameraModeScreen cameraScreen;
		private HandheldWeaponPicker weaponPicker;
		private readonly System.Collections.Generic.List<VisualElement> scoreRoots = new();
		private UIDocument[] menuDocuments;
		private UIDocument gateDocument, hudDocument, editorDocument;
		private UIToolkitPanelXRSetup editorPanel;
		private VisualElement placedEditorRoot;
		private bool wasEditingObjects;
		private Rect appliedSafeArea;
		private Vector2 appliedPanelSize;
		private RenderTexture appliedTarget;
		private ScreenOrientation appliedOrientation;

		private void Start()
		{
			// the blaster is the phone itself: the hands follow its camera, and a tap fires
			Camera camera = Anaglyph.XR.MainXRRig.Camera;
			if (camera != null) Anaglyph.XR.Input.HandInput.FollowCamera(camera.transform);
			else Debug.LogError($"[{nameof(HandheldScreenUI)}] no camera for the hands to follow: taps will not fire");
			Anaglyph.Menu.ActOnPress.ActsOnRelease = true;

			menu = GetComponentInChildren<MainMenuController>(true);
			if (menu == null)
			{
				Debug.LogError($"[{nameof(HandheldScreenUI)}] no main menu to put on the screen");
				enabled = false;
				return;
			}

			gateDocument = menu.Gate.GetComponent<UIDocument>();
			menuDocuments = System.Array.ConvertAll(menu.Panels, panel => panel.GetComponent<UIDocument>());
			screenPanel = CreateScreenPanel(gateDocument.panelSettings);

			MoveToScreen(gateDocument);
			foreach (UIDocument document in menuDocuments) MoveToScreen(document);

			HUD.HUD hud = GetComponentInChildren<HUD.HUD>(true);
			if (hud != null)
			{
				hudDocument = hud.GetComponent<UIDocument>();
				MoveToScreen(hudDocument);
			}

			cameraMode = GetComponent<CameraMode>();
			BuildOverlay();
			menu.ShowOnScreen(DefaultPanel());
			MapEditor.MapEditor.ActiveChanged += HandleMapEditorActiveChanged;
			MapEditorTool.ModeChanged += HandleMapEditorModeChanged;
			ShowMenuUnlessEditingObjects();
			Anaglyph.MenuCopy.Changed += LabelTabs;
			LabelTabs();
			PutAwayControllerInterfaces();
			weaponPicker = new HandheldWeaponPicker(overlay.rootVisualElement,
				editorDocument != null && editorDocument.visualTreeAsset != null
					? editorDocument.visualTreeAsset.stylesheets
					: System.Array.Empty<StyleSheet>());
		}

		// a back swipe: rightward, mostly level, and not one that drags a slider, scrollbar or text.
		// Over a page that can go back, the page follows the finger; let go past a third of the way, or
		// flicked, it goes back. Elsewhere (a modal, a panel's first page) a quick swipe does the same as back
		private const float SwipeBackUnits = 70, SwipeBackSeconds = 0.6f, SwipeBackSlope = 0.6f;
		private const float DragDecideUnits = 10, DragCommitFraction = 0.35f, DragCommitSpeed = 500;
		private Vector2? swipeStart;
		private float swipeStartTime;
		private bool swipeStartedOnDraggable, swipeDecided;
		private Anaglyph.Menu.NavView draggingBack;

		private void Update()
		{
			if (menu == null) return;

			// Android's back button and back gesture arrive as Escape; iOS has no system back
			if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
			{
				if (IsFilming) cameraScreen.Back();
				else if (IsEditorShown) MapEditor.MapEditor.SetActive(false);
				else Back();
			}

			if (IsFilming) return;

			Touchscreen touchscreen = Touchscreen.current;
			if (touchscreen == null) return;
			TouchControl touch = touchscreen.primaryTouch;
			if (touch.press.wasPressedThisFrame)
			{
				swipeStart = touch.position.ReadValue();
				swipeStartTime = Time.unscaledTime;
				swipeStartedOnDraggable = IsDraggable(Anaglyph.XR.Input.ScreenSpaceUIHit.PickedUnderPrimaryTouch());
				swipeDecided = false;
				return;
			}

			if (!swipeStart.HasValue) return;
			Vector2 moved = (touch.position.ReadValue() - swipeStart.Value) / PixelsPerUnit;

			if (touch.press.isPressed)
			{
				if (draggingBack != null) draggingBack.UpdateDragBack(moved.x / draggingBack.DragBackWidth);
				else if (!swipeDecided && menu.IsVisible && !swipeStartedOnDraggable)
				{
					if (Mathf.Abs(moved.y) >= DragDecideUnits && Mathf.Abs(moved.y) > moved.x * SwipeBackSlope) swipeDecided = true;
					else if (moved.x >= DragDecideUnits)
					{
						swipeDecided = true;
						Anaglyph.Menu.NavView view = Anaglyph.Menu.NavView.InnermostThatCanGoBack(ShownMenuDocument.rootVisualElement);
						if (view != null && view.CanDragBack && view.BeginDragBack()) draggingBack = view;
					}
				}

				return;
			}

			float seconds = Mathf.Max(Time.unscaledTime - swipeStartTime, 0.01f);
			swipeStart = null;
			if (draggingBack != null)
			{
				bool commit = moved.x >= draggingBack.DragBackWidth * DragCommitFraction || moved.x / seconds >= DragCommitSpeed;
				draggingBack.EndDragBack(commit);
				draggingBack = null;
				return;
			}

			bool swipedBack = !swipeStartedOnDraggable && seconds <= SwipeBackSeconds &&
			                  moved.x >= SwipeBackUnits && Mathf.Abs(moved.y) <= moved.x * SwipeBackSlope;
			if (swipedBack && menu.IsVisible) Back();
		}

		private bool IsFilming => cameraMode != null && cameraMode.IsActive;

		private static bool IsEditingObjects => MapEditor.MapEditor.IsActive &&
			MapEditorTool.CurrentMode is MapEditorTool.Mode.Move or MapEditorTool.Mode.Place;

		private bool IsEditorShown => IsEditingObjects && !menu.IsVisible && !IsFilming;

		private UIDocument ShownMenuDocument =>
			menu.Gate.Access == GateMenu.AccessState.Granted ? menuDocuments[menu.ScreenPanel] : gateDocument;

		private static bool IsDraggable(VisualElement element)
		{
			for (VisualElement e = element; e != null; e = e.hierarchy.parent)
				if (e is Slider or SliderInt or MinMaxSlider or Scroller or TextField) return true;
			return false;
		}

		// back a page in the panel on screen; from a panel's first page, close the menu
		private void Back()
		{
			if (!menu.IsVisible) return;
			bool granted = menu.Gate.Access == GateMenu.AccessState.Granted;
			if (!Anaglyph.Menu.NavView.TryGoBackIn(ShownMenuDocument.rootVisualElement) && granted) menu.SetVisible(false);
		}

		private void OnDestroy()
		{
			Anaglyph.XR.Input.HandInput.FollowCamera(null);
			Anaglyph.Menu.ActOnPress.ActsOnRelease = false;
			Anaglyph.MenuCopy.Changed -= LabelTabs;
			MapEditor.MapEditor.ActiveChanged -= HandleMapEditorActiveChanged;
			MapEditorTool.ModeChanged -= HandleMapEditorModeChanged;
			MainPlayer.TeamChanged -= ColorReticle;
			cameraScreen?.Dispose();
			if (screenPanel != null) Destroy(screenPanel);
		}

		// the menus' own panel settings, laid flat on the screen
		private static PanelSettings CreateScreenPanel(PanelSettings template)
		{
			PanelSettings panel = Instantiate(template);
			panel.name = "Handheld Screen Panel";
			panel.renderMode = PanelRenderMode.ScreenSpaceOverlay;
			panel.scaleMode = PanelScaleMode.ConstantPixelSize;
			panel.targetTexture = null;
			panel.themeStyleSheet = Resources.Load<ThemeStyleSheet>(TouchThemeResource);
			return panel;
		}

		// a unit is a point on iOS and a dp on Android; Unity's iOS dpi comes from a device table newer phones aren't in
		private float PixelsPerUnit
		{
			get
			{
				if (screenPanel.targetTexture != null || Screen.dpi <= 0) return PixelsPerUnitForTests ?? TexturePixelsPerUnit;
#if UNITY_IOS && !UNITY_EDITOR
				return AnaglyphScreenScale();
#else
				return Screen.dpi / UnitDpi;
#endif
			}
		}

#if UNITY_IOS && !UNITY_EDITOR
		[System.Runtime.InteropServices.DllImport("__Internal")]
		private static extern float AnaglyphScreenScale();
#endif

		private void MoveToScreen(UIDocument document)
		{
			document.panelSettings = screenPanel;
			VisualElement root = document.rootVisualElement;
			if (root == null) return;
			root.style.position = Position.Absolute;
		}

		private int DefaultPanel()
		{
			Unity.Netcode.NetworkManager network = Unity.Netcode.NetworkManager.Singleton;
			bool inSession = network != null && network.IsListening;
			for (int i = 0; i < menu.Panels.Length; i++)
				if (!inSession && menu.Panels[i].GetComponent<ConnectionMenu>() != null)
					return i;
			for (int i = 0; i < menu.Panels.Length; i++)
				if (menu.Panels[i].GetComponent<GameMenu>() != null)
					return i;
			return 0;
		}

		private void BuildOverlay()
		{
			GameObject holder = new("Handheld Screen Overlay");
			holder.transform.SetParent(transform, false);
			overlay = holder.AddComponent<UIDocument>();
			overlay.panelSettings = screenPanel;
			overlay.sortingOrder = 1000;

			VisualElement root = overlay.rootVisualElement;
			root.pickingMode = PickingMode.Ignore;
			root.style.position = Position.Absolute;
			root.style.left = root.style.right = root.style.top = root.style.bottom = 0;
			// the menus' style sheets, so the tabs dress as the game menu's own tabs
			foreach (UIDocument document in menuDocuments)
				document.rootVisualElement?.Query().ForEach(element =>
				{
					for (int i = 0; i < element.styleSheets.count; i++)
						if (!root.styleSheets.Contains(element.styleSheets[i])) root.styleSheets.Add(element.styleSheets[i]);
				});

			reticle = CenteredDot("handheld-reticle", ReticleSize);
			SetBorder(reticle, 1, new Color(0, 0, 0, 0.6f));
			VisualElement reticleCore = CenteredDot("handheld-reticle-core", ReticleCoreSize);
			reticleCore.style.backgroundColor = Color.white;
			reticle.Add(reticleCore);
			root.Add(reticle);
			MainPlayer.TeamChanged += ColorReticle;
			ColorReticle(MainPlayer.Instance != null ? MainPlayer.Instance.Team : (byte)0);

			// one bar along the phone's physical bottom edge: the tabs sharing it equally, the menu button at its end
			tabBar = new VisualElement { name = "handheld-tab-bar" };
			tabBar.AddToClassList("game-menu-tabs");
			tabBar.style.position = Position.Absolute;
			tabBar.style.justifyContent = Justify.FlexEnd;
			tabBar.style.marginLeft = tabBar.style.marginRight = 0;
			root.Add(tabBar);

			tabs = new Button[menu.Panels.Length];
			for (int i = 0; i < tabs.Length; i++)
			{
				int index = i;
				Button tab = new(() => menu.ScreenPanel = index) { name = $"handheld-tab-{i}" };
				tab.AddToClassList("game-menu-tab");
				tab.AddToClassList(PanelColorClass(menu.Panels[i]));
				tabs[i] = tab;
				tabBar.Add(tab);
			}

			if (cameraMode != null)
			{
				cameraTab = new Button(EnterCameraMode) { name = "handheld-tab-camera" };
				cameraTab.AddToClassList("game-menu-tab");
				cameraTab.AddToClassList(NeutralPanelUssClassName);
				tabBar.Add(cameraTab);
				cameraScreen = new CameraModeScreen(cameraMode, root);
			}

			toggle = new VisualElement { name = "handheld-menu-toggle" };
			toggle.style.flexShrink = 0;
			toggle.style.minWidth = toggle.style.minHeight = ControlSize;
			toggle.style.backgroundColor = ToggleFill;
			SetBorder(toggle, 2, ToggleEdge);
			toggle.style.justifyContent = Justify.Center;
			toggle.style.alignItems = Align.Center;
			toggle.RegisterCallback<ClickEvent>(_ => menu.SetVisible(!menu.IsVisible));
			for (int i = 0; i < 3; i++)
			{
				VisualElement line = new() { pickingMode = PickingMode.Ignore };
				line.style.width = 20;
				line.style.height = 2;
				line.style.marginTop = line.style.marginBottom = 3;
				line.style.backgroundColor = Color.white;
				toggle.Add(line);
			}
			tabBar.Add(toggle);
		}

		private void HandleMapEditorActiveChanged(bool active) => ShowMenuUnlessEditingObjects();
		private void HandleMapEditorModeChanged(MapEditorTool.Mode mode) => ShowMenuUnlessEditingObjects();

		// editing objects starts from a menu page: the menu gets out of the way, and is back when
		// editing ends, unless a match ended it
		private void ShowMenuUnlessEditingObjects()
		{
			bool editingObjects = IsEditingObjects;
			if (editingObjects && !wasEditingObjects) menu.SetVisible(false);
			else if (!editingObjects && wasEditingObjects && Matches.MatchReferee.State == Matches.MatchState.NotPlaying) menu.SetVisible(true);
			wasEditingObjects = editingObjects;
		}

		private void EnterCameraMode()
		{
			menu.SetVisible(false);
			cameraMode.SetActive(true);
		}

		// the class that colors each panel, so its tab wears the same colors
		private static string PanelColorClass(Transform panel)
		{
			if (panel.GetComponent<GameMenu>() != null) return "game-panel";
			if (panel.GetComponent<ConnectionMenu>() != null) return "multiplayer-panel";
			if (panel.GetComponent<SettingsMenu>() != null) return "settings-panel";
			return NeutralPanelUssClassName;
		}

		private void LabelTabs()
		{
			if (tabs == null) return;
			for (int i = 0; i < tabs.Length; i++)
				tabs[i].text = TabLabel(menu.Panels[i]);
			if (cameraTab != null) cameraTab.text = Anaglyph.MenuCopy.Get("HUDMenu", "camera.tab");
		}

		private static string TabLabel(Transform panel)
		{
			if (panel.GetComponent<GameMenu>() != null) return Anaglyph.MenuCopy.Get("GameMenu", "GameHomePage.navheader-2.title");
			if (panel.GetComponent<ConnectionMenu>() != null) return Anaglyph.MenuCopy.Get("ConnectionMenu", "ConnectionHomePage.navheader-2.title");
			if (panel.GetComponent<SettingsMenu>() != null) return Anaglyph.MenuCopy.Get("SettingsMenu", "SettingsHomePage.navheader-2.title");
			return panel.name;
		}

		private void LateUpdate()
		{
			if (menu == null || overlay == null) return;

			if (!Mathf.Approximately(screenPanel.scale, PixelsPerUnit)) screenPanel.scale = PixelsPerUnit;

			bool menuShown = menu.IsVisible;
			bool granted = menu.Gate.Access == GateMenu.AccessState.Granted;
			bool filming = IsFilming;
			bool editing = IsEditorShown;
			Display(tabBar, granted && !filming && !editing);
			// closed, only the menu button blocks fire taps; the rest of the bar's strip is open screen
			tabBar.pickingMode = menuShown ? PickingMode.Position : PickingMode.Ignore;
			foreach (Button tab in tabs) Display(tab, menuShown);
			if (cameraTab != null) Display(cameraTab, menuShown);
			Display(reticle, !menuShown && !filming);
			if (hudDocument != null && hudDocument.rootVisualElement != null) Display(hudDocument.rootVisualElement, !filming && !editing);
			foreach (VisualElement score in scoreRoots) Display(score, !filming && !editing);
			if (editorPanel != null) editorPanel.SetVisible(editing);
			cameraScreen?.Refresh();
			weaponPicker?.Refresh(granted && !menuShown && !filming && !editing);
			for (int i = 0; i < tabs.Length; i++)
			{
				tabs[i].EnableInClassList("game-menu-tab-selected", i == menu.ScreenPanel);
				tabs[i].SetEnabled(menu.IsPanelAvailable(i));
			}

			IPanel panel = overlay.rootVisualElement.panel;
			Vector2 panelSize = panel != null ? panel.visualTree.layout.size : Vector2.zero;
			Rect safeArea = SafeAreaPixels;
			ScreenOrientation orientation = Orientation(panelSize);
			// the editor's document builds a new tree each time the editor opens
			VisualElement editorRoot = editorDocument != null ? editorDocument.rootVisualElement : null;
			if (panelSize.x <= 0 || safeArea == appliedSafeArea && panelSize == appliedPanelSize && screenPanel.targetTexture == appliedTarget &&
			    orientation == appliedOrientation && editorRoot == placedEditorRoot) return;
			placedEditorRoot = editorRoot;
			appliedOrientation = orientation;
			appliedSafeArea = safeArea;
			appliedPanelSize = panelSize;
			appliedTarget = screenPanel.targetTexture;
			Layout(panel, panelSize);
		}

		// the safe area in pixels, bottom-left origin as Screen.safeArea gives it, of whatever the panel draws into
		private Rect SafeAreaPixels
		{
			get
			{
				RenderTexture target = screenPanel.targetTexture;
				if (target == null) return Screen.safeArea;
				return SafeAreaForTests ?? new Rect(0, 0, target.width, target.height);
			}
		}

		// insets of the safe area, in panel units
		private (float left, float top, float right, float bottom) SafeInsets(Vector2 panelSize)
		{
			Rect safe = SafeAreaPixels;
			RenderTexture target = screenPanel.targetTexture;
			float pixelWidth = target != null ? target.width : Screen.width, pixelHeight = target != null ? target.height : Screen.height;
			float unitsPerPixelX = panelSize.x / pixelWidth, unitsPerPixelY = panelSize.y / pixelHeight;
			return (safe.xMin * unitsPerPixelX, (pixelHeight - safe.yMax) * unitsPerPixelY,
				(pixelWidth - safe.xMax) * unitsPerPixelX, safe.yMin * unitsPerPixelY);
		}

		// how the screen is turned; a texture target is taken as a phone turned left when wider than tall
		private ScreenOrientation Orientation(Vector2 panelSize) =>
			screenPanel.targetTexture == null && Screen.orientation is ScreenOrientation.Portrait or ScreenOrientation.PortraitUpsideDown
				or ScreenOrientation.LandscapeLeft or ScreenOrientation.LandscapeRight
				? Screen.orientation
				: panelSize.x > panelSize.y ? ScreenOrientation.LandscapeLeft : ScreenOrientation.Portrait;

		// the bar's thickness when it runs down a side: room for a label across
		private const float SideBarThickness = 2 * ControlSize;

		private void Layout(IPanel panel, Vector2 panelSize)
		{
			(float left, float top, float right, float bottom) = SafeInsets(panelSize);
			bottom = Mathf.Max(bottom, GestureEdge);
			if (hudDocument != null) FitDesignSize(hudDocument, panelSize, left, top, right, bottom);
			// the gate has neither the bar nor tabs
			Place(gateDocument, left + Margin, top + Margin, right + Margin, bottom + Margin);

			// the bar keeps to the phone's physical bottom: the screen's bottom upright, its right side turned
			// left (counterclockwise), its left side turned right, its top upside down. Its items keep their order
			// along the phone, so the menu button stays under the same thumb
			ScreenOrientation orientation = Orientation(panelSize);
			bool alongSide = orientation is ScreenOrientation.LandscapeLeft or ScreenOrientation.LandscapeRight;
			float thickness = alongSide ? SideBarThickness : ControlSize;
			tabBar.style.flexDirection = orientation switch
			{
				ScreenOrientation.LandscapeLeft => FlexDirection.ColumnReverse,
				ScreenOrientation.LandscapeRight => FlexDirection.Column,
				ScreenOrientation.PortraitUpsideDown => FlexDirection.RowReverse,
				_ => FlexDirection.Row,
			};
			tabBar.style.left = orientation == ScreenOrientation.LandscapeLeft ? StyleKeyword.Auto : left + Margin;
			tabBar.style.right = orientation == ScreenOrientation.LandscapeRight ? StyleKeyword.Auto : right + Margin;
			tabBar.style.top = orientation == ScreenOrientation.Portrait ? StyleKeyword.Auto : top + Margin;
			tabBar.style.bottom = orientation == ScreenOrientation.PortraitUpsideDown ? StyleKeyword.Auto : bottom + Margin;
			tabBar.style.width = alongSide ? thickness : StyleKeyword.Auto;
			tabBar.style.height = alongSide ? StyleKeyword.Auto : thickness;
			// the same gap between every pair of items, tabs and menu button alike, along whichever way the bar runs
			for (int i = 0; i < tabBar.childCount; i++)
			{
				VisualElement item = tabBar[i];
				item.style.marginLeft = item.style.marginRight = alongSide ? 0 : BarGap / 2;
				item.style.marginTop = item.style.marginBottom = alongSide ? BarGap / 2 : 0;
			}

			// the panels fill the safe area beside the bar
			float barSpace = thickness + 2 * Margin;
			float panelLeft = left + Margin + (orientation == ScreenOrientation.LandscapeRight ? barSpace - Margin : 0);
			float panelRight = right + Margin + (orientation == ScreenOrientation.LandscapeLeft ? barSpace - Margin : 0);
			float panelTop = top + Margin + (orientation == ScreenOrientation.PortraitUpsideDown ? barSpace - Margin : 0);
			float panelBottom = bottom + Margin + (orientation == ScreenOrientation.Portrait ? barSpace - Margin : 0);
			foreach (UIDocument document in menuDocuments)
				Place(document, panelLeft, panelTop, panelRight, panelBottom);

			cameraScreen?.Layout(left, top, right, bottom, orientation);
			weaponPicker?.Layout(panelLeft, panelRight, panelBottom);
			PlaceAlongScreenBottom(editorDocument, left + Margin, right + Margin, bottom + Margin);
		}

		// as tall as its content, on the screen's bottom edge however the phone is turned: the editor's rows scroll sideways
		private static void PlaceAlongScreenBottom(UIDocument document, float left, float right, float bottom)
		{
			VisualElement root = document != null ? document.rootVisualElement : null;
			if (root == null) return;
			root.AddToClassList(TouchScreenUssClassName);
			root.style.width = StyleKeyword.Auto;
			root.style.height = StyleKeyword.Auto;
			root.style.position = Position.Absolute;
			root.style.left = left;
			root.style.right = right;
			root.style.top = StyleKeyword.Auto;
			root.style.bottom = bottom;
		}

		// a document laid out at its headset size (the HUD's 800 × 600), scaled down to fit the space
		// between the insets and centered in it, so its text keeps its proportions and wrapping
		private static void FitDesignSize(UIDocument document, Vector2 panelSize, float left, float top, float right, float bottom)
		{
			VisualElement root = document != null ? document.rootVisualElement : null;
			if (root == null) return;
			Vector2 design = document.worldSpaceSize;
			float width = panelSize.x - left - right, height = panelSize.y - top - bottom;
			float scale = Mathf.Min(1, width / design.x, height / design.y);
			root.style.position = Position.Absolute;
			root.style.right = root.style.bottom = StyleKeyword.Auto;
			root.style.left = left + (width - design.x * scale) / 2;
			root.style.top = top + (height - design.y * scale) / 2;
			root.style.width = design.x;
			root.style.height = design.y;
			root.style.transformOrigin = new TransformOrigin(0, 0);
			root.style.scale = new Scale(new Vector2(scale, scale));
		}

		private static void Place(UIDocument document, float left, float top, float right, float bottom)
		{
			VisualElement root = document != null ? document.rootVisualElement : null;
			if (root == null) return;
			// the touch-screen class lets fixed-size headset menus take the room there is
			root.AddToClassList(TouchScreenUssClassName);
			// a world-space document's root carries its fixed size, which would override the insets
			root.style.width = StyleKeyword.Auto;
			root.style.height = StyleKeyword.Auto;
			root.style.position = Position.Absolute;
			root.style.left = left;
			root.style.top = top;
			root.style.right = right;
			root.style.bottom = bottom;
		}

		// the rig's interfaces that follow a controller or a hand have nothing to follow on a phone: the score moves
		// to the top of the screen and the map editor's palette to the bottom
		private void PutAwayControllerInterfaces()
		{
			foreach (ScoreHUDHandSelector selector in GetComponentsInChildren<ScoreHUDHandSelector>(true))
			{
				selector.enabled = false;
				if (selector.TryGetComponent(out HandHUDPositioner positioner)) positioner.enabled = false;
				if (!selector.TryGetComponent(out UIDocument score)) continue;
				score.panelSettings = screenPanel;
				VisualElement root = score.rootVisualElement;
				if (root == null) continue;
				root.style.position = Position.Absolute;
				root.style.left = root.style.right = 0;
				root.style.top = Margin;
				root.style.alignItems = Align.Center;
				root.pickingMode = PickingMode.Ignore;
				scoreRoots.Add(root);
				VisualElement hud = root.Q("hand-hud-root");
				if (hud != null) hud.style.display = DisplayStyle.Flex;
			}

			PaletteMenu palette = GetComponentInChildren<PaletteMenu>(true);
			if (palette != null && palette.TryGetComponent(out editorDocument))
			{
				editorDocument.panelSettings = screenPanel;
				palette.TryGetComponent(out editorPanel);
			}
		}

		private static void Display(VisualElement element, bool shown) =>
			element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

		private void ColorReticle(byte team) => reticle.style.backgroundColor = Teams.Colors[team];

		private static VisualElement CenteredDot(string name, float size)
		{
			VisualElement dot = new() { name = name, pickingMode = PickingMode.Ignore };
			dot.style.position = Position.Absolute;
			dot.style.width = dot.style.height = size;
			dot.style.left = dot.style.top = Length.Percent(50);
			dot.style.translate = new Translate(-size / 2, -size / 2);
			SetRadius(dot, size / 2);
			return dot;
		}

		private static void SetRadius(VisualElement element, float radius) =>
			element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
				element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;

		private static void SetBorder(VisualElement element, float width, Color color)
		{
			element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = width;
			element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
		}
	}
}
