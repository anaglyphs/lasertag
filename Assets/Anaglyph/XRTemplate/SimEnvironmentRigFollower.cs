using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Anaglyph.XRTemplate
{
	/// <summary>
	/// Editor-only: anchors XR Simulation's "physical reality" to the
	/// tracking space. XR Simulation keeps the simulated environment and
	/// the simulated device pose in raw session coordinates, so when the
	/// app moves the tracking space, both stay behind in world space — as
	/// if the player teleported inside the room. In reality, moving the
	/// rig never changes the player's relationship to the physical room.
	/// This moves the environment roots and the simulated device together
	/// with the tracking space, preserving that relationship.
	/// </summary>
	public class SimEnvironmentRigFollower : MonoBehaviour
	{
#if UNITY_EDITOR
		// runtime names used by ARFoundation's XR Simulation internals
		private const string EnvScenePrefix = "Simulated Environment Scene";
		private const string SimCameraName = "SimulationCamera";
		private const string ProxyName = "Simulation Camera Rig Anchor";

		private struct EnvRoot
		{
			public Transform transform;
			public Matrix4x4 original;
		}

		private readonly List<EnvRoot> envRoots = new();
		private Scene envScene;
		private Transform simCamera;
		private Transform camProxy;

		// emulated tracking drift: the room's session-space pose shift
		private Matrix4x4 drift = Matrix4x4.identity;

		private void LateUpdate()
		{
			if (!MainXRRig.Instance) return;

			Transform space = MainXRRig.TrackingSpace;
			if (!space) return;

			TrackEnvironmentScene();
			TrackSimulationCamera(space);

			if (camProxy)
				camProxy.SetPositionAndRotation(space.position, space.rotation);

			Matrix4x4 spaceMat = Matrix4x4.TRS(space.position, space.rotation, Vector3.one);

			foreach (EnvRoot root in envRoots)
			{
				if (!root.transform) continue;

				Matrix4x4 m = spaceMat * drift * root.original;
				root.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
			}
		}

		private void OnDisable()
		{
			foreach (EnvRoot root in envRoots)
				if (root.transform)
					root.transform.SetPositionAndRotation(
						root.original.GetPosition(), root.original.rotation);

			envRoots.Clear();
			envScene = default;
		}

		private void TrackEnvironmentScene()
		{
			if (envScene.IsValid() && envScene.isLoaded) return;

			envRoots.Clear();
			envScene = default;

			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (!scene.isLoaded || !scene.name.StartsWith(EnvScenePrefix)) continue;

				envScene = scene;

				foreach (GameObject root in scene.GetRootGameObjects())
				{
					Transform t = root.transform;
					envRoots.Add(new EnvRoot
					{
						transform = t,
						original = Matrix4x4.TRS(t.position, t.rotation, Vector3.one)
					});
				}

				break;
			}
		}

		private void TrackSimulationCamera(Transform space)
		{
			if (simCamera && camProxy && simCamera.parent == camProxy) return;

			if (!simCamera)
			{
				GameObject go = GameObject.Find(SimCameraName);
				if (!go) return;
				simCamera = go.transform;
			}

			if (!camProxy)
			{
				GameObject proxy = GameObject.Find(ProxyName);
				if (!proxy)
				{
					proxy = new GameObject(ProxyName);
					// the simulated device must stay in the DontDestroyOnLoad
					// scene, so its parent has to live there too
					DontDestroyOnLoad(proxy);
				}

				camProxy = proxy.transform;
			}

			if (simCamera.parent != camProxy)
			{
				camProxy.SetPositionAndRotation(space.position, space.rotation);

				// keep the local pose: it is the session-space device pose
				// that SimulationCameraPoseProvider pushes to XR input
				simCamera.SetParent(camProxy, false);
			}
		}

		// real drift shifts the device pose estimate without changing what
		// the player sees. emulate it by moving the room and the simulated
		// device together: the view stays put, session coordinates shift,
		// and the corrector should pull the rig back into alignment
		[ContextMenu("Inject test drift")]
		private void InjectTestDrift()
		{
			Quaternion rot = Quaternion.Euler(0f, 1.5f, 0f);
			Vector3 offset = new(0.15f, 0.03f, -0.1f);

			drift = Matrix4x4.TRS(offset, rot, Vector3.one) * drift;

			if (simCamera)
				simCamera.SetLocalPositionAndRotation(
					rot * simCamera.localPosition + offset,
					rot * simCamera.localRotation);
		}

		[ContextMenu("Reset test drift")]
		private void ResetTestDrift()
		{
			if (simCamera)
			{
				Matrix4x4 inv = drift.inverse;
				simCamera.SetLocalPositionAndRotation(
					inv.MultiplyPoint3x4(simCamera.localPosition),
					inv.rotation * simCamera.localRotation);
			}

			drift = Matrix4x4.identity;
		}
#endif
	}
}
