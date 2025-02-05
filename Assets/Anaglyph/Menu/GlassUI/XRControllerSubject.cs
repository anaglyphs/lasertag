using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate
{
	/// <summary>
	///     Core component for XRController Observer pattern.
	///     Tracks an XR InputDevice, its values, and invokes delegates based on
	///     changes in those values.
	/// </summary>
	public class XRControllerSubject : MonoBehaviour
	{
		public enum JoystickDirection
		{
			Up,
			Down,
			Left,
			Right
		}

		public static List<XRControllerSubject> ActiveSubjects = new();

		[Tooltip("For oculus touch controllers use: HeldInHand, Controller, Left/Right")]
		public InputDeviceCharacteristics DeviceCharacteristics;

		private readonly Dictionary<InputFeatureUsage<bool>, Action> ControllerBinaryFeatureOnPressEvents = new();
		private readonly Dictionary<InputFeatureUsage<bool>, Action> ControllerBinaryFeatureOnReleaseEvents = new();

		private readonly Dictionary<InputFeatureUsage<bool>, bool> ControllerBinaryFeatureValues = new();

		private readonly List<InputFeatureUsage> ControllerFeatures = new();
		private readonly Dictionary<InputFeatureUsage<float>, float> ControllerLinearFeatureValues = new();
		private readonly Dictionary<InputFeatureUsage<Vector2>, Vector2> ControllerPlanarFeatureValues = new();

		private readonly Dictionary<InputFeatureUsage<bool>, bool> PreviousControllerBinaryFeatureValues = new();
		private readonly Dictionary<InputFeatureUsage<float>, float> PreviousControllerLinearFeatureValues = new();
		private readonly Dictionary<InputFeatureUsage<Vector2>, Vector2> PreviousControllerPlanarFeatureValues = new();

		private List<InputFeatureUsage<bool>> binaryFeatures = new();
		public InputDevice Controller;
		private List<InputFeatureUsage<float>> linearFeatures = new();
		private List<InputFeatureUsage<Vector2>> planarFeatures = new();

		private float timeSinceLastSearch;

		#region Unity Lifecycle Events

		private void Update()
		{
			if (Controller.isValid)
			{
				UpdateFeatureValues();
				InvokeEvents();
			}
			else
			{
				ListenForControllerConnection();
			}
		}

		private void OnEnable()
		{
			ActiveSubjects.Add(this);
		}

		private void OnDisable()
		{
			ActiveSubjects.Remove(this);
		}

		#endregion

		#region State Queries

		public static bool GetFeatureStateOnAnyController(InputFeatureUsage<bool> feature)
		{
			foreach (XRControllerSubject subject in ActiveSubjects)
				if (subject.GetFeatureState(feature))
					return true;
			return false;
		}

		public bool GetFeatureState(InputFeatureUsage<bool> feature)
		{
			bool state = false;
			if (Controller.isValid)
				Controller.TryGetFeatureValue(feature, out state);
			return state;
		}

		public float GetFeatureState(InputFeatureUsage<float> feature)
		{
			float state = 0f;
			if (Controller.isValid)
				Controller.TryGetFeatureValue(feature, out state);
			return state;
		}

		public Vector2 GetFeatureState(InputFeatureUsage<Vector2> feature)
		{
			Vector2 state = Vector2.zero;
			if (Controller.isValid)
				Controller.TryGetFeatureValue(feature, out state);
			return state;
		}

		public bool GetJoystickFlick(InputFeatureUsage<Vector2> feature, JoystickDirection dir, float deadzone = 0.5f)
		{
			// flick is defined as a value past the deadzone which was not past it last frame
			Vector2 currentValues = Vector2.zero, previousValues = Vector2.zero;
			if (Controller.isValid)
			{
				currentValues = ControllerPlanarFeatureValues[feature];
				previousValues = PreviousControllerPlanarFeatureValues[feature];
				switch (dir)
				{
					case JoystickDirection.Up:
						return currentValues.y > deadzone && previousValues.y < deadzone;
					case JoystickDirection.Down:
						return currentValues.y < -deadzone && previousValues.y > -deadzone;
					case JoystickDirection.Left:
						return currentValues.x < -deadzone && previousValues.x > -deadzone;
					case JoystickDirection.Right:
						return currentValues.x > deadzone && previousValues.x < deadzone;
				}
			}

			return false;
		}

		public bool GetButtonDown(InputFeatureUsage<bool> feature)
		{
			bool state = false;
			if (Controller.isValid && binaryFeatures.Contains(feature))
				state = ControllerBinaryFeatureValues[feature] && !PreviousControllerBinaryFeatureValues[feature];
			return state;
		}

		public bool GetButtonUp(InputFeatureUsage<bool> feature)
		{
			bool state = false;
			if (Controller.isValid && binaryFeatures.Contains(feature))
				state = !ControllerBinaryFeatureValues[feature] && PreviousControllerBinaryFeatureValues[feature];
			return state;
		}

		#endregion

		#region Event Subscription

		public void SubscribeToOnPressEvent(InputFeatureUsage<bool> input, Action action)
		{
			if (ControllerBinaryFeatureOnPressEvents.ContainsKey(input))
				ControllerBinaryFeatureOnPressEvents[input] += action;
			else
				//Debug.LogWarning ("Subscription to On Press Event generating own dictionary entry. " +
				//	"Key, " + input.name + ", does not exist in observed XR controller on ." + gameObject.name);
				ControllerBinaryFeatureOnPressEvents.Add(input, action);
		}

		public void SubscribeToOnReleaseEvent(InputFeatureUsage<bool> input, Action action)
		{
			if (ControllerBinaryFeatureOnReleaseEvents.ContainsKey(input))
				ControllerBinaryFeatureOnReleaseEvents[input] += action;
			else
				//Debug.LogWarning ("Subscription to On Release Event generating own dictionary entry. " +
				//	"Key, " + input.name + ", does not exist in observed XR controller on ." + gameObject.name);
				ControllerBinaryFeatureOnReleaseEvents.Add(input, action);
		}

		public void UnsubscribeToOnPressEvent(InputFeatureUsage<bool> input, Action action)
		{
			if (ControllerBinaryFeatureOnPressEvents.ContainsKey(input))
				ControllerBinaryFeatureOnPressEvents[input] -= action;
		}

		public void UnsubscribeToOnReleaseEvent(InputFeatureUsage<bool> input, Action action)
		{
			if (ControllerBinaryFeatureOnReleaseEvents.ContainsKey(input))
				ControllerBinaryFeatureOnReleaseEvents[input] -= action;
		}

		#endregion

		#region Initialization

		private void ListenForControllerConnection()
		{
			timeSinceLastSearch += Time.deltaTime;
			if (timeSinceLastSearch >= 0.5f)
			{
				timeSinceLastSearch = 0f;
				List<InputDevice> DeviceMatches = new List<InputDevice>();
				InputDevices.GetDevicesWithCharacteristics(DeviceCharacteristics, DeviceMatches);
				if (DeviceMatches != null && DeviceMatches.Count > 0)
				{
					if (DeviceMatches.Count > 1)
						Debug.LogWarning(
							"XR Controller: Multiple devices matching the device characteristics were found on "
							+ gameObject.name + ". Defaulting to first valid device found.");
					foreach (InputDevice device in DeviceMatches)
						if (device.isValid)
						{
							Debug.Log("XR Controller: Valid device found.");
							Controller = device;
							SetupControllerFeatures();
							return;
						}
						else
						{
							Debug.LogWarning("XR Controller: Device found was invalid.");
						}
				}
				//Debug.LogWarning ("XR Controller: Valid device not found for XRControllerSubject configuration on "
				//	+ gameObject.name + ". Trying again in 0.5 seconds.");
			}
		}

		private void SetupControllerFeatures()
		{
			//Debug.Log ("XR Controller: Setting Up Controller");
			Controller.TryGetFeatureUsages(ControllerFeatures);
			foreach (InputFeatureUsage feature in ControllerFeatures)
			{
				//Debug.Log ("XR Controller: Setting Up " + feature.name + " on " + gameObject.name);
				if (feature.type == typeof(bool))
				{
					ControllerBinaryFeatureValues.Add(feature.As<bool>(), false);
					PreviousControllerBinaryFeatureValues.Add(feature.As<bool>(), false);
				}

				if (feature.type == typeof(float))
				{
					ControllerLinearFeatureValues.Add(feature.As<float>(), 0f);
					PreviousControllerLinearFeatureValues.Add(feature.As<float>(), 0f);
				}

				if (feature.type == typeof(Vector2))
				{
					ControllerPlanarFeatureValues.Add(feature.As<Vector2>(), Vector2.zero);
					PreviousControllerPlanarFeatureValues.Add(feature.As<Vector2>(), Vector2.zero);
				}
			}

			binaryFeatures = new List<InputFeatureUsage<bool>>(ControllerBinaryFeatureValues.Keys);
			linearFeatures = new List<InputFeatureUsage<float>>(ControllerLinearFeatureValues.Keys);
			planarFeatures = new List<InputFeatureUsage<Vector2>>(ControllerPlanarFeatureValues.Keys);
		}

		#endregion

		#region State Update and Event Triggering

		private void UpdateFeatureValues()
		{
			for (int i = 0; i < binaryFeatures.Count; i++)
			{
				InputFeatureUsage<bool> feature = binaryFeatures[i];
				bool value;
				if (Controller.TryGetFeatureValue(feature, out value))
				{
					PreviousControllerBinaryFeatureValues[feature] = ControllerBinaryFeatureValues[feature];
					ControllerBinaryFeatureValues[feature] = value;
				}
			}

			for (int i = 0; i < linearFeatures.Count; i++)
			{
				InputFeatureUsage<float> feature = linearFeatures[i];
				float value;
				if (Controller.TryGetFeatureValue(feature, out value))
				{
					PreviousControllerLinearFeatureValues[feature] = ControllerLinearFeatureValues[feature];
					ControllerLinearFeatureValues[feature] = value;
				}
			}

			for (int i = 0; i < planarFeatures.Count; i++)
			{
				InputFeatureUsage<Vector2> feature = planarFeatures[i];
				Vector2 value;
				if (Controller.TryGetFeatureValue(feature, out value))
				{
					PreviousControllerPlanarFeatureValues[feature] = ControllerPlanarFeatureValues[feature];
					ControllerPlanarFeatureValues[feature] = value;
				}
			}
		}

		private void InvokeEvents()
		{
			for (int i = 0; i < binaryFeatures.Count; i++)
			{
				InputFeatureUsage<bool> feature = binaryFeatures[i];

				//Invoke OnPress Event
				if (ControllerBinaryFeatureValues[feature] && !PreviousControllerBinaryFeatureValues[feature])
					if (ControllerBinaryFeatureOnPressEvents.ContainsKey(feature))
						ControllerBinaryFeatureOnPressEvents[feature]?.Invoke();
				//Invoke OnRelease Event
				if (!ControllerBinaryFeatureValues[feature] && PreviousControllerBinaryFeatureValues[feature])
					if (ControllerBinaryFeatureOnReleaseEvents.ContainsKey(feature))
						ControllerBinaryFeatureOnReleaseEvents[feature]?.Invoke();
			}
		}

		#endregion
	}
}