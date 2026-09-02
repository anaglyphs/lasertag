using System.Collections.Generic;
using Anaglyph.XR.SharedSpaces.AprilTags;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor.Tools
{
	/// <summary>
	/// Remembers where tags were last seen and answers which one a ray is pointing at.
	///
	/// A plain object rather than a component: <see cref="MapEditorTool"/> is its only owner, and
	/// there is nothing here for a scene to configure that the tool does not already hold.
	/// </summary>
	internal sealed class TagAimer
	{
		private readonly Dictionary<int, (Pose pose, float time)> observations = new();
		private readonly List<int> expiredScratch = new();

		private readonly float observationLifetime;
		private readonly float maxAngleDegrees;

		/// <param name="observationLifetime">Seconds an observation stays valid. A pose measured
		/// long enough ago says where the tag was, not where it is.</param>
		/// <param name="maxAngleDegrees">How far off the ray a tag can be and still count as
		/// aimed at.</param>
		public TagAimer(float observationLifetime, float maxAngleDegrees)
		{
			this.observationLifetime = observationLifetime;
			this.maxAngleDegrees = maxAngleDegrees;
		}

		/// <summary>The tag nearest the ray as of the last <see cref="Aim"/>, or -1.</summary>
		public int AimedTagId { get; private set; } = -1;

		public void Register()
		{
			if (AprilTagColocationConstraintProvider.Instance != null)
				AprilTagColocationConstraintProvider.Instance.TagObserved += OnTagObserved;
		}

		public void Unregister()
		{
			if (AprilTagColocationConstraintProvider.Instance != null)
				AprilTagColocationConstraintProvider.Instance.TagObserved -= OnTagObserved;
		}

		public void Clear()
		{
			observations.Clear();
			AimedTagId = -1;
		}

		public bool TryGetObservation(int tagId, out Pose pose)
		{
			if (observations.TryGetValue(tagId, out (Pose pose, float time) entry))
			{
				pose = entry.pose;
				return true;
			}

			pose = default;
			return false;
		}

		private void OnTagObserved(int id, Pose pose) => observations[id] = (pose, Time.time);

		/// <summary>Drops stale observations and re-aims.</summary>
		public int Aim(Vector3 origin, Vector3 forward)
		{
			Expire();

			int best = -1;
			float bestAngle = maxAngleDegrees;

			foreach ((int id, (Pose pose, float _)) in observations)
			{
				float angle = Vector3.Angle(forward, pose.position - origin);

				if (angle < bestAngle)
				{
					bestAngle = angle;
					best = id;
				}
			}

			AimedTagId = best;
			return best;
		}

		private void Expire()
		{
			expiredScratch.Clear();

			foreach ((int id, (Pose _, float time)) in observations)
				if (Time.time - time > observationLifetime)
					expiredScratch.Add(id);

			foreach (int id in expiredScratch)
				observations.Remove(id);
		}
	}
}
