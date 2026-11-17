using System.Collections.Generic;
using Anaglyph.Audio;
using Anaglyph.XR.DepthKit.EnvScanning;
using UnityEngine;

namespace Anaglyph.LaserTag
{
	/// <summary>
	/// First-order echoes off the scanned surfaces around the listener: each wall a probe
	/// ray finds mirrors the source, and the echo plays from where the bounce reaches the
	/// listener, delayed by its extra path
	/// </summary>
	public class EnvSoundReflections : MonoBehaviour, ISoundReflections
	{
		private const float SpeedOfSound = 343f;
		private const float SurfaceOffset = 0.05f;

		[SerializeField] private int maxReflections = 3;
		// earlier echoes fuse with the direct sound and only colour it
		[SerializeField] private float minDelaySeconds = 0.005f;
		[SerializeField] private float maxWallDistance = 20f;
		[SerializeField] private int horizontalProbeCount = 8;
		[SerializeField] private float gainPerBounce = 0.5f;
		[SerializeField] private float mufflePerBounce = 0.15f;

		private struct Wall
		{
			public Vector3 point;
			public Vector3 normal;
		}

		private readonly List<Wall> walls = new();
		private readonly List<Vector3> probeDirections = new();
		private int envMeshLayerMask;
		private AudioListener listener;

		private void Awake()
		{
			envMeshLayerMask = LayerMask.GetMask(EnvMesher.EnvironmentMeshLayerName);

			probeDirections.Add(Vector3.up);
			probeDirections.Add(Vector3.down);
			for (int i = 0; i < horizontalProbeCount; i++)
				probeDirections.Add(Quaternion.Euler(0, i * 360f / horizontalProbeCount, 0) * Vector3.forward);
		}

		private void OnEnable() => AudioPool.Reflections = this;

		private void OnDisable()
		{
			if (ReferenceEquals(AudioPool.Reflections, this)) AudioPool.Reflections = null;
		}

		public void FindReflections(Vector3 source, List<SoundReflection> reflections)
		{
			if (!listener || !listener.isActiveAndEnabled)
				listener = FindAnyObjectByType<AudioListener>();
			if (!listener) return;

			Vector3 listenerPos = listener.transform.position;
			float directLength = Vector3.Distance(source, listenerPos);
			if (directLength > maxWallDistance * 2) return;

			FindWallsAround(listenerPos);

			foreach (Wall wall in walls)
			{
				if (!TryBounce(wall, source, listenerPos, out Vector3 bouncePoint, out float pathLength)) continue;

				float delaySeconds = (pathLength - directLength) / SpeedOfSound;
				if (delaySeconds < minDelaySeconds) continue;

				SoundReflection reflection = new()
				{
					apparentSource = listenerPos + (bouncePoint - listenerPos).normalized * pathLength,
					delaySeconds = delaySeconds,
					gain = gainPerBounce,
					muffle = mufflePerBounce
				};

				// keep the shortest paths: they're the loudest and earliest
				int insertAt = reflections.Count;
				while (insertAt > 0 && reflections[insertAt - 1].delaySeconds > reflection.delaySeconds) insertAt--;
				if (insertAt >= maxReflections) continue;
				reflections.Insert(insertAt, reflection);
				if (reflections.Count > maxReflections) reflections.RemoveAt(maxReflections);
			}
		}

		private void FindWallsAround(Vector3 listenerPos)
		{
			walls.Clear();
			foreach (Vector3 direction in probeDirections)
			{
				if (!Physics.Raycast(listenerPos, direction, out RaycastHit hit, maxWallDistance, envMeshLayerMask,
					    QueryTriggerInteraction.Ignore)) continue;

				Wall wall = new() { point = hit.point, normal = hit.normal };
				if (!IsSameAsFoundWall(wall)) walls.Add(wall);
			}
		}

		private bool IsSameAsFoundWall(Wall wall)
		{
			foreach (Wall found in walls)
				if (Vector3.Dot(found.normal, wall.normal) > 0.9f &&
				    Mathf.Abs(Vector3.Dot(wall.point - found.point, found.normal)) < 0.3f)
					return true;
			return false;
		}

		// mirror the source across the wall's plane; the bounce is where the listener's line
		// to that mirror image crosses the plane, and it only counts if the scan has a
		// surface there that both ends can see
		private bool TryBounce(Wall wall, Vector3 source, Vector3 listenerPos, out Vector3 bouncePoint,
			out float pathLength)
		{
			bouncePoint = default;
			pathLength = 0;

			float sourceHeight = Vector3.Dot(source - wall.point, wall.normal);
			float listenerHeight = Vector3.Dot(listenerPos - wall.point, wall.normal);
			if (sourceHeight < SurfaceOffset || listenerHeight < SurfaceOffset) return false;

			Vector3 mirroredSource = source - 2 * sourceHeight * wall.normal;
			pathLength = Vector3.Distance(listenerPos, mirroredSource);
			bouncePoint = listenerPos + (mirroredSource - listenerPos) * (listenerHeight / (listenerHeight + sourceHeight));

			Vector3 toBounce = bouncePoint - listenerPos;
			float bounceDistance = toBounce.magnitude;
			if (!Physics.Raycast(listenerPos, toBounce / bounceDistance, out RaycastHit hit,
				    bounceDistance + 0.3f, envMeshLayerMask, QueryTriggerInteraction.Ignore)) return false;
			if (hit.distance < bounceDistance - 0.3f || Vector3.Dot(hit.normal, wall.normal) < 0.7f) return false;

			Vector3 sourceToBounce = bouncePoint + wall.normal * SurfaceOffset - source;
			float sourceToBounceLength = sourceToBounce.magnitude;
			return sourceToBounceLength < SurfaceOffset ||
			       !Physics.Raycast(source, sourceToBounce / sourceToBounceLength, sourceToBounceLength,
				       envMeshLayerMask, QueryTriggerInteraction.Ignore);
		}
	}
}
