#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Anaglyph.XR;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Walks the simulated headset around the XR Simulation environment on its baked
	/// nav-mesh, for unattended tests. It moves the head only: the XR Interaction
	/// Simulator's HMD state is written before the simulator applies it each frame, so the
	/// head moves in device space exactly as a tracked head would, while the rig root stays
	/// with colocation and <see cref="SimTrackingDrift"/>, and the room keeps following it.
	///
	/// Routes are planned on the nav-mesh in world space and stored relative to the room,
	/// so they move with it as drift and corrections move the room. The head turns toward
	/// where it is going at a human rate, glances around, and looks about at each stop;
	/// fast turns trip the localizer's motion-blur guard, as real heads do.
	/// </summary>
	[DefaultExecutionOrder(-32000)] // before the XR Interaction Simulator (-29991) applies its HMD state
	public class SimWalker : MonoBehaviour
	{
		public static SimWalker Instance { get; private set; }

		[Header("Motion")]
		public float walkSpeed = 1.2f;
		public float eyeHeight = 1.6f;
		[Tooltip("Fastest the head turns, degrees per second")]
		public float maxTurnRate = 120f;
		[Tooltip("How quickly the head turns toward its target heading (1/s)")]
		public float turnResponse = 4f;
		[Tooltip("Mean seconds between glances while walking")]
		public float glanceInterval = 3f;
		public float glanceAngle = 45f;
		[Tooltip("Seconds spent looking around at each stop")]
		public float lookAroundSeconds = 4f;
		[Tooltip("Head shake while moving: degrees of jitter, as a running head bobs and darts")]
		public float shakeDegrees;

		/// <summary>
		/// Moves the way a player in a game does, knowing nothing of mapping: runs, darts the
		/// head around often and fast, never stops to look around.
		/// </summary>
		public void PlayLikeAPlayer()
		{
			walkSpeed = 3f;
			maxTurnRate = 300f;
			turnResponse = 10f;
			glanceInterval = 1.2f;
			glanceAngle = 70f;
			lookAroundSeconds = 0;
			shakeDegrees = 5f;
		}

		/// <summary>Raised on arriving at each stop of a route, with its index.</summary>
		public event Action<int> Arrived = delegate { };

		public bool IsMoving => legs.Count > 0 || lookAroundLeft > 0;
		public int StopsReached { get; private set; }
		public float DistanceWalked { get; private set; }
		public string Status { get; private set; } = "idle";

		private object simulator;
		private FieldInfo hmdStateField, centerEyeEulerField;
		private Transform room;

		// the route, as stops in room coordinates, each with the corners leading to it
		private readonly Queue<List<Vector3>> legs = new();
		private int stopIndex;

		// the head, in room coordinates (where the walking happens) and its heading
		private Vector3 position;
		private float yaw, pitch;
		private bool placed;
		private float glanceYaw, glanceUntil, nextGlance;
		private float lookAroundLeft, lookAroundYaw;
		private float glancePitch;
		private readonly Random random = new(12345);

		private void Awake() => Instance = this;

		private void OnDestroy()
		{
			if (Instance == this) Instance = null;
		}

		// ---- the room and the head -----------------------------------------------

		/// <summary>The XR Simulation environment's root: it moves rigidly with the simulated room.</summary>
		private Transform Room => room ? room : room = FindRoom();

		/// <summary>
		/// The root of the environment that carries the baked nav-mesh. Found by its
		/// NavMeshSurface, not by the simulation camera's scene: that camera can sit in
		/// another scene (DontDestroyOnLoad), and it follows the head.
		/// </summary>
		public static Transform FindRoom()
		{
			Type surface = Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
			if (surface == null) return null;
			UnityEngine.Object found = FindFirstObjectByType(surface);
			return found is Component c ? c.transform.root : null;
		}

		private bool Bind()
		{
			if (simulator != null) return true;
			Type type = Type.GetType("UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator, Unity.XR.Interaction.Toolkit");
			if (type == null) return false;
			simulator = FindFirstObjectByType(type);
			if (simulator == null) return false;
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			hmdStateField = type.GetField("m_HMDState", flags);
			centerEyeEulerField = type.GetField("m_CenterEyeEuler", flags);
			return hmdStateField != null && centerEyeEulerField != null;
		}

		// the head's world position now, from the camera
		private static Vector3 HeadWorld => MainXRRig.Camera.transform.position;

		// ---- commands ---------------------------------------------------------

		/// <summary>Walks to a world point (snapped to the nav-mesh), looking around on arrival.</summary>
		public bool GoTo(Vector3 world) => Route(new[] { world });

		/// <summary>Walks through these world points in order, looking around at each.</summary>
		public bool Route(IReadOnlyList<Vector3> worldStops)
		{
			if (!Bind() || Room == null || MainXRRig.Instance == null)
			{
				Status = "not ready: no simulator, room or rig";
				return false;
			}

			Place();
			legs.Clear();
			stopIndex = 0;
			lookAroundLeft = 0;

			Vector3 from = Room.TransformPoint(position);
			foreach (Vector3 stop in worldStops)
			{
				if (!NavMesh.SamplePosition(from, out NavMeshHit start, 3f, NavMesh.AllAreas) ||
				    !NavMesh.SamplePosition(stop, out NavMeshHit end, 3f, NavMesh.AllAreas))
				{
					Status = $"no nav-mesh near {(NavMesh.SamplePosition(from, out _, 3f, NavMesh.AllAreas) ? stop : from)}";
					legs.Clear();
					return false;
				}

				NavMeshPath path = new();
				if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathInvalid)
				{
					Status = $"no path to {stop}";
					legs.Clear();
					return false;
				}

				List<Vector3> corners = new();
				foreach (Vector3 c in path.corners) corners.Add(Room.InverseTransformPoint(c + Vector3.up * eyeHeight));
				legs.Enqueue(corners);
				from = end.position;
			}

			Status = $"walking a route of {worldStops.Count} stops";
			return true;
		}

		/// <summary>A reachable nav-mesh point at least <paramref name="minDistance"/> from the head, chosen at random.</summary>
		public bool RandomReachable(float minDistance, out Vector3 world)
		{
			world = default;
			NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
			if (tri.vertices.Length == 0 || MainXRRig.Instance == null) return false;
			Vector3 head = HeadWorld;
			for (int attempt = 0; attempt < 64; attempt++)
			{
				int t = random.Next(tri.indices.Length / 3) * 3;
				float a = (float)random.NextDouble(), b = (float)random.NextDouble();
				if (a + b > 1) { a = 1 - a; b = 1 - b; }
				Vector3 p = tri.vertices[tri.indices[t]] + a * (tri.vertices[tri.indices[t + 1]] - tri.vertices[tri.indices[t]]) +
				            b * (tri.vertices[tri.indices[t + 2]] - tri.vertices[tri.indices[t]]);
				Vector3 flat = p - head;
				flat.y = 0;
				if (flat.magnitude < minDistance) continue;
				NavMeshPath path = new();
				if (!NavMesh.SamplePosition(head, out NavMeshHit start, 3f, NavMesh.AllAreas)) return false;
				if (!NavMesh.CalculatePath(start.position, p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
				world = p;
				return true;
			}

			return false;
		}

		public void Stop()
		{
			legs.Clear();
			lookAroundLeft = 0;
			Status = "stopped";
		}

		// ---- motion -----------------------------------------------------------

		// takes over from wherever the head is now
		private void Place()
		{
			if (placed || Room == null) return;
			Transform head = MainXRRig.Camera.transform;
			position = Room.InverseTransformPoint(head.position);
			Vector3 forward = Room.InverseTransformDirection(head.forward);
			yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
			pitch = 0;
			placed = true;
		}

		private void Update()
		{
			if (!placed || !Bind() || Room == null || MainXRRig.Instance == null) return;

			// never steer a head that has left the building: something has gone wrong
			if (!NavMesh.SamplePosition(Room.TransformPoint(position), out _, 10f, NavMesh.AllAreas))
			{
				Status = "stopped: the head is nowhere near the nav-mesh";
				legs.Clear();
				lookAroundLeft = 0;
				placed = false;
				return;
			}

			float dt = Time.deltaTime;
			float targetYaw = yaw;
			float targetPitch = 0;

			if (legs.Count > 0)
			{
				List<Vector3> corners = legs.Peek();
				// drop corners already reached
				while (corners.Count > 0 && Flat(corners[0] - position).magnitude < 0.05f) corners.RemoveAt(0);

				if (corners.Count == 0)
				{
					legs.Dequeue();
					StopsReached++;
					lookAroundLeft = lookAroundSeconds;
					lookAroundYaw = yaw;
					Arrived.Invoke(stopIndex++);
					Status = legs.Count > 0 ? $"reached stop {stopIndex}, {legs.Count} to go" : "arrived";
				}
				else if (lookAroundLeft <= 0)
				{
					Vector3 toward = Flat(corners[0] - position);
					float step = Mathf.Min(walkSpeed * dt, toward.magnitude);
					position += toward.normalized * step;
					position.y = Mathf.Lerp(position.y, corners[0].y, dt * 2f);
					DistanceWalked += step;
					targetYaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;

					// glances: a look to the side every few seconds, as people walking do
					if (Time.time >= nextGlance)
					{
						glanceYaw = ((float)random.NextDouble() * 2 - 1) * glanceAngle;
						glanceUntil = Time.time + 0.6f + (float)random.NextDouble();
						// players look about at eye level: a glance dips a little more often than it rises
						glancePitch = shakeDegrees > 0 ? Mathf.Lerp(-5f, 10f, (float)random.NextDouble()) : 0;
						if (shakeDegrees > 0) glanceUntil -= 0.3f;
						nextGlance = glanceUntil + glanceInterval * (0.5f + (float)random.NextDouble());
					}

					if (Time.time < glanceUntil) targetYaw += glanceYaw;
					targetPitch = Mathf.Sin(Time.time * 0.7f) * (shakeDegrees > 0 ? 4f : 8f);
					if (Time.time < glanceUntil) targetPitch += glancePitch;
				}
			}

			if (lookAroundLeft > 0)
			{
				// a slow full turn with some looking up and down
				lookAroundLeft -= dt;
				float t = 1 - lookAroundLeft / Mathf.Max(lookAroundSeconds, 0.01f);
				targetYaw = lookAroundYaw + t * 360f;
				targetPitch = Mathf.Sin(t * Mathf.PI * 4) * 15f;
				if (lookAroundLeft <= 0 && legs.Count == 0) Status = "arrived";
			}

			// turn at a human rate toward the target heading
			float yawError = Mathf.DeltaAngle(yaw, targetYaw);
			float turn = Mathf.Clamp(yawError * turnResponse, -maxTurnRate, maxTurnRate) * dt;
			yaw += Mathf.Abs(turn) > Mathf.Abs(yawError) ? yawError : turn;
			pitch = Mathf.MoveTowards(pitch, targetPitch, (shakeDegrees > 0 ? 200f : 60f) * dt);

			// the jitter a running head has on top of where it is looking: a few hertz, a few degrees, and the stride's bob
			float shakeYaw = 0, shakePitch = 0;
			if (shakeDegrees > 0 && legs.Count > 0)
			{
				float t = Time.time;
				shakeYaw = shakeDegrees * (Mathf.PerlinNoise(t * 3f, 0.5f) * 2 - 1);
				shakePitch = shakeDegrees * (Mathf.PerlinNoise(0.5f, t * 3.7f) * 2 - 1) + 0.4f * shakeDegrees * Mathf.Sin(t * 2 * Mathf.PI * 2.6f);
			}

			Apply(yaw + shakeYaw, pitch + shakePitch);
		}

		private static Vector3 Flat(Vector3 v)
		{
			v.y = 0;
			return v;
		}

		// the head, from room coordinates into the simulator's device space (the camera's parent)
		private void Apply(float headYaw, float headPitch)
		{
			Transform cameraParent = MainXRRig.Camera.transform.parent;
			Vector3 world = Room.TransformPoint(position);
			Quaternion worldRotation = Room.rotation * Quaternion.Euler(headPitch, headYaw, 0);
			Vector3 device = cameraParent ? cameraParent.InverseTransformPoint(world) : world;
			Quaternion deviceRotation = cameraParent ? Quaternion.Inverse(cameraParent.rotation) * worldRotation : worldRotation;

			object state = hmdStateField.GetValue(simulator);
			Type type = state.GetType();
			type.GetField("centerEyePosition").SetValue(state, device);
			type.GetField("devicePosition").SetValue(state, device);
			type.GetField("centerEyeRotation").SetValue(state, deviceRotation);
			type.GetField("deviceRotation").SetValue(state, deviceRotation);
			hmdStateField.SetValue(simulator, state);
			centerEyeEulerField.SetValue(simulator, deviceRotation.eulerAngles);
		}
	}
}
#endif
