using System;
using System.Collections.Generic;
using UnityEngine;

namespace Anaglyph.XR.SharedSpaces.AprilTags
{
	/// <summary>
	/// The lower of two detected IDs defines world zero; the line to the higher defines +Z.
	/// Gravity defines +Y. Tag rotations, registrations, and saved map references are unused.
	/// Observations live only in the current tracking frame. Invalidate them when that frame
	/// or alignment is lost; both tags must then be seen again. No anchors are created or used.
	/// </summary>
	public sealed class TwoAprilTagColocationConstraintProvider : IColocationConstraintProvider
	{
		public const float MinimumHorizontalSeparation = 0.1f;
		private const int CorrectionSamples = 30;

		private sealed class TagReference
		{
			public readonly int id;
			public bool observed;
			public Vector3 position;
			public Vector3 positionSum;
			public int samples;

			public TagReference(int id) => this.id = id;
		}

		private readonly AprilTagColocationConstraintProvider observations;
		private readonly Transform trackingSpace;
		private readonly Func<bool> trackingReady;
		private int configuredFirst = -1;
		private int configuredSecond = -1;
		public event Action<int, int> PairSelected = delegate { };
		private TagReference origin;
		private TagReference forward;
		private long ignoreFramesThrough;

		public int? OriginTagId => origin?.id;
		public int? ForwardTagId => forward?.id;

		public TwoAprilTagColocationConstraintProvider(AprilTagColocationConstraintProvider observations,
			Transform trackingSpace, Func<bool> trackingReady)
		{
			this.observations = observations;
			this.trackingSpace = trackingSpace;
			this.trackingReady = trackingReady ?? throw new ArgumentNullException(nameof(trackingReady));
		}

		public bool IsAvailable => observations != null && observations.TagTracker != null &&
			trackingSpace != null;
		public bool IsRunning { get; private set; }

		public void StartProviding()
		{
			if (IsRunning) return;
			IsRunning = true;
			ResetReferences();
			if (observations == null) return;
			observations.TagObserved += OnTagObserved;
			observations.TagSizeChanged += ResetReferences;
			observations.SetDetectionRequest(this, true);
		}

		public void StopProviding()
		{
			if (!IsRunning) return;
			IsRunning = false;
			if (observations != null)
			{
				observations.TagObserved -= OnTagObserved;
				observations.TagSizeChanged -= ResetReferences;
				observations.SetDetectionRequest(this, false);
			}
			ResetReferences();
		}

		/// <summary>Reacquires the configured pair, discarding only its transient observations.</summary>
		public void ResetReferences()
		{
			origin = forward = null;
			// Detection is asynchronous. A frame already being processed at invalidation
			// must not supply observations in the replacement tracking frame.
			ignoreFramesThrough = observations != null && observations.TagTracker != null
				? observations.TagTracker.FrameTimestampNs : 0;
		}

		private bool CanObserve()
		{
			if (!IsRunning) return false;
			if (IsAvailable && trackingReady()) return true;
			ResetReferences();
			return false;
		}

		private void OnTagObserved(int id, Pose pose)
		{
			if (!CanObserve() || observations.TagTracker.FrameTimestampNs <= ignoreFramesThrough ||
				!IsFinite(pose.position)) return;
			Vector3 position = trackingSpace.InverseTransformPoint(pose.position);
			if (!IsFinite(position)) return;
			TagReference reference = SelectReference(id);
			if (reference == null) return;

			if (!reference.observed)
			{
				reference.position = position;
				reference.observed = true;
				return;
			}

			// Rig corrections move world space; averaging in tracking space avoids feeding
			// those corrections back into the observations.
			reference.positionSum += position;
			if (++reference.samples < CorrectionSamples) return;
			reference.position = reference.positionSum / reference.samples;
			reference.positionSum = Vector3.zero;
			reference.samples = 0;
		}

		public void ConfigurePair(int first, int second)
		{
			if (first >= 0 && second >= 0 && first != second)
			{ int low = Math.Min(first, second); second = Math.Max(first, second); first = low; }
			else first = second = -1;
			if (configuredFirst == first && configuredSecond == second) return;
			configuredFirst = first; configuredSecond = second; ResetReferences();
		}

		private TagReference SelectReference(int id)
		{
			if (id < 0) return null;
			if (configuredFirst >= 0)
			{
				if (id == configuredFirst) return origin ??= new TagReference(id);
				if (id == configuredSecond) return forward ??= new TagReference(id);
				return null;
			}
			if (origin == null) return origin = new TagReference(id);
			if (id == origin.id) return origin;
			if (forward == null)
			{
				forward = new TagReference(id);
				if (origin.id > forward.id) (origin, forward) = (forward, origin);

			}
			return id == origin.id ? origin : id == forward.id ? forward : null;
		}

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!CanObserve() || origin?.observed != true || forward?.observed != true) return;

			if (TryCreateConstraints(trackingSpace.TransformPoint(origin.position),
				trackingSpace.TransformPoint(forward.position), out var first, out var second))
			{
				if (configuredFirst < 0)
				{
					var selectedOrigin = origin;
					var selectedForward = forward;
					configuredFirst = origin.id; configuredSecond = forward.id;
					PairSelected.Invoke(configuredFirst, configuredSecond);
					// The session may reject this pair or replace its context synchronously.
					if (!IsRunning || origin != selectedOrigin || forward != selectedForward) return;
				}
				results.Add(first);
				results.Add(second);
			}
		}

		/// <summary>Preserves measured scale and height difference while constraining yaw.</summary>
		public static bool TryCreateConstraints(Vector3 originPosition, Vector3 forwardPosition,
			out ColocationConstraint first, out ColocationConstraint second)
		{
			first = second = default;
			Vector3 delta = forwardPosition - originPosition;
			if (!IsFinite(originPosition) || !IsFinite(forwardPosition) || !IsFinite(delta)) return false;
			float distance = new Vector2(delta.x, delta.z).magnitude;
			if (float.IsInfinity(distance) || distance < MinimumHorizontalSeparation) return false;

			first = new ColocationConstraint(new Pose(originPosition, Quaternion.identity), Pose.identity);
			second = new ColocationConstraint(new Pose(forwardPosition, Quaternion.identity),
				new Pose(new Vector3(0, delta.y, distance), Quaternion.identity));
			return true;
		}

		private static bool IsFinite(Vector3 value) =>
			!float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
			!float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
			!float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}
}
