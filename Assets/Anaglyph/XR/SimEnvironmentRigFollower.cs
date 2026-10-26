using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Anaglyph.XR
{
	/// <summary>
	/// Editor-only: anchors XR Simulation's "physical reality" to the
	/// tracking space. The simulated environment stands in for the physical
	/// room, whose origin is the tracking space origin, so the environment
	/// roots keep their original poses relative to the tracking space no
	/// matter how the app moves it. The XR Interaction Simulator drives the
	/// head and copies the XR camera's world pose onto XR Simulation's
	/// camera, which renders the environment for passthrough and depth.
	/// Each headset's tracking space starts at a random world pose, so
	/// simulated players begin with mismatched coordinate spaces. Multiplayer
	/// Play Mode virtual players also start at a random spot and heading in
	/// the room (within poseOffsMax / poseYawMax of the main editor's start),
	/// so their scans cover the room differently. With SimTrackingDrift, the
	/// room follows its drifted frame instead, as a drifting tracker would see it.
	/// </summary>
	public class SimEnvironmentRigFollower : MonoBehaviour
	{
#if UNITY_EDITOR
		// runtime name used by ARFoundation's XR Simulation internals
		private const string SimCameraName = "SimulationCamera";

		[SerializeField] private float coordOffsMax = 10;
		[SerializeField] private float poseOffsMax = 2;
		[SerializeField] private float poseYawMax = 60;
		[SerializeField] private float headOffs = -1.7f;

		// the room relative to the tracking space: identity in the main editor
		private Matrix4x4 poseOffs = Matrix4x4.identity;

		private struct EnvRoot
		{
			public Transform transform;
			public Matrix4x4 original;
		}

		private readonly List<EnvRoot> envRoots = new();
		private Scene envScene;
		private Camera simCamera;

		// a private generator so nothing reseeding Unity's Random can give
		// Multiplayer Play Mode players the same origin
		private System.Random random = new(Guid.NewGuid().GetHashCode());

		/// <summary>When set before play starts, the random origin follows this seed: repeatable tests.</summary>
		public static int? SeedOverride;
		private bool randomized;

		private void OnEnable()
		{
			if (SeedOverride.HasValue) random = new System.Random(SeedOverride.Value);
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		// the room can't be parented to the rig: it lives in XR Simulation's own
		// scene, and moving it out would put its colliders in the game's physics.
		// Its only reader is the simulated camera's render, which XR Simulation
		// issues from an Update in no particular order, so re-follow the rig
		// right as that render starts. LateUpdate keeps the scene view current
		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (simCamera && camera == simCamera)
				Follow();
		}

		private void LateUpdate() => Follow();

		private void Follow()
		{
			if (!MainXRRig.Instance) return;

			Transform space = MainXRRig.TrackingSpace;
			Camera xrCamera = MainXRRig.Camera;
			if (!space || !xrCamera) return;

			if (!TrackEnvironmentScene()) return;

			if (!randomized)
			{
				RandomizeOrigin(space);
				if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
					poseOffs = RandomStartPose(space.InverseTransformPoint(xrCamera.transform.position));
				randomized = true;
			}

			// the room stays put in the tracker's (possibly drifted) frame
			Transform frame = SimTrackingDrift.Frame ? SimTrackingDrift.Frame : space;
			Matrix4x4 spaceMat = Matrix4x4.TRS(frame.position, frame.rotation, Vector3.one);

			foreach (EnvRoot root in envRoots)
			{
				if (!root.transform) continue;

				Matrix4x4 m = spaceMat * poseOffs * root.original;
				Vector3 pos = m.GetPosition() + Vector3.up * headOffs;
				Quaternion rot = m.rotation;

				root.transform.GetPositionAndRotation(out Vector3 curPos, out Quaternion curRot);
				if (curPos != pos || curRot != rot)
					root.transform.SetPositionAndRotation(pos, rot);
			}

			// the interaction simulator syncs this in its Update, which may run
			// before a tracking space move later in the frame
			Transform head = xrCamera.transform;
			simCamera.transform.SetPositionAndRotation(head.position, head.rotation);
		}

		private void OnDisable()
		{
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

			foreach (EnvRoot root in envRoots)
				if (root.transform)
					root.transform.SetPositionAndRotation(
						root.original.GetPosition(), root.original.rotation);

			envRoots.Clear();
			envScene = default;
		}

		private bool TrackEnvironmentScene()
		{
			if (!simCamera)
			{
				GameObject go = GameObject.Find(SimCameraName);
				if (!go || !go.TryGetComponent(out simCamera)) return false;
			}

			// XR Simulation restricts its camera to the environment scene,
			// whose name is internal and carries a fresh GUID per setup
			Scene scene = simCamera.scene;
			if (!scene.IsValid() || !scene.isLoaded) return false;
			if (scene == envScene) return true;

			envScene = scene;
			envRoots.Clear();

			foreach (GameObject root in scene.GetRootGameObjects())
			{
				Transform t = root.transform;
				envRoots.Add(new EnvRoot
				{
					transform = t,
					original = Matrix4x4.TRS(t.position, t.rotation, Vector3.one)
				});
			}

			return true;
		}

		private void RandomizeOrigin(Transform space)
		{
			Quaternion rot = Quaternion.Euler(0f, Range(0f, 360f), 0f);
			Vector3 offset = new(
				Range(-coordOffsMax, coordOffsMax),
				Range(-coordOffsMax, coordOffsMax),
				Range(-coordOffsMax, coordOffsMax));

			space.SetPositionAndRotation(offset, rot);
		}

		// turns the room about the head's starting spot and slides it, so the head
		// starts somewhere else in it, facing another way
		private Matrix4x4 RandomStartPose(Vector3 head)
		{
			head.y = 0;
			Vector3 slide = new(Range(-poseOffsMax, poseOffsMax), 0, Range(-poseOffsMax, poseOffsMax));
			Quaternion turn = Quaternion.Euler(0, Range(-poseYawMax, poseYawMax), 0);
			return Matrix4x4.Translate(head + slide) * Matrix4x4.Rotate(turn) * Matrix4x4.Translate(-head);
		}

		private float Range(float min, float max) =>
			min + (float)random.NextDouble() * (max - min);
#endif
	}
}
