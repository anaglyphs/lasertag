using System;
using System.Collections.Generic;
using Anaglyph.Debugging.Visuals;
using Anaglyph.Input;
using Anaglyph.XRTemplate.SharedSpaces;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.Lasertag
{
	/// <summary>
	/// Registers tags into the current map — a deliberate authoring act, before hosting:
	/// with the palette's tag mode selected, aim at a freshly observed tag and pull the
	/// trigger. Registering an already-registered tag rewrites its canon pose, which is
	/// also the recovery path for a tag that has been physically moved.
	///
	/// Lives on each map-editor hand, next to <see cref="MapEditorTool"/>, and activates
	/// with it.
	/// </summary>
	public class TagRegistrationTool : MonoBehaviour
	{
		public static bool RegistrationMode { get; private set; }
		public static event Action<bool> RegistrationModeChanged = delegate { };

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Init()
		{
			RegistrationMode = false;
			RegistrationModeChanged = delegate { };
		}

		public static void SetRegistrationMode(bool on)
		{
			if (RegistrationMode == on) return;
			RegistrationMode = on;

			// Registration needs tag detection even with no map loaded, which is otherwise
			// exactly when colocation would leave it off.
			ColocationManager.Instance?.SetTagDetectionOverride(on && MapEditor.IsActive);
			RegistrationModeChanged.Invoke(on);
		}

		[SerializeField] private MapEditorTool editorTool;
		[SerializeField] private HandSubject handSubject;

		[SerializeField] private string registerInputBinding = "OnFire";
		[SerializeField] private string unregisterInputBinding = "OnBack";

		[Tooltip("How far off the hand ray a tag can be and still count as aimed at")]
		[SerializeField] private float aimMaxAngleDegrees = 10f;

		[Tooltip("Seconds an observation stays valid; stale poses must not be registered")]
		[SerializeField] private float observationLifetime = 5f;

		private readonly Dictionary<int, (Pose pose, float time)> observations = new();
		private readonly List<int> expiredScratch = new();

		private int aimedTagId = -1;

		private void Awake()
		{
			if (!editorTool)
				TryGetComponent(out editorTool);
			if (!handSubject)
				TryGetComponent(out handSubject);
		}

		private void Start()
		{
			handSubject.Bind(registerInputBinding, OnRegisterInput);
			handSubject.Bind(unregisterInputBinding, OnUnregisterInput);
		}

		private void OnEnable()
		{
			if (TagConstraintProvider.Instance != null)
				TagConstraintProvider.Instance.TagObserved += OnTagObserved;

			ColocationManager.Instance?.SetTagDetectionOverride(
				RegistrationMode && MapEditor.IsActive);
		}

		private void OnDisable()
		{
			observations.Clear();
			aimedTagId = -1;

			if (TagConstraintProvider.Instance != null)
				TagConstraintProvider.Instance.TagObserved -= OnTagObserved;

			// Leaving the map editor drops the override; colocation decides again whether
			// tag detection stays on.
			if (!MapEditor.IsActive)
				ColocationManager.Instance?.SetTagDetectionOverride(false);
		}

		private void OnTagObserved(int id, Pose pose)
		{
			if (!RegistrationMode)
				return;

			observations[id] = (pose, Time.time);
		}

		private void LateUpdate()
		{
			if (!RegistrationMode)
			{
				aimedTagId = -1;
				return;
			}

			ExpireObservations();
			aimedTagId = FindAimedTag();

			GameMap map = MapManager.Instance != null ? MapManager.Instance.CurrentMap : null;

			foreach ((int id, (Pose pose, float _)) in observations)
			{
				bool registered = map != null && map.TryGetTag(id, out _);
				bool aimed = id == aimedTagId;

				Color color = registered ? Color.green : Color.yellow;
				if (aimed) color = Color.white;

				DebugAxisVisual.DrawDebugAxis(pose.position, pose.rotation, color);
			}
		}

		private void ExpireObservations()
		{
			expiredScratch.Clear();

			foreach ((int id, (Pose _, float time)) in observations)
				if (Time.time - time > observationLifetime)
					expiredScratch.Add(id);

			foreach (int id in expiredScratch)
				observations.Remove(id);
		}

		private int FindAimedTag()
		{
			int best = -1;
			float bestAngle = aimMaxAngleDegrees;

			foreach ((int id, (Pose pose, float _)) in observations)
			{
				float angle = Vector3.Angle(transform.forward, pose.position - transform.position);

				if (angle < bestAngle)
				{
					bestAngle = angle;
					best = id;
				}
			}

			return best;
		}

		private bool CanAct()
		{
			return RegistrationMode &&
			       aimedTagId >= 0 &&
			       editorTool == MapEditorTool.DominantHand &&
			       !handSubject.Current.InputBlocked &&
			       MapManager.Instance != null;
		}

		private void OnRegisterInput(InputAction.CallbackContext context)
		{
			if (!context.performed || !CanAct())
				return;

			(Pose pose, float _) = observations[aimedTagId];

			if (!MapManager.Instance.RegisterTag(aimedTagId, pose))
				Debug.LogWarning($"Couldn't register tag {aimedTagId} — " +
					"tags only register outside sessions, in a trusted frame.");
		}

		private void OnUnregisterInput(InputAction.CallbackContext context)
		{
			if (!context.performed || !CanAct())
				return;

			MapManager.Instance.UnregisterTag(aimedTagId);
		}
	}
}
