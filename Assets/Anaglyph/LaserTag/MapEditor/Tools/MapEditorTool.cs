using System;
using Anaglyph.LaserTag.Maps;
using Anaglyph.LaserTag.Player.Teams;
using Anaglyph.XR.Input;
using Anaglyph.XR.SharedSpaces.AprilTags;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Anaglyph.LaserTag.MapEditor.Tools
{
	/// <summary>
	/// The map editor's hand tool. One of these per hand, activated with the editor.
	///
	/// It has one mode at a time and the buttons mean whatever that mode says: the trigger places
	/// an object or registers a tag, the back button deletes one or unregisters one. Mode is
	/// static because both hands share the palette selection and the game menu's tag mode.
	/// </summary>
	public class MapEditorTool : MonoBehaviour
	{
		public enum Mode
		{
			/// <summary>Grab and reposition what is already placed.</summary>
			Move,

			/// <summary>Place copies of the palette's selected object.</summary>
			Place,

			/// <summary>Register the map's AprilTags.</summary>
			Tags
		}

		public static Mode CurrentMode { get; private set; }
		public static MapObject SelectedObject { get; private set; }
		public static event Action<Mode> ModeChanged = delegate { };

		public static MapEditorTool DominantHand;

		// Statics persist across play sessions while domain reload is disabled.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			CurrentMode = Mode.Move;
			SelectedObject = null;
			ModeChanged = delegate { };
			DominantHand = null;
		}

		/// <summary>
		/// Switches both hands to a mode. <paramref name="spawnObject"/> is what
		/// <see cref="Mode.Place"/> places, and is ignored by every other mode — so a mode and the
		/// thing it acts on can never disagree.
		/// </summary>
		public static void SetMode(Mode mode, MapObject spawnObject = null)
		{
			CurrentMode = mode;
			// Keep the object selection while the tags submenu temporarily owns the tools.
			if (mode != Mode.Tags)
				SelectedObject = mode == Mode.Place ? spawnObject : null;

			MapEditorTool[] tools = FindObjectsByType<MapEditorTool>(
				FindObjectsInactive.Include, FindObjectsSortMode.None);

			foreach (MapEditorTool tool in tools)
				tool.ApplyMode(mode, spawnObject);

			UpdateTagDetection();
			ModeChanged.Invoke(mode);
		}

		/// <summary>
		/// Registering needs tag detection even with no map loaded, which is otherwise exactly
		/// when colocation would leave it off.
		/// </summary>
		private static void UpdateTagDetection()
		{
			ColocationManager.Instance?.SetTagDetectionOverride(
				CurrentMode == Mode.Tags && MapEditor.IsActive);
		}

		[SerializeField] private Color placeColor = Color.green;
		[SerializeField] private Color deleteColor = Color.red;
		[SerializeField] private Color moveColor = Color.white;

		[SerializeField] private string rotateInputBinding = "OnAxis";
		[SerializeField] private string moveInputBinding = "OnGrip";
		[SerializeField] private string deleteInputBinding = "OnBack";
		[SerializeField] private string placeInputBinding = "OnFire";

		[SerializeField] private float rotationSpeed;
		[SerializeField] private float distanceSpeed;

		[Tooltip("How far off the hand ray a tag can be and still count as aimed at")]
		[SerializeField] private float aimMaxAngleDegrees = 10f;

		[Tooltip("Seconds a tag observation stays valid; stale poses must not be registered")]
		[SerializeField] private float observationLifetime = 5f;

		private MapObject currentSpawnObject;
		private GameObject previewObject;
		private float spawnRotation;

		private MapObject grabbedObject;
		private Vector3 grabOffset;
		private float grabDistance;
		private float grabRotation;

		private float rotationDelta;
		private float distanceDelta;

		private TagAimer tagAimer;

		/// <summary>
		/// Built on demand: a mode change reaches every tool including ones on still-inactive
		/// hands, whose Awake has not run yet. Serialized fields are already deserialized by
		/// then, so the settings below are the authored ones either way.
		/// </summary>
		private TagAimer Aimer => tagAimer ??= new TagAimer(observationLifetime, aimMaxAngleDegrees);

		[SerializeField] private HandSubject handSubject;

		[SerializeField] private LineRenderer lineRenderer;

		private void Awake()
		{
			MapEditor.ActiveChanged += gameObject.SetActive;
			gameObject.SetActive(MapEditor.IsActive);

			lineRenderer.useWorldSpace = false;
		}

		private void Start()
		{
			if (!handSubject)
				TryGetComponent(out handSubject);

			if (handSubject.Current.Handedness == Handedness.Right)
				DominantHand = this;

			handSubject.Bind(rotateInputBinding, OnRotateInput);
			handSubject.Bind(placeInputBinding, OnFireInput);
			handSubject.Bind(deleteInputBinding, OnBackInput);
			handSubject.Bind(moveInputBinding, OnGripInput);
		}

		private void OnDestroy()
		{
			MapEditor.ActiveChanged -= gameObject.SetActive;
		}

		private void OnEnable()
		{
			Aimer.Register();
			UpdateTagDetection();
		}

		private void OnDisable()
		{
			Aimer.Unregister();
			Aimer.Clear();
			ReleaseTagHighlight();

			if (previewObject != null)
				previewObject.SetActive(false);

			// Leaving the map editor drops the override; colocation decides again whether tag
			// detection stays on.
			UpdateTagDetection();
		}

		/// <summary>Whatever the outgoing mode was holding is not the incoming one's to keep.</summary>
		private void ApplyMode(Mode mode, MapObject spawnObject)
		{
			SetSpawnObject(mode == Mode.Place ? spawnObject : null);
			TryLetGo();
			Aimer.Clear();
			ReleaseTagHighlight();
		}

		public void SetSpawnObject(MapObject spawnObject)
		{
			if (Equals(spawnObject, currentSpawnObject))
				return;

			currentSpawnObject = spawnObject;

			if (previewObject)
				Destroy(previewObject);

			if (spawnObject != null) previewObject = InstantiateObjectAsPreview(spawnObject);
		}

		private Quaternion GetSpawnRotation()
		{
			return Quaternion.Euler(0, spawnRotation, 0);
		}

		// ------- map objects -------------------------------------

		public bool TryDelete()
		{
			if (!Raycast(out GameObject hitObj))
				return false;

			MapObject mapObj = hitObj.GetComponentInParent<MapObject>();
			if (mapObj == null || LaserTagMapCoordinator.Instance == null || !LaserTagMapCoordinator.Instance.RequestRemoveObject(mapObj))
				return false;

			MapObject.NotifyLocalEdit();
			return true;
		}

		public bool TryPlace()
		{
			if (!CheckCanPlace())
				return false;

			if (!Raycast(out Vector3 hitPos))
				return false;

			if (!SpawnMapObject(currentSpawnObject, hitPos, GetSpawnRotation()))
				return false;

			MapObject.NotifyLocalEdit();

			return true;
		}

		public bool TryGrab()
		{
			// Moving an object records a new world pose, so it is held to the same rule as
			// placing one.
			if (LaserTagMapCoordinator.Instance != null && !LaserTagMapCoordinator.Instance.CheckCanEditMap())
				return false;

			if (!Raycast(out RaycastHit hit)) return false;

			grabbedObject = hit.collider.GetComponentInParent<MapObject>();
			if (grabbedObject == null || !grabbedObject.Movable) return false;

			grabbedObject.TryTakeOwnership();

			grabOffset = hit.point - grabbedObject.transform.position;
			grabDistance = hit.distance;
			grabRotation = grabbedObject.transform.eulerAngles.y;

			return true;
		}

		public void TryLetGo()
		{
			if (grabbedObject != null)
			{
				LaserTagMapCoordinator.Instance?.CommitObjectMove(grabbedObject);
				grabbedObject.ReleaseOwnership();
				MapObject.NotifyLocalEdit();
			}

			grabbedObject = null;
		}

		// Also drives the placement preview, so an edit the map manager would refuse shows as
		// nothing to place rather than as a press that does nothing.
		private bool CheckCanPlace()
		{
			return CurrentMode == Mode.Place && currentSpawnObject != null &&
				!handSubject.Current.InputBlocked &&
				this == DominantHand &&
				(LaserTagMapCoordinator.Instance == null || LaserTagMapCoordinator.Instance.CheckCanEditMap());
		}

		// ------- tags --------------------------------------------

		private bool CanActOnTag()
		{
			return CurrentMode == Mode.Tags &&
			       this == DominantHand &&
			       Aimer.AimedTagId >= 0 &&
			       !handSubject.Current.InputBlocked &&
			       LaserTagMapCoordinator.Instance != null;
		}

		public bool TryRegisterTag()
		{
			if (!CanActOnTag())
				return false;

			int tagId = Aimer.AimedTagId;

			string blocker = LaserTagMapCoordinator.Instance.DescribeTagRegistrationBlocker();
			if (blocker != null)
			{
				Debug.LogWarning($"Couldn't register tag {tagId} — {blocker}.");
				return false;
			}

			return Aimer.TryGetObservation(tagId, out Pose pose) &&
			       LaserTagMapCoordinator.Instance.RegisterTag(tagId, pose);
		}

		public bool TryUnregisterTag()
		{
			return CanActOnTag() && LaserTagMapCoordinator.Instance.UnregisterTag(Aimer.AimedTagId);
		}

		private void ReleaseTagHighlight()
		{
			// Only the hand that set the highlight may clear it, or the off hand would wipe the
			// dominant one's every frame.
			if (this == DominantHand)
				TagReferenceVisuals.HighlightedTagId = -1;
		}

		// ------- per-frame ---------------------------------------

		private void LateUpdate()
		{
			bool didHit = Raycast(out RaycastHit hit);
			float lineDist = didHit ? hit.distance : 1f;

			if (CurrentMode == Mode.Tags)
				UpdateTagAim();
			else
				lineDist = UpdateObjectTools(didHit, hit, lineDist);

			lineRenderer.enabled = !handSubject.Current.InputBlocked;
			lineRenderer.SetPosition(0, Vector3.zero);
			lineRenderer.SetPosition(1, Vector3.forward * lineDist);
		}

		private void UpdateTagAim()
		{
			if (this != DominantHand)
				return;

			// The indicator drawn at every visible tag is the one visual; aiming just recolors it.
			TagReferenceVisuals.HighlightedTagId =
				Aimer.Aim(transform.position, transform.forward);
		}

		private float UpdateObjectTools(bool didHit, RaycastHit hit, float lineDist)
		{
			if (grabbedObject != null)
			{
				if (previewObject != null)
					previewObject.SetActive(false);

				if (grabbedObject.CanManage())
				{
					Vector3 targetWorldPos = transform.position + transform.forward * grabDistance;
					Vector3 currentGrabWorldPos = grabbedObject.transform.position + grabOffset;
					grabbedObject.transform.position += targetWorldPos - currentGrabWorldPos;

					grabDistance += distanceDelta * Time.deltaTime * distanceSpeed;
					grabRotation += rotationDelta * Time.deltaTime * rotationSpeed;

					grabbedObject.transform.rotation = Quaternion.Euler(0, grabRotation, 0);

					lineDist = grabDistance;
				}
			}
			else if (previewObject != null)
			{
				spawnRotation += rotationDelta * Time.deltaTime * rotationSpeed;

				previewObject.transform.position = hit.point;
				previewObject.transform.rotation = GetSpawnRotation();
				previewObject.SetActive(didHit && CheckCanPlace());
			}

			return lineDist;
		}

		#region Input

		private void OnFireInput(InputAction.CallbackContext context)
		{
			if (!context.performed) return;

			// Every mode: the off hand's trigger is how the palette swaps sides, so it has to be
			// answered before whatever the current mode would do with the press.
			if (this != DominantHand)
			{
				Handedness otherHand =
					handSubject.Current.Handedness == Handedness.Left ? Handedness.Right : Handedness.Left;

				DominantHand = this;
				Palette.Instance?.SetHandSide(otherHand);
				return;
			}

			if (CurrentMode == Mode.Tags)
			{
				TryRegisterTag();
				return;
			}

			if (grabbedObject != null) return;

			TryPlace();
		}

		private void OnBackInput(InputAction.CallbackContext context)
		{
			if (!context.performed) return;

			if (CurrentMode == Mode.Tags)
				TryUnregisterTag();
			else
				TryDelete();
		}

		private void OnGripInput(InputAction.CallbackContext context)
		{
			if (CurrentMode == Mode.Tags)
				return;

			if (context.performed)
				TryGrab();
			else if (context.canceled && grabbedObject != null)
				TryLetGo();
		}

		private void OnRotateInput(InputAction.CallbackContext context)
		{
			Vector2 axis = context.ReadValue<Vector2>();

			rotationDelta = -axis.x;
			distanceDelta = axis.y;

			if (Mathf.Abs(axis.x) > Mathf.Abs(axis.y))
				distanceDelta = 0;
			else
				rotationDelta = 0;
		}

		#endregion


		#region Helpers

		private bool Raycast(out RaycastHit hit)
		{
			Ray ray = new(transform.position, transform.forward);

			return Physics.Raycast(ray, out hit);
		}

		private bool Raycast(out GameObject hitObject)
		{
			bool didHit = Raycast(out RaycastHit hit);

			hitObject = hit.collider?.gameObject;

			return didHit;
		}

		private bool Raycast(out Vector3 hitPos)
		{
			bool didHit = Raycast(out RaycastHit hit);

			hitPos = hit.point;

			return didHit;
		}

		private static readonly Type[] blacklistedPreviewComponents =
		{
			typeof(MonoBehaviour), typeof(Animator), typeof(Collider), typeof(Rigidbody)
		};

		private static readonly Type[] whiteListedPreviewComponents =
		{
			typeof(TeamColorer), typeof(TeamOwner)
		};

		public static bool SpawnMapObject(MapObject prefab, Vector3 position,
			Quaternion rotation = default)
		{
			return LaserTagMapCoordinator.Instance != null &&
				LaserTagMapCoordinator.Instance.RequestPlaceObject(prefab, position, rotation);
		}

		private static GameObject InstantiateObjectAsPreview(MapObject obj)
		{
			GameObject preview = Instantiate(obj.Visuals);
			return preview;
		}

		#endregion
	}
}
