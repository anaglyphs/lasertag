using Anaglyph.Netcode;
using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	/// <summary>
	/// Presents registered AprilTags as colocation references.
	///
	/// Tags are the only absolute reference in the system: they *are* environment, so unlike
	/// anchors they cannot drift as the runtime revises its tracking map. What they cannot do
	/// is persist — a tag is only a reference while it is in view. That is why tags and
	/// anchors are complementary sources rather than competing colocators: anchors carry
	/// alignment across recenters, sleep, and sessions, while tags keep the anchors honest.
	///
	/// Observed tag positions are remembered in tracking space, so a tag seen a moment ago
	/// stays usable as the player moves on. Nothing here is ever shared or networked; only
	/// tag canon poses propagate, through <see cref="CanonTags"/>.
	/// </summary>
	public class TagReferenceSource : MonoBehaviour, IColocationReferenceSource
	{
		public static TagReferenceSource Instance { get; private set; }

		public float tagSizeCmHostSetting;
		private readonly SyncVariable<float> tagSizeSync = new("tags.size");
		public float TagSizeCm => tagSizeSync.Value;

		/// <summary>
		/// Registered tag canon poses, in the map's world frame — injected by the game layer.
		/// In a session this is host-synced; offline it mirrors the loaded map.
		/// </summary>
		public SyncDictionary<int, Pose> CanonTags { get; set; }

		/// <summary>
		/// A stable, close-up observation of a tag (registered or not), in world coordinates.
		/// The map system mints and corrects per-tag anchors from these; the registration tool
		/// listens for unregistered ones.
		/// </summary>
		public event Action<int, Pose> TagObserved = delegate { };

		[Tooltip("How near the tag must be to trust an observation, as a multiple of tag size")]
		public float lockDistanceScale = 10;

		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;

		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		// Tag positions in tracking space, so they survive alignment shifts. Per-peer only.
		private readonly Dictionary<int, Vector3> localTags = new();

		/// <summary>This peer's remembered tag positions, in tracking space. Read-only.</summary>
		public IReadOnlyDictionary<int, Vector3> LocalTags => localTags;

		private bool running;

		/// <summary>Whether tag detection is running at all.</summary>
		public bool IsRunning => running;

		private readonly List<XRInputSubsystem> xrSubsystems = new();

		private void Awake()
		{
			Instance = this;

			if (!tagTracker)
				tagTracker = FindAnyObjectByType<AprilTagTracker>();

			tagSizeSync.Register();
			tagSizeSync.Changed += OnTagSizeChanged;
			SyncBus.Activated += OnBusActivated;

			tagTracker.OnDetectTags += OnDetectTags;
		}

		private void OnDestroy()
		{
			SetRunning(false);

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
			UpdateTrackerEnabled();
		}

		/// <summary>
		/// Starts or stops tag detection. Independent of whether anything is colocating:
		/// the map editor's registration tool wants detections with no alignment running,
		/// and a tag map wants them whichever colocation method a session uses.
		/// </summary>
		public void SetRunning(bool value)
		{
			if (running == value) return;
			running = value;

			if (running)
			{
				// Offline authoring: this peer is the only writer, and the tracker needs a
				// size before it can detect anything.
				if (!SyncBus.Active && tagSizeCmHostSetting > 0)
					tagSizeSync.Value = tagSizeCmHostSetting;

				SubsystemManager.GetSubsystems(xrSubsystems);
				foreach (XRInputSubsystem sub in xrSubsystems)
					sub.trackingOriginUpdated += OnTrackingOriginUpdated;
			}
			else
			{
				foreach (XRInputSubsystem sub in xrSubsystems)
					sub.trackingOriginUpdated -= OnTrackingOriginUpdated;
				xrSubsystems.Clear();

				localTags.Clear();
			}

			UpdateTrackerEnabled();
		}

		private void UpdateTrackerEnabled()
		{
			if (tagTracker == null) return;

			tagTracker.tagSizeMeters = EffectiveTagSizeMeters();
			tagTracker.enabled = running;
		}

		// Offline the size sync may never have been written; fall back to the host setting so
		// authoring works before any session existed.
		private float EffectiveTagSizeMeters()
		{
			float cm = TagSizeCm > 0 ? TagSizeCm : tagSizeCmHostSetting;
			return cm / 100f;
		}

		// A recenter moves the tracking origin out from under the remembered positions, so
		// they no longer describe anywhere real. The colocator keeps its alignment from the
		// anchors until a tag comes back into view.
		private void OnTrackingOriginUpdated(XRInputSubsystem _)
		{
			localTags.Clear();
		}

		// Sleep pauses tracking; same reasoning as a recenter.
		private void OnApplicationFocus(bool isFocused)
		{
			if (!isFocused)
				localTags.Clear();
		}

		public void GetColocationReferences(List<ColocationReference> results)
		{
			if (CanonTags == null)
				return;

			Matrix4x4 spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			foreach (KeyValuePair<int, Vector3> localTag in localTags)
			{
				if (!CanonTags.TryGetValue(localTag.Key, out Pose canonPose))
					continue;

				Vector3 observedWorld = spaceMat.MultiplyPoint(localTag.Value);

				// A tag's rotation comes from one noisy image estimate, so it is not
				// trustworthy enough to align against on its own — hence the default false.
				results.Add(new ColocationReference(
					new Pose(observedWorld, canonPose.rotation), canonPose));
			}
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!running)
				return;

			Matrix4x4 spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			// Remember tracking-space positions for the per-frame fit.
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
	}
}
