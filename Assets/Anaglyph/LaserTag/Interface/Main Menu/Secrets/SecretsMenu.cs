using System;
using Anaglyph.Menu;
using Anaglyph.VariableObjects;
using Anaglyph.XR;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;
using static Anaglyph.Menu.UIQuery;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// Sits on the back of the connection panel, facing the other way, and shows only
	/// while the connection panel is shown and the camera is behind it.
	/// </summary>
	[DefaultExecutionOrder(100)]
	[RequireComponent(typeof(UIToolkitPanelXRSetup))]
	public class SecretsMenu : MonoBehaviour
	{
		[SerializeField] private UIToolkitPanelXRSetup parentPanel;
		[SerializeField] private float gapBehindConnectionPanelInMeters = 0.01f;
		[SerializeField] private BoolObject hostPrototypeZombiesSetting;

		private readonly UIEventBindings bindings = new();
		private UIToolkitPanelXRSetup panel;
		private Toggle hostPrototypeZombiesToggle;

		private void Awake()
		{
			panel = GetComponent<UIToolkitPanelXRSetup>();
			panel.SetVisible(false);
		}

		private void OnEnable()
		{
			VisualElement root = GetComponent<UIDocument>()?.rootVisualElement;
			if (root == null)
				throw new InvalidOperationException(
					"SecretsMenu requires an enabled UIDocument with a visual tree.");

			hostPrototypeZombiesToggle = Require<Toggle>(root, "host-prototype-zombies-toggle");
			bindings.Value(hostPrototypeZombiesToggle,
				change => hostPrototypeZombiesSetting.Value = change.newValue);

			hostPrototypeZombiesSetting.Changed += OnHostPrototypeZombiesChanged;
			OnHostPrototypeZombiesChanged(hostPrototypeZombiesSetting.Value);
		}

		private void OnDisable()
		{
			bindings.Dispose();
			hostPrototypeZombiesSetting.Changed -= OnHostPrototypeZombiesChanged;
			panel.SetVisible(false);
		}

		private void LateUpdate()
		{
			Transform front = parentPanel.transform;

			panel.SetVisible(parentPanel.IsVisible && parentPanel.IsPlacedInWorld &&
				IsCameraBehindConnectionPanel());
		}

		private bool IsCameraBehindConnectionPanel()
		{
			Camera viewer = MainXRRig.Camera;
			if (viewer == null) return false;
			Transform front = parentPanel.transform;
			return Vector3.Dot(viewer.transform.position - front.position, front.forward) >
				gapBehindConnectionPanelInMeters;
		}

		private void OnHostPrototypeZombiesChanged(bool enabled) =>
			hostPrototypeZombiesToggle.SetValueWithoutNotify(enabled);
	}
}
