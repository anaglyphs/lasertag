using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Anaglyph.XR;
using Anaglyph.XR.DepthKit.EnvScanning;
using Anaglyph.XR.Input;
using Anaglyph.XR.SharedSpaces;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using Utilities.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;
using InputDevice = UnityEngine.XR.InputDevice;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// A debugging tool that measures how far the virtual world sits from the physical one at a
	/// distance, which the headset can't know by itself. Touch a controller's tip to a sharp
	/// physical spot to place a marker; walk away, line the same spot up in a one-eyed crosshair
	/// and press. The bearing between the crosshair and the marker is the error seen from there.
	///
	/// Each marker is kept both in the shared frame and in tracking space, so one sighting scores
	/// the drift-corrected alignment and raw tracking under the same walk. A marker's sightings
	/// soon after placing it read the crosshair's own bias, and are subtracted from later ones.
	/// Results append to <see cref="EnvScan.DefaultDirectory"/>/sightings/*.jsonl.
	/// </summary>
	public class AlignmentSightingTool : MonoBehaviour
	{
		public enum ToolMode { Off, Place, Sight }

		private const string FireAction = "OnFire";
		private const float CrosshairDistance = 5f;
		private const float CrosshairArmDegrees = 0.8f;
		private const float CrosshairGapDegrees = 0.2f;
		private const float CrosshairThicknessDegrees = 0.08f;
		// a marker this far off the crosshair isn't the one being sighted
		private const float MaxSightDegrees = 15f;
		// sightings this soon after placing a marker measure the crosshair, not drift
		private const float ZeroWalk = 5f;
		private const float ZeroMinDistance = 1.5f;
		private const float RevealSeconds = 4f;
		private static readonly Color MarkerColor = new(1f, 0.8f, 0.2f);
		private static readonly Color AimColor = new(0.2f, 1f, 0.3f);

		private sealed class Marker
		{
			public int id;
			public Vector3 tracking;
			public Vector3 corrected;
			public int epoch;
			public float path;
			public float correctedZeroSum, rawZeroSum;
			public int correctedZeros, rawZeros;
		}

		[Serializable]
		private struct Record
		{
			public string kind;
			public float time;
			public int marker;
			public float walked, walkedSincePlaced, distance;
			public float correctedDegrees, correctedCm, correctedZeroedDegrees, correctedZeroedCm;
			public bool rawValid;
			public float rawDegrees, rawCm, rawZeroedDegrees, rawZeroedCm;
			public bool zeroing, showingRaw, rightEye;
			public int sites, generation, epoch;
			public Vector3 tracking, corrected, eyeTracking, aimTracking;
			// tracking space → shared frame when it was taken: the correction in force, without a replay
			public Vector3 targetPosition;
			public float targetYawDegrees;
		}

		private static AlignmentSightingTool instance;
		private static ToolMode mode;
		private static bool showRawTracking;
		private static bool crosshairInRightEye = true;

		public static event Action Changed = delegate { };

		/// <summary>The latest thing the tool did, for the settings page.</summary>
		public static string Status { get; private set; } = "";

		public static ToolMode Mode
		{
			get => mode;
			set
			{
				mode = value;
				Ensure();
				Changed.Invoke();
			}
		}

		/// <summary>Whether the rig shows raw tracking rather than the drift-corrected alignment. Sightings score both either way.</summary>
		public static bool ShowRawTracking
		{
			get => showRawTracking;
			set
			{
				showRawTracking = value;
				Ensure();
				Changed.Invoke();
			}
		}

		public static bool CrosshairInRightEye
		{
			get => crosshairInRightEye;
			set
			{
				crosshairInRightEye = value;
				Changed.Invoke();
			}
		}

		public static void ClearMarkers()
		{
			if (instance == null) return;
			instance.markers.Clear();
			instance.revealUntil = 0;
			SetStatus("Markers cleared");
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			instance = null;
			mode = ToolMode.Off;
			showRawTracking = false;
			crosshairInRightEye = true;
			Status = "";
			Changed = delegate { };
		}

		private static void Ensure()
		{
			if (instance != null) return;
			GameObject go = new(nameof(AlignmentSightingTool));
			DontDestroyOnLoad(go);
			instance = go.AddComponent<AlignmentSightingTool>();
		}

		private static void SetStatus(string status)
		{
			Status = status;
			Changed.Invoke();
		}

		private readonly List<Marker> markers = new();
		private readonly List<Vector3> markerPositions = new();
		private int nextId = 1;
		private string logPath;

		private GameObject crosshair;
		private Material crosshairMaterial;
		private Mesh crosshairMesh;

		private Marker revealed;
		private Vector3 revealAimEye, revealAimDirection;
		private string revealText;
		private float revealUntil;

		private void OnDestroy()
		{
			if (crosshair != null) Destroy(crosshair);
			if (crosshairMaterial != null) Destroy(crosshairMaterial);
			if (crosshairMesh != null) Destroy(crosshairMesh);
		}

		private void Update()
		{
			VisualColocationConstraintProvider provider = VisualColocationConstraintProvider.Instance;
			if (provider != null) provider.ShowRawTracking = showRawTracking;

			UpdateCrosshair();
			if (mode == ToolMode.Off || !TryPressedHand(out HandInput hand)) return;

			if (provider == null || provider.Alignment == null || provider.Alignment.CurrentPhase != CanonAlignment.Phase.Aligned || MainXRRig.Instance == null)
			{
				SetStatus("Not aligned by visual colocation");
				return;
			}

			if (mode == ToolMode.Place) Place(provider, hand);
			else Sight(provider);
		}

		private void LateUpdate()
		{
			if (MainXRRig.Instance == null) return;

			// hidden while aiming, so the virtual marker can't lead the eye
			if (mode == ToolMode.Place) DrawPlacing();
			if (Time.unscaledTime < revealUntil && revealed != null) DrawReveal();
		}

		private static bool TryPressedHand(out HandInput pressed)
		{
			foreach (HandInput hand in HandInput.Hands.Values)
			{
				if (hand == null || hand.InputBlocked) continue;
				InputAction fire = hand.Actions.FindAction(FireAction);
				if (fire == null || !fire.WasPressedThisFrame()) continue;
				pressed = hand;
				return true;
			}

			pressed = null;
			return false;
		}

		// ---- placing -----------------------------------------------------------

		private void Place(VisualColocationConstraintProvider provider, HandInput hand)
		{
			if (!hand.IsTracking)
			{
				SetStatus("That controller isn't tracking");
				return;
			}

			float path = HeadPoseHistory.Instance != null ? HeadPoseHistory.Instance.Path : 0f;
			Vector3 tracking = hand.PointPosition;
			Marker marker = new()
			{
				id = nextId++,
				tracking = tracking,
				corrected = Apply(provider.Alignment.TargetSpacePose, tracking),
				epoch = provider.Alignment.FrameEpoch,
				path = path,
			};
			markers.Add(marker);

			Write(new Record
			{
				kind = "place", marker = marker.id, tracking = tracking, corrected = marker.corrected, walked = path,
				targetPosition = provider.Alignment.TargetSpacePose.position, targetYawDegrees = provider.Alignment.TargetSpacePose.rotation.eulerAngles.y,
				showingRaw = showRawTracking, rightEye = crosshairInRightEye, sites = provider.Sites.Count,
				generation = provider.Alignment.Generation, epoch = marker.epoch, time = Time.realtimeSinceStartup,
			});
			SetStatus($"Placed marker {marker.id}");
		}

		private void DrawPlacing()
		{
			Transform space = MainXRRig.TrackingSpace;
			foreach (HandInput hand in HandInput.Hands.Values)
				if (hand != null && hand.IsTracking)
					XRGizmos.DrawPoint(space.TransformPoint(hand.PointPosition), AimColor, 0.02f);

			markerPositions.Clear();
			foreach (Marker marker in markers)
			{
				Vector3 at = Shown(marker);
				markerPositions.Add(at);
				DrawLabel(marker.id.ToString(CultureInfo.InvariantCulture), at + Vector3.up * 0.06f, at, 0.04f, MarkerColor);
			}

			if (markerPositions.Count > 0) XRGizmos.DrawPointSet(markerPositions, MarkerColor, 0.04f);
		}

		// ---- sighting ----------------------------------------------------------

		private void Sight(VisualColocationConstraintProvider provider)
		{
			Transform head = MainXRRig.Camera.transform;
			Transform space = MainXRRig.TrackingSpace;
			Vector3 eyeTracking = space.InverseTransformPoint(head.TransformPoint(EyeInHead()));
			Vector3 aimTracking = space.InverseTransformDirection(head.forward);

			Pose target = provider.Alignment.TargetSpacePose;
			Vector3 eyeCorrected = Apply(target, eyeTracking);
			Vector3 aimCorrected = target.rotation * aimTracking;

			Marker marker = null;
			float nearest = MaxSightDegrees;
			foreach (Marker candidate in markers)
			{
				float off = Mathf.Abs(Bearing(aimCorrected, candidate.corrected - eyeCorrected));
				if (off >= nearest) continue;
				nearest = off;
				marker = candidate;
			}

			if (marker == null)
			{
				SetStatus($"No marker within {MaxSightDegrees:F0}° of the crosshair");
				return;
			}

			float path = HeadPoseHistory.Instance != null ? HeadPoseHistory.Instance.Path : 0f;
			float walked = path - marker.path;
			Vector3 toCorrected = marker.corrected - eyeCorrected;
			float distance = new Vector2(toCorrected.x, toCorrected.z).magnitude;

			float corrected = Bearing(aimCorrected, toCorrected);
			// a recenter leaves the marker's tracking-space position describing a frame that is gone
			bool rawValid = marker.epoch == provider.Alignment.FrameEpoch;
			float raw = rawValid ? Bearing(aimTracking, marker.tracking - eyeTracking) : 0f;

			bool zeroing = walked < ZeroWalk && distance >= ZeroMinDistance;
			if (zeroing)
			{
				marker.correctedZeroSum += corrected;
				marker.correctedZeros++;
				if (rawValid)
				{
					marker.rawZeroSum += raw;
					marker.rawZeros++;
				}
			}

			float correctedZeroed = corrected - (marker.correctedZeros > 0 ? marker.correctedZeroSum / marker.correctedZeros : 0f);
			float rawZeroed = raw - (marker.rawZeros > 0 ? marker.rawZeroSum / marker.rawZeros : 0f);

			Write(new Record
			{
				kind = "sight", time = Time.realtimeSinceStartup, marker = marker.id, walked = path, walkedSincePlaced = walked, distance = distance,
				correctedDegrees = corrected, correctedCm = Centimeters(corrected, distance),
				correctedZeroedDegrees = correctedZeroed, correctedZeroedCm = Centimeters(correctedZeroed, distance),
				rawValid = rawValid, rawDegrees = raw, rawCm = Centimeters(raw, distance),
				rawZeroedDegrees = rawZeroed, rawZeroedCm = Centimeters(rawZeroed, distance),
				zeroing = zeroing, showingRaw = showRawTracking, rightEye = crosshairInRightEye,
				sites = provider.Sites.Count, generation = provider.Alignment.Generation, epoch = provider.Alignment.FrameEpoch,
				tracking = marker.tracking, corrected = marker.corrected, eyeTracking = eyeTracking, aimTracking = aimTracking,
				targetPosition = target.position, targetYawDegrees = target.rotation.eulerAngles.y,
			});

			string rawText = rawValid ? $"{rawZeroed:+0.00;-0.00}° {Centimeters(rawZeroed, distance):+0;-0} cm" : "invalid (recentered)";
			SetStatus($"Marker {marker.id}{(zeroing ? " (zeroing)" : "")}, {distance:F1} m away, {walked:F0} m walked\n" +
			          $"Corrected {correctedZeroed:+0.00;-0.00}° {Centimeters(correctedZeroed, distance):+0;-0} cm\nRaw {rawText}");

			revealed = marker;
			revealAimEye = head.TransformPoint(EyeInHead());
			revealAimDirection = head.forward;
			revealText = $"{marker.id} {distance:F1}M C{correctedZeroed:+0.00;-0.00} R{(rawValid ? rawZeroed.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "-")}{(zeroing ? " Z" : "")}";
			revealUntil = Time.unscaledTime + RevealSeconds;
		}

		// where the virtual marker stands, against where the crosshair said it should
		private void DrawReveal()
		{
			Vector3 at = Shown(revealed);
			float along = Mathf.Max(Vector3.Dot(at - revealAimEye, revealAimDirection), 0.1f);
			Vector3 aimed = revealAimEye + revealAimDirection * along;
			XRGizmos.DrawPoint(at, MarkerColor, 0.1f);
			XRGizmos.DrawPoint(aimed, AimColor, 0.1f);
			XRGizmos.DrawLine(at, aimed, Color.white);

			Vector3 near = revealAimEye + revealAimDirection * 2f + Vector3.up * 0.25f;
			DrawLabel(revealText, near, near, 0.05f, Color.white);
		}

		// the marker as the rig on show places it
		private static Vector3 Shown(Marker marker) =>
			showRawTracking ? MainXRRig.TrackingSpace.TransformPoint(marker.tracking) : marker.corrected;

		// degrees about up from the aim to a point's direction, positive clockwise from above
		private static float Bearing(Vector3 aim, Vector3 to)
		{
			aim.y = to.y = 0;
			return Vector3.SignedAngle(aim, to, Vector3.up);
		}

		private static float Centimeters(float degrees, float distance) => Mathf.Sin(degrees * Mathf.Deg2Rad) * distance * 100f;

		private static Vector3 Apply(in Pose pose, Vector3 point) => pose.position + pose.rotation * point;

		private static void DrawLabel(string text, Vector3 at, Vector3 faceFrom, float size, Color color)
		{
			Vector3 toHead = MainXRRig.Camera.transform.position - faceFrom;
			toHead.y = 0;
			if (toHead.sqrMagnitude < 1e-4f) return;
			XRGizmos.DrawString(text, at, Quaternion.LookRotation(-toHead, Vector3.up), color, size, size);
		}

		// ---- the crosshair -----------------------------------------------------

		private void UpdateCrosshair()
		{
			bool show = mode == ToolMode.Sight && MainXRRig.Instance != null;
			if (!show)
			{
				if (crosshair != null) crosshair.SetActive(false);
				return;
			}

			if (crosshair == null && !BuildCrosshair()) return;

			Transform head = MainXRRig.Camera.transform;
			if (crosshair.transform.parent != head) crosshair.transform.SetParent(head, false);
			crosshair.transform.localPosition = EyeInHead() + Vector3.forward * CrosshairDistance;
			crosshair.transform.localRotation = Quaternion.identity;
			crosshairMaterial.SetFloat("_Eye", crosshairInRightEye ? 1 : 0);
			crosshair.SetActive(true);
		}

		private bool BuildCrosshair()
		{
			Shader shader = Resources.Load<Shader>("SightingCrosshair");
			if (shader == null) return false;

			float Size(float degrees) => Mathf.Tan(degrees * Mathf.Deg2Rad) * CrosshairDistance;
			float gap = Size(CrosshairGapDegrees), arm = gap + Size(CrosshairArmDegrees), half = Size(CrosshairThicknessDegrees) / 2;

			List<Vector3> vertices = new();
			List<int> triangles = new();
			void Bar(float x0, float y0, float x1, float y1)
			{
				int start = vertices.Count;
				vertices.Add(new Vector3(x0, y0));
				vertices.Add(new Vector3(x1, y0));
				vertices.Add(new Vector3(x1, y1));
				vertices.Add(new Vector3(x0, y1));
				triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
			}

			Bar(gap, -half, arm, half);
			Bar(-arm, -half, -gap, half);
			Bar(-half, gap, half, arm);
			Bar(-half, -arm, half, -gap);

			crosshairMesh = new Mesh { name = "Sighting Crosshair" };
			crosshairMesh.SetVertices(vertices);
			crosshairMesh.SetTriangles(triangles, 0);
			crosshairMesh.RecalculateBounds();
			crosshairMaterial = new Material(shader);

			crosshair = new GameObject("Sighting Crosshair");
			crosshair.AddComponent<MeshFilter>().sharedMesh = crosshairMesh;
			MeshRenderer renderer = crosshair.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = crosshairMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			return true;
		}

		// the eye the crosshair is drawn for, relative to the head: at far range the crosshair lines
		// up from that eye, and at the zeroing distance half an IPD is over half a degree
		private static Vector3 EyeInHead()
		{
			if (!XRSettings.isDeviceActive) return Vector3.zero;

			InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.Head);
			InputFeatureUsage<Vector3> usage = crosshairInRightEye ? CommonUsages.rightEyePosition : CommonUsages.leftEyePosition;
			if (device.TryGetFeatureValue(usage, out Vector3 eye) &&
			    device.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 center) &&
			    device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation))
				return Quaternion.Inverse(rotation) * (eye - center);

			return new Vector3(crosshairInRightEye ? 0.032f : -0.032f, 0, 0);
		}

		// ---- the log -----------------------------------------------------------

		private void Write(in Record record)
		{
			try
			{
				if (logPath == null)
				{
					string folder = Path.Combine(EnvScan.DefaultDirectory, "sightings");
					Directory.CreateDirectory(folder);
					logPath = Path.Combine(folder, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".jsonl");
				}

				File.AppendAllText(logPath, JsonUtility.ToJson(record) + "\n");
			}
			catch (IOException e)
			{
				Debug.LogException(e, this);
			}
		}
	}
}
