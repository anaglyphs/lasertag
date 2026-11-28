using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>Shows the time and headset battery while the panel is visible.</summary>
	[DefaultExecutionOrder(100)]
	[RequireComponent(typeof(UIToolkitPanelXRSetup))]
	public class MenuStatusBar : MonoBehaviour
	{
		private const float RefreshIntervalSeconds = 1f;
		private const int LowBatteryPercent = 20;

		private UIToolkitPanelXRSetup panel;
		private Label clockLabel;
		private Label batteryLabel;
		private VisualElement battery;
		private VisualElement batteryFill;
		private float nextRefreshTime;

		private void Awake() => panel = GetComponent<UIToolkitPanelXRSetup>();

		private void OnEnable()
		{
			VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException("MenuStatusBar requires an enabled UIDocument with a visual tree.");

			clockLabel = Require<Label>(root, "clock-label");
			battery = Require<VisualElement>(root, "battery");
			batteryLabel = Require<Label>(root, "battery-label");
			batteryFill = Require<VisualElement>(root, "battery-fill");
			Refresh();
		}

		private void Update()
		{
			if (panel.IsVisible && Time.unscaledTime >= nextRefreshTime) Refresh();
		}

		private void Refresh()
		{
			nextRefreshTime = Time.unscaledTime + RefreshIntervalSeconds;
			clockLabel.text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);

			float level = SystemInfo.batteryLevel;
			bool batteryIsKnown = level >= 0;
			battery.style.display = batteryIsKnown ? DisplayStyle.Flex : DisplayStyle.None;
			if (!batteryIsKnown) return;

			int percent = Mathf.Clamp(Mathf.RoundToInt(level * 100), 0, 100);
			batteryLabel.text = $"{percent}%";
			batteryFill.style.width = Length.Percent(percent);
			battery.EnableInClassList("battery--low", percent <= LowBatteryPercent);
			battery.EnableInClassList("battery--charging",
				SystemInfo.batteryStatus is BatteryStatus.Charging or BatteryStatus.Full);
		}
	}
}
