using System.Collections;
using Anaglyph.LaserTag.Interface;
using Anaglyph.LaserTag.Matches;
using Anaglyph.Menu;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Tests
{
	public class MenuBindingLifetimeTests
	{
		private MenuBindingLifetimeTestWindow window;
		private Locale previousLocale;

		[SetUp]
		public void SetUp()
		{
			previousLocale = LocalizationSettings.SelectedLocale;
			LocalizationSettings.SelectedLocale = AssetDatabase.LoadAssetAtPath<Locale>(
				"Assets/Anaglyph/LaserTag/Localization/English.asset");
			window = ScriptableObject.CreateInstance<MenuBindingLifetimeTestWindow>();
			window.position = new Rect(100, 100, 440, 650);
			window.Show();
		}

		[TearDown]
		public void TearDown()
		{
			window.Close();
			LocalizationSettings.SelectedLocale = previousLocale;
		}

		private VisualElement LoadMatchPage()
		{
			var root = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
				"Assets/Anaglyph/LaserTag/Interface/Main Menu/Game/GameMenu.uxml").CloneTree();
			root.style.flexGrow = 1;
			root.MakeButtonsActOnPress();
			window.rootVisualElement.Add(root);
			NavView.RequireIn(root).GoToPage("match-page");
			return root;
		}

		[UnityTest]
		public IEnumerator RebindingMatchSettingsLeavesTheDisposedBinderUnchanged()
		{
			var root = LoadMatchPage();
			for (int i = 0; i < 2; i++) yield return null;
			var score = root.Q<SliderInt>("score-target-slider");
			var damage = root.Q<Slider>("damage-multiplier-slider");
			var zombies = root.Q<Toggle>("spawn-zombies-toggle");
			var win = root.Q<RadioButtonGroup>("win-by-radio");
			using var previous = new MatchSettingsBinder(root);
			score.value = 7;
			damage.value = 0.75f;
			zombies.value = true;
			win.value = 1;
			MatchSettings saved = previous.Settings;
			Assert.That(saved.scoreTarget, Is.EqualTo(7));
			Assert.That(saved.damageMultiplier, Is.EqualTo(0.75f));
			Assert.That(saved.spawnZombies, Is.True);
			Assert.That(saved.CheckWinByTimer(), Is.False);

			previous.Dispose();
			previous.Dispose();
			using var current = new MatchSettingsBinder(root);
			score.value = 12;
			damage.value = 1.5f;
			zombies.value = false;
			win.value = 2;

			Assert.That(previous.Settings, Is.EqualTo(saved),
				"Controls retained by the panel must not update a disposed owner.");
			Assert.That(current.Settings.scoreTarget, Is.EqualTo(12));
			Assert.That(current.Settings.damageMultiplier, Is.EqualTo(1.5f));
			Assert.That(current.Settings.spawnZombies, Is.False);
			Assert.That(current.Settings.CheckWinByScore(), Is.True);
			Assert.That(current.Settings.CheckWinByTimer(), Is.True);
			Assert.That(root.Q<Slider>("round-time-slider").enabledSelf, Is.True);
			Assert.That(score.enabledSelf, Is.True);
		}

		[UnityTest]
		public IEnumerator RepeatedMenuBindingsStartOneMatchAndStopRespondingWhenDisposed()
		{
			var root = LoadMatchPage();
			for (int i = 0; i < 2; i++) yield return null;
			var zombies = root.Q<Toggle>("spawn-zombies-toggle");
			var score = root.Q<SliderInt>("score-target-slider");
			int starts = 0;
			int edits = 0;
			MatchSettings submitted = default;

			// Controllers may be disabled and re-enabled while the same document survives.
			for (int cycle = 0; cycle < 3; cycle++)
			{
				root.MakeButtonsActOnPress();
				using var match = new MatchSettingsBinder(root);
				using var events = new UIEventBindings();
				events.Click(match.StartButton, () =>
				{
					starts++;
					submitted = match.Settings;
				});
				events.Value(zombies, _ => edits++);
				score.value = 8 + cycle;
				zombies.value = !zombies.value;
				Submit(match.StartButton);
				Assert.That(starts, Is.EqualTo(cycle + 1));
				Assert.That(edits, Is.EqualTo(cycle + 1));
				Assert.That(submitted.scoreTarget, Is.EqualTo(8 + cycle));
				Assert.That(submitted.spawnZombies, Is.EqualTo(zombies.value));

				events.Dispose();
				events.Dispose();
				match.Dispose();
				zombies.value = !zombies.value;
				Submit(match.StartButton);
				Assert.That(starts, Is.EqualTo(cycle + 1), "A disabled menu must not start a match.");
				Assert.That(edits, Is.EqualTo(cycle + 1), "A disabled menu must not process field edits.");
			}
		}

		private static void Submit(Button button)
		{
			using (var down = PointerDownEvent.GetPooled(new Event
				{ type = EventType.MouseDown, button = 0, mousePosition = button.worldBound.center }))
				button.SendEvent(down);
			using (var up = PointerUpEvent.GetPooled(new Event
				{ type = EventType.MouseUp, button = 0, mousePosition = button.worldBound.center }))
				button.SendEvent(up);
		}
	}

	public sealed class MenuBindingLifetimeTestWindow : EditorWindow { }
}
