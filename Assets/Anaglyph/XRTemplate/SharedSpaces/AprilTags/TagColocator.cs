using Anaglyph.Netcode;
using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Tag-augmented anchor colocator. Tags are environment — they cannot drift — but they
	/// are only references while in view. Anchors survive recenters, sleep, and sessions,
	/// but drift as the runtime revises its tracking map. So this colocator aligns against
	/// both: the map system's anchors (via <see cref="AnchorReferenceSource"/>, one per
	/// registered tag) plus any registered tags currently observed — and reports stable,
	/// close tag observations upward so the map system can mint each tag's anchor and
	/// correct its canon pose as it drifts.
	///
	/// No anchor is ever shared or networked in tag mode; only tag canon poses propagate.
	/// </summary>
	public class TagColocator : MonoBehaviour, IColocator
	{
		public static TagColocator Instance { get; private set; }

		public float tagSizeCmHostSetting;
		private readonly SyncVariable<float> tagSizeSync = new("tags.size");
		public float TagSizeCm => tagSizeSync.Value;

		/// <summary>The map system's anchors — injected by the game layer.</summary>
		public IColocationReferenceSource AnchorReferenceSource { get; set; }

		/// <summary>
		/// Registered tag canon poses, in the map's world frame — injected by the game
		/// layer. In a session this is host-synced; offline it mirrors the loaded map.
		/// </summary>
		public SyncDictionary<int, Pose> CanonTags { get; set; }

		/// <summary>
		/// A stable, close-up observation of a tag (any tag, registered or not), in world
		/// coordinates. The map system mints and corrects per-tag anchors from these; the
		/// registration tool listens for unregistered tags.
		/// </summary>
		public event Action<int, Pose> TagObserved = delegate { };

		// Local tag positions are per-peer only, kept in tracking space so they stay
		// meaningful across alignment shifts.
		private readonly Dictionary<int, Vector3> localTags = new();

		/// <summary>This peer's remembered tag positions, in tracking space. Read-only.</summary>
		public IReadOnlyDictionary<int, Vector3> LocalTags => localTags;

		public float tagLerp = 0.1f;
		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;

		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		public ColocationState State { get; private set; } = ColocationState.Stopped;
		public bool IsActive => State != ColocationState.Stopped;

		public event Action<ColocationState> StateChanged = delegate { };

		// Tag detection wanted without colocation running — the map editor's registration
		// mode, before a map (or its first tag) exists.
		private bool observing;

		private System.Threading.CancellationTokenSource ctknSrc;

		private readonly List<ColocationReference> anchorReferences = new();
		private readonly List<(float3 subject, float3 target)> positionPairs = new();
		private readonly List<XRInputSubsystem> xrSubsystems = new();

		private void SetState(ColocationState next)
		{
			if (State == next) return;
			State = next;
			StateChanged.Invoke(next);
		}

		// Alignment we already had is stale but still applied, so this is Lost rather than
		// Searching. Never demotes a colocator that was never aligned in the first place.
		private void Delocalize()
		{
			if (State == ColocationState.Localized)
				SetState(ColocationState.Lost);
		}

		private void Awake()
		{
			Instance = this;

			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			tagSizeSync.Register();
			tagSizeSync.Changed += OnTagSizeChanged;
			SyncBus.Activated += OnBusActivated;

			// Subscribed for the component's whole lifetime: observation mode needs
			// detections while colocation is stopped.
			tagTracker.OnDetectTags += OnDetectTags;
		}

		private void OnDestroy()
		{
			StopColocation();

			if (tagTracker != null)
				tagTracker.OnDetectTags -= OnDetectTags;

			SyncBus.Activated -= OnBusActivated;
			tagSizeSync.Changed -= OnTagSizeChanged;
			tagSizeSync.Unregister();
		}

		private void Start()
		{
			UpdateTrackerEnabled();
		}

		private void OnBusActivated()
		{
			if (SyncBus.IsAuthority)
				tagSizeSync.Value = tagSizeCmHostSetting;
		}

		private void OnTagSizeChanged(float oldValue, float newValue)
		{
			if (tagTracker != null && newValue > 0)
				tagTracker.tagSizeMeters = newValue / 100f;
		}

		// Sleeping pauses tracking, so whatever alignment we had no longer describes where
		// the headset is. Waking up mid-game must not be mistaken for still being aligned.
		private void OnApplicationFocus(bool isFocused)
		{
			if (!isFocused)
				Delocalize();
		}

		/// <summary>
		/// Tag detection without colocation, for the registration tool. Observation raises
		/// <see cref="TagObserved"/> but never aligns anything.
		/// </summary>
		public void SetObserving(bool value)
		{
			observing = value;
			UpdateTrackerEnabled();
		}

		private void UpdateTrackerEnabled()
		{
			if (tagTracker == null) return;

			tagTracker.tagSizeMeters = EffectiveTagSizeMeters();
			tagTracker.enabled = IsActive || observing;
		}

		// Offline (authoring) the size sync may never have been written; fall back to the
		// host setting so registration works before any session existed.
		private float EffectiveTagSizeMeters()
		{
			float cm = TagSizeCm > 0 ? TagSizeCm : tagSizeCmHostSetting;
			return cm / 100f;
		}

		public void StartColocation()
		{
			if (IsActive) return;
			SetState(ColocationState.Searching);

			// Offline authoring: this peer is the only writer, and the tracker needs a size.
			if (!SyncBus.Active && tagSizeCmHostSetting > 0)
				tagSizeSync.Value = tagSizeCmHostSetting;

			SubsystemManager.GetSubsystems(xrSubsystems);
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated += OnTrackingOriginUpdated;

			UpdateTrackerEnabled();

			ctknSrc = new System.Threading.CancellationTokenSource();
			AlignLoop(ctknSrc.Token);
		}

		public void StopColocation()
		{
			if (!IsActive) return;
			SetState(ColocationState.Stopped);

			ctknSrc?.Cancel();
			ctknSrc = null;

			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated -= OnTrackingOriginUpdated;
			xrSubsystems.Clear();

			localTags.Clear();
			UpdateTrackerEnabled();
		}

		// A recenter moves the tracking origin out from under the alignment, so the fit we
		// computed no longer holds and the tracking-space tag positions are garbage. The
		// map's anchors restore alignment with no tag in view — that recovery is the reason
		// this colocator is anchor-backed.
		private void OnTrackingOriginUpdated(XRInputSubsystem _)
		{
			localTags.Clear();
			Delocalize();
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!IsActive && !observing)
				return;

			Matrix4x4 spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			// Remember local (tracking space) tag positions for the per-frame fit.
			if (IsActive)
				foreach (TagPose r in results)
					localTags[r.ID] = spaceMat.inverse.MultiplyPoint(r.Position);

			// Head velocity at the frame's capture time: a fast-moving head means motion
			// blur and pose-latency error, which must not leak into durable canon data.
			Vector3 headVel = default, headAngVel = default;
			bool gotVel = HeadPoseHistory.Instance != null &&
			              HeadPoseHistory.Instance.TryGetVelocity(tagTracker.FrameTimestampNs,
				              out headVel, out headAngVel);

			float speed = gotVel ? headVel.magnitude : 0f;
			float angSpeed = gotVel ? headAngVel.magnitude : 0f;

			// if velocity is unknown (no history yet), don't block observation
			bool headIsStable = !gotVel || (speed < maxHeadSpeed && angSpeed < maxHeadAngSpeed);

#if UNITY_EDITOR
			headIsStable = true;
#endif

			if (!headIsStable)
				return;

			Vector3 headPos = MainXRRig.Camera.transform.position;
			float lockDistance = EffectiveTagSizeMeters() * lockDistanceScale;

			foreach (TagPose r in results)
			{
				float dist = Vector3.Distance(headPos, r.Position);
				if (dist < lockDistance)
					TagObserved.Invoke(r.ID, new Pose(r.Position, r.Rotation));
			}
		}

		private async void AlignLoop(System.Threading.CancellationToken ctkn)
		{
			try
			{
				while (!ctkn.IsCancellationRequested)
				{
					await Awaitable.NextFrameAsync(ctkn);

					Transform space = MainXRRig.TrackingSpace;
					Matrix4x4 spaceMat = space.localToWorldMatrix;

					anchorReferences.Clear();
					AnchorReferenceSource?.GetColocationReferences(anchorReferences);

					positionPairs.Clear();

					foreach (ColocationReference reference in anchorReferences)
						positionPairs.Add(
							(reference.observed.position, reference.canon.position));

					if (CanonTags != null)
					{
						foreach (KeyValuePair<int, Vector3> localTag in localTags)
						{
							if (!CanonTags.TryGetValue(localTag.Key, out Pose canonPose))
								continue;

							float3 observedWorld = spaceMat.MultiplyPoint(localTag.Value);
							positionPairs.Add((observedWorld, canonPose.position));
						}
					}

					// An anchor carries a trustworthy rotation, so one is enough to align.
					// Tag position estimates alone need three before a fit means anything.
					bool canFit = anchorReferences.Count >= 1
						? positionPairs.Count >= 1
						: positionPairs.Count >= 3;

					if (!canFit)
					{
						Delocalize();
						continue;
					}

					// First fit after Searching or Lost snaps; subsequent ones ease in.
					float lerp = State == ColocationState.Localized ? tagLerp : 1f;

					if (positionPairs.Count == 1)
					{
						Pose s = anchorReferences[0].observed;
						Pose t = anchorReferences[0].canon;

						MainXRRig.Instance.AlignSpace(
							Matrix4x4.TRS(s.position, s.rotation, Vector3.one),
							Matrix4x4.TRS(t.position, t.rotation, Vector3.one),
							lerp);
					}
					else
					{
						Matrix4x4 delta = BestFit.Find4DOF(positionPairs);
						MainXRRig.Instance.AlignSpace(spaceMat, delta * spaceMat, lerp);
					}

					Vector3 spacePos = space.position;
					if (spacePos.magnitude > 10000f ||
					    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
					    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
					    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
						MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);

					SetState(ColocationState.Localized);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception e)
			{
				Debug.LogException(e);
				AlignLoop(ctkn);
			}
		}
	}
}
