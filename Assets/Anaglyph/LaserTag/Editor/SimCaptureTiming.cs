#if UNITY_EDITOR
using System.Collections.Generic;
using Anaglyph.XR;
using UnityEngine;
using UnityEngine.Rendering;

namespace Anaglyph.LaserTag.Editor
{
	/// <summary>
	/// Measures how far the pose the localizer gives a simulated camera image is from where
	/// that image was really rendered. XR Simulation renders its camera from an Update and
	/// stamps the image with that frame's Time.time; the localizer looks the camera up in
	/// <see cref="HeadPoseHistory"/> at that stamp. Any difference between the two is error
	/// mapping and loop closure inherit, and it grows with how fast the head moves.
	///
	/// Both poses are compared in the drift frame (the room), where the render really was.
	/// Also tried at a frame earlier and later, to show which way an offset lies.
	/// </summary>
	public class SimCaptureTiming : MonoBehaviour
	{
		private const string SimCameraName = "SimulationCamera";

		private struct Shot
		{
			public long timestampNs;
			public float dt;
			public Vector3 position;
			public Quaternion rotation;
			public Matrix4x4 spaceFromFrame;
		}

		private readonly Queue<Shot> shots = new();
		private readonly Stats[] byOffset = { new(), new(), new() }; // a frame early, as stamped, a frame late
		private readonly Stats corrected = new(); // as PassthroughCapture looks it up in the editor

		private class Stats
		{
			public int count;
			public double sumTranslation, sumAngle;
			public float maxTranslation, maxAngle;

			public void Add(float t, float a)
			{
				count++;
				sumTranslation += t;
				sumAngle += a;
				maxTranslation = Mathf.Max(maxTranslation, t);
				maxAngle = Mathf.Max(maxAngle, a);
			}

			public override string ToString() => count == 0
				? "no samples"
				: $"{sumTranslation / count * 100:F1} cm / {sumAngle / count:F2}° mean, {maxTranslation * 100:F1} cm / {maxAngle:F2}° max";
		}

		private void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		private void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

		// after SimEnvironmentRigFollower has placed the room and the simulation camera for this render
		private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			if (camera.name != SimCameraName || MainXRRig.Instance == null) return;
			Transform space = MainXRRig.TrackingSpace;
			Transform frame = SimTrackingDrift.Frame ? SimTrackingDrift.Frame : space;
			shots.Enqueue(new Shot
			{
				timestampNs = (long)(Time.time * 1e9),
				dt = Time.deltaTime,
				position = frame.InverseTransformPoint(camera.transform.position),
				rotation = Quaternion.Inverse(frame.rotation) * camera.transform.rotation,
				spaceFromFrame = space.worldToLocalMatrix * frame.localToWorldMatrix,
			});
		}

		private void Update()
		{
			HeadPoseHistory history = HeadPoseHistory.Instance;
			long now = (long)(Time.time * 1e9);
			while (shots.Count > 0 && now - shots.Peek().timestampNs > 100_000_000)
			{
				Shot s = shots.Dequeue();
				if (history == null) continue;
				Matrix4x4 frameFromSpace = s.spaceFromFrame.inverse;
				for (int k = -1; k <= 1; k++)
				{
					if (!history.TryGetLocalPose(s.timestampNs + (long)(k * s.dt * 1e9), out Pose local)) continue;
					Vector3 position = frameFromSpace.MultiplyPoint3x4(local.position);
					Quaternion rotation = frameFromSpace.rotation * local.rotation;
					byOffset[k + 1].Add(Vector3.Distance(position, s.position), Quaternion.Angle(rotation, s.rotation));
				}

				if (history.TryGetPreviousSampleTime(s.timestampNs, out long previous) && history.TryGetLocalPose(previous, out Pose fixedPose))
				{
					Vector3 position = frameFromSpace.MultiplyPoint3x4(fixedPose.position);
					Quaternion rotation = frameFromSpace.rotation * fixedPose.rotation;
					corrected.Add(Vector3.Distance(position, s.position), Quaternion.Angle(rotation, s.rotation));
				}
			}
		}

		public string Summary =>
			$"image pose error as the localizer looks it up {corrected}; as stamped {byOffset[1]} (a frame earlier {byOffset[0]}; a frame later {byOffset[2]})";
	}
}
#endif
