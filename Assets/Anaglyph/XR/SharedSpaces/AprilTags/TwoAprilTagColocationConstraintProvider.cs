using System;
using System.Collections.Generic;
using System.Threading;
using Anaglyph.XR.SharedSpaces.SharedAnchors;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace Anaglyph.XR.SharedSpaces.AprilTags
{
	/// <summary>
	/// The lower of two detected IDs defines world zero; the line to the higher defines +Z.
	/// Gravity defines +Y. Tag rotations, registrations, and saved map references are unused.
	/// Private, unsaved anchors keep both points observable after scanning them individually.
	/// </summary>
	public sealed class TwoAprilTagColocationConstraintProvider : IColocationConstraintProvider
	{
		public const float MinimumHorizontalSeparation = 0.1f;
		private const int CorrectionSamples = 30;

		private sealed class TagReference
		{
			public readonly int id;
			public bool retired;
			public AnchorLease lease;
			public Vector3 offset;
			public Vector3 offsetSum;
			public int samples;
			public bool minting;

			public TagReference(int id) => this.id = id;

			public void Release()
			{
				retired = true;
				lease?.Dispose();
			}
		}

		private readonly AprilTagColocationConstraintProvider observations;
		private readonly AnchorRegistry registry;
		private readonly Transform trackingSpace;
		private TagReference origin;
		private TagReference forward;
		private CancellationTokenSource lifetime;

		public int? OriginTagId => origin?.id;
		public int? ForwardTagId => forward?.id;

		public TwoAprilTagColocationConstraintProvider(AprilTagColocationConstraintProvider observations,
			AnchorRegistry registry, Transform trackingSpace)
		{
			this.observations = observations;
			this.registry = registry;
			this.trackingSpace = trackingSpace;
		}

		public bool IsAvailable => observations != null && observations.TagTracker != null &&
			registry != null && registry.IsAvailable && trackingSpace != null;
		public bool IsRunning { get; private set; }

		public void StartProviding()
		{
			if (IsRunning) return;
			IsRunning = true;
			lifetime = new CancellationTokenSource();
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

		/// <summary>Forgets the discovered pair when loading a map or changing physical tag size.</summary>
		public void ResetReferences()
		{
			lifetime?.Cancel();
			lifetime?.Dispose();
			lifetime = IsRunning ? new CancellationTokenSource() : null;
			origin?.Release();
			forward?.Release();
			// Pending mints retain the old entries and token, never the new pair.
			origin = forward = null;
		}

		private void OnTagObserved(int id, Pose pose)
		{
			if (!IsRunning || !IsAvailable || !IsFinite(pose.position)) return;
			TagReference reference = SelectReference(id);
			if (reference == null) return;

			if (!TryGetTransform(reference, out Transform anchor))
			{
				if (!reference.minting) MintReference(reference, pose.position, lifetime.Token);
				return;
			}

			// Average the tag's offset in anchor coordinates, so rig corrections and runtime
			// relocalization cannot turn an old world-space reading into fresh evidence.
			reference.offsetSum += anchor.InverseTransformPoint(pose.position);
			if (++reference.samples < CorrectionSamples) return;
			reference.offset = reference.offsetSum / reference.samples;
			reference.offsetSum = Vector3.zero;
			reference.samples = 0;
		}

		private TagReference SelectReference(int id)
		{
			if (id < 0) return null;
			if (origin == null || id < origin.id)
			{
				forward?.Release();
				forward = origin;
				origin = new TagReference(id);
			}
			else if (id != origin.id && (forward == null || id < forward.id))
			{
				forward?.Release();
				forward = new TagReference(id);
			}
			// Keep the two lowest IDs seen during this run, independent of detection order.
			return id == origin.id ? origin : id == forward?.id ? forward : null;
		}

		private async void MintReference(TagReference reference, Vector3 position, CancellationToken token)
		{
			reference.minting = true;
			AnchorLease minted = null;
			Vector3 localPosition = trackingSpace.InverseTransformPoint(position);
			try
			{
				minted = await registry.TryMintAsync(new Pose(position, Quaternion.identity), token);
				if (token.IsCancellationRequested || reference.retired || minted == null || trackingSpace == null) return;
				Transform anchor = minted.Handle.anchor != null ? minted.Handle.anchor.transform : null;
				if (anchor == null) return;
				reference.lease?.Dispose();
				reference.lease = minted;
				minted = null;
				// The colocator may have moved tracking space while native creation was pending.
				reference.offset = anchor.InverseTransformPoint(trackingSpace.TransformPoint(localPosition));
				reference.offsetSum = Vector3.zero;
				reference.samples = 0;
			}
			catch (OperationCanceledException) { }
			catch (Exception exception) { Debug.LogException(exception); }
			finally
			{
				minted?.Dispose();
				reference.minting = false;
			}
		}

		public void GetColocationConstraints(List<ColocationConstraint> results)
		{
			if (!IsRunning || !IsAvailable ||
				!TryGetTransform(origin, out Transform originAnchor) ||
				!TryGetTransform(forward, out Transform forwardAnchor)) return;

			if (TryCreateConstraints(originAnchor.TransformPoint(origin.offset),
				forwardAnchor.TransformPoint(forward.offset), out var first, out var second))
			{
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

		private static bool TryGetTransform(TagReference reference, out Transform transform)
		{
			transform = null;
			if (reference == null) return false;
			AnchorHandle handle = reference.lease?.Handle;
			transform = handle?.anchor != null && handle.state == AnchorHandle.State.Active &&
				handle.anchor.trackingState == TrackingState.Tracking ? handle.anchor.transform : null;
			if (transform == null)
			{
				reference.offsetSum = Vector3.zero;
				reference.samples = 0;
			}
			return transform != null;
		}

		private static bool IsFinite(Vector3 value) =>
			!float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
			!float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
			!float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}
}
