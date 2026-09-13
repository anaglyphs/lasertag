using System;
using System.Linq;
using System.Reflection;
using Anaglyph.LaserTag.Interface;
using Anaglyph.Netcode;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Tests
{
	public class MainMenuControllerTests
	{
		private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
		private GameObject root;
		private MainMenuController controller;
		private GateMenu gate;
		private UIToolkitPanelXRSetup gatePanel;
		private UIToolkitPanelXRSetup[] panels;
		private HeadsetConfiguration previousConfiguration;
		private Action accessChanged;

		[SetUp]
		public void SetUp()
		{
			root = PrefabUtility.LoadPrefabContents("Assets/Anaglyph/LaserTag/Interface/Main Menu/Menu.prefab");
			controller = root.GetComponent<MainMenuController>();
			gate = root.GetComponentInChildren<GateMenu>(true);
			gatePanel = gate.GetComponent<UIToolkitPanelXRSetup>();
			panels = ((Transform[])Get(controller, "panels"))
				.Select(t => t.GetComponent<UIToolkitPanelXRSetup>()).ToArray();
			previousConfiguration = HeadsetConfiguration.Instance;
			HeadsetConfiguration configuration = root.GetComponent<HeadsetConfiguration>();
			Set(configuration, "requireMenuPassword", true);
			Set(configuration, "menuPasswordHash", "test-password-hash");
			typeof(HeadsetConfiguration).GetProperty("Instance")?.SetValue(null, configuration);
			Set(controller, "gateMenu", gate);
			Set(controller, "gatePanel", gatePanel);
			Set(controller, "menuPanels", panels);
			Set(controller, "sessionDiscoveryController", root.GetComponent<SessionDiscoveryController>());
			Set(controller, "initializedVisibility", true);
			Set(controller, "lastAccess", GateMenu.AccessState.Granted);
			Set(controller, "transitionProgress", 0.5f);
			Set(gate, "gatePanel", gatePanel);
			Set(gate, "requiredPermissionsGranted", true);
			Set(gate, "passwordUnlocked", true);
			Call(gate, "RefreshAccess", false);
			accessChanged = () => Call(controller, "OnAccessChanged");
			gate.AccessChanged += accessChanged;
		}

		[TearDown]
		public void TearDown()
		{
			if (gate != null) gate.AccessChanged -= accessChanged;
			typeof(HeadsetConfiguration).GetProperty("Instance")?.SetValue(null, previousConfiguration);
			if (root != null) PrefabUtility.UnloadPrefabContents(root);
		}

		[Test]
		public void ClosingKeepsCurrentPanelsOpaqueAndRelocksAccess()
		{
			Call(controller, "SetVisibility", true, false);
			Call(controller, "SetVisibility", false, false);
			Assert.That(controller.IsVisible, Is.False);
			Assert.That(controller.IsClosing, Is.True);
			Assert.That(gate.Access, Is.EqualTo(GateMenu.AccessState.Password));
			AssertPanel(gatePanel, false, false);
			foreach (var panel in panels) AssertPanel(panel, false, true);
			Set(controller, "transitionLength", 0f);
			Call(controller, "Update");
			Assert.That(controller.IsClosing, Is.False);
			foreach (var panel in panels) AssertPanel(panel, false, false);
		}

		[Test]
		public void AccessChangesWhileClosedDoNotOpenAnyPanel()
		{
			Set(controller, "transitionProgress", 0f);
			gate.ResetPasswordAccess();
			Assert.That(controller.IsVisible, Is.False);
			AssertPanel(gatePanel, false, false);
			Set(gate, "passwordUnlocked", true);
			Call(gate, "RefreshAccess", false);
			Assert.That(controller.IsVisible, Is.False);
			foreach (var panel in panels) AssertPanel(panel, false, false);
		}

		[Test]
		public void ReopeningDuringClosePreservesProgressAndShowsPasswordGate()
		{
			Call(controller, "SetVisibility", true, false);
			var positions = panels.Select(p => p.transform.localPosition).ToArray();
			Call(controller, "SetVisibility", false, false);
			Call(controller, "SetVisibility", true, false);
			Assert.That(Get(controller, "transitionProgress"), Is.EqualTo(0.5f));
			Assert.That(controller.IsClosing, Is.False);
			AssertPanel(gatePanel, true, true);
			for (int i = 0; i < panels.Length; i++)
			{
				AssertPanel(panels[i], false, false);
				Assert.That(panels[i].transform.localPosition, Is.EqualTo(positions[i]));
			}
		}

		[Test]
		public void ConnectionClosesMenuAndEndingTransitionHidesPanels()
		{
			Call(controller, "SetVisibility", true, false);
			Call(controller, "OnNetworkStateChanged", NetcodeState.Connected);
			Assert.That(controller.IsVisible, Is.False);
			Assert.That(controller.IsClosing, Is.True);
			Call(controller, "OnDisable");
			Assert.That(controller.IsClosing, Is.False);
			foreach (var panel in panels) AssertPanel(panel, false, false);
		}

		[Test]
		public void PermissionLossIsHandledByControllerAndDismissalStaysClosed()
		{
			Set(gate, "requiredPermissionsGranted", false);
			Call(gate, "RefreshAccess", false);
			Assert.That(controller.IsVisible, Is.True);
			AssertPanel(gatePanel, true, true);
			Call(controller, "SetVisibility", false, false);
			Set(controller, "transitionProgress", 0f);
			Call(controller, "ApplyPanelVisibility");
			Call(gate, "RefreshAccess", false);
			Assert.That(controller.IsVisible, Is.False);
			AssertPanel(gatePanel, false, false);
		}

		[TestCase(4.99f, true)]
		[TestCase(5f, true)]
		[TestCase(5.01f, false)]
		public void WalkingAwayClosesOnlyBeyondFiveMeters(float distance, bool remainsVisible)
		{
			Call(controller, "SetVisibility", true, false);
			Call(controller, "CheckAutoCloseDistance", root.transform.position + new Vector3(distance, 2f, 0));
			Assert.That(controller.IsVisible, Is.EqualTo(remainsVisible));
			Assert.That(controller.IsClosing, Is.EqualTo(!remainsVisible));
			if (!remainsVisible)
				Assert.That(gate.Access, Is.EqualTo(GateMenu.AccessState.Password));
		}

		private static void AssertPanel(UIToolkitPanelXRSetup panel, bool visible, bool rendered)
		{
			Assert.That(panel.IsVisible, Is.EqualTo(visible));
			Assert.That(panel.GetComponent<BoxCollider>().enabled, Is.EqualTo(visible));
			var ui = panel.GetComponent<UIDocument>().rootVisualElement;
			Assert.That(ui.style.display.value, Is.EqualTo(rendered ? DisplayStyle.Flex : DisplayStyle.None));
			Assert.That(ui.enabledSelf, Is.True, "Closing must not apply disabled styling.");
		}

		private static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
		private static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
		private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
	}
}
