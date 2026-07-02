using Anaglyph.Netcode;
using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Mathematics;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class TagColocator : MonoBehaviour, IColocator
	{
		public static TagColocator Instance { get; private set; }

		public float tagSizeCmHostSetting;
		private readonly SyncVariable<float> tagSizeSync = new("tags.size");
		public float TagSizeCm => tagSizeSync.Value;

		// Canon tag poses live in the shared frame and are registered by the bus
		// authority; local tag positions are per-peer only.
		private readonly SyncDictionary<int, Pose> canonTags = new("tags.canon");
		private readonly Dictionary<int, Vector3> localTags = new();

		public IReadOnlyDictionary<int, Pose> CanonTags => canonTags;
		public ReadOnlyDictionary<int, Vector3> LocalTags { get; private set; }

		private readonly List<float3> sharedLocalPositions = new();
		private readonly List<float3> sharedCanonPositions = new();

		public float tagLerp = 0.1f;
		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;

		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		private bool isActive;
		public bool IsActive => isActive;
		private bool isAligned;

		// Set when a non-authority peer realigns: the clear runs once bus authority
		// actually lands on this peer (see OnAuthorityChanged).
		private bool pendingRealign;

		public event Action Colocated = delegate { };

		private void Awake()
		{
			Instance = this;

			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			LocalTags = new ReadOnlyDictionary<int, Vector3>(localTags);

			tagSizeSync.Register();
			canonTags.Register();
			canonTags.Changed += OnCanonTagsChanged;
			SyncBus.Activated += OnBusActivated;
			SyncBus.AuthorityChanged += OnAuthorityChanged;
		}

		private void OnDestroy()
		{
			SyncBus.AuthorityChanged -= OnAuthorityChanged;
			SyncBus.Activated -= OnBusActivated;
			canonTags.Changed -= OnCanonTagsChanged;
			canonTags.Unregister();
			tagSizeSync.Unregister();
		}

		private void Start()
		{
			tagTracker.enabled = false;
		}

		private void OnBusActivated()
		{
			if (SyncBus.IsAuthority)
				tagSizeSync.Value = tagSizeCmHostSetting;
		}

		// The canon set emptying (realign or session reset) invalidates every peer's
		// local pairs and alignment.
		private void OnCanonTagsChanged()
		{
			if (canonTags.Count != 0) return;

			localTags.Clear();
			isAligned = false;
		}

		private readonly List<XRInputSubsystem> xrSubsystems = new();

		public void StartColocation()
		{
			if (isActive) return;
			isActive = true;

			SubsystemManager.GetSubsystems(xrSubsystems);
			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated += OnTrackingOriginUpdated;

			tagTracker.OnDetectTags += OnDetectTags;
			tagTracker.tagSizeMeters = TagSizeCm / 100f;

			tagTracker.enabled = true;
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;
			isAligned = false;
			pendingRealign = false;

			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated -= OnTrackingOriginUpdated;
			xrSubsystems.Clear();

			tagTracker.OnDetectTags -= OnDetectTags;

			tagTracker.enabled = false;

			localTags.Clear();
		}

		// Clearing the canon set restarts alignment everywhere (each peer reacts in
		// OnCanonTagsChanged). The realigner must be the bus authority first — it
		// registers the new canon tags — so on a non-authority peer the clear waits
		// for the ownership change to land instead of racing it.
		public void RealignEveryone()
		{
			if (!SyncBus.Active) return;

			if (SyncBus.IsAuthority)
			{
				canonTags.Clear();
			}
			else
			{
				pendingRealign = true;
				SyncBus.RequestAuthority();
			}
		}

		private void OnAuthorityChanged(bool isAuthority)
		{
			if (!isAuthority)
			{
				pendingRealign = false; // someone else took over mid-request
				return;
			}

			if (pendingRealign)
			{
				pendingRealign = false;
				canonTags.Clear();
			}
		}

		private void OnTrackingOriginUpdated(XRInputSubsystem _)
		{
			localTags.Clear();
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			Transform space = MainXRRig.TrackingSpace;
			Matrix4x4 spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			// register local tags
			foreach (TagPose r in results)
			{
				Vector3 globalPos = r.Position;
				Vector3 localPos = spaceMat.inverse.MultiplyPoint(globalPos);
				localTags[r.ID] = localPos;
			}

			// register canon tags
			if (SyncBus.IsAuthority)
			{
				// Head velocity at the frame's capture time, to avoid registering
				// canon tags while moving fast (motion blur / pose-latency error).
				// Replaces OVRPlugin head Velocity / AngularVelocity.
				Vector3 headVel = default, headAngVel = default;
				bool gotVel = HeadPoseHistory.Instance != null &&
				              HeadPoseHistory.Instance.TryGetVelocity(tagTracker.FrameTimestampNs, out headVel,
					              out headAngVel);

				float speed = gotVel ? headVel.magnitude : 0f;
				float angSpeed = gotVel ? headAngVel.magnitude : 0f;

				// if velocity is unknown (no history yet), don't block registration
				bool headIsStable = !gotVel || (speed < maxHeadSpeed && angSpeed < maxHeadAngSpeed);

#if UNITY_EDITOR
				headIsStable = true;
#endif

				if (headIsStable)
				{
					Vector3 headPos = MainXRRig.Camera.transform.position;

					foreach (TagPose r in results)
					{
						bool alreadyRegistered = canonTags.ContainsKey(r.ID);
						if (alreadyRegistered)
							continue;

						Vector3 globalPos = r.Position;
						float dist = Vector3.Distance(headPos, globalPos);
						bool isCloseEnough = dist < TagSizeCm * lockDistanceScale;

						if (isCloseEnough)
							canonTags.Set(r.ID, new Pose(r.Position, r.Rotation));
					}
				}
			}

			sharedLocalPositions.Clear();
			sharedCanonPositions.Clear();

			foreach (KeyValuePair<int, Vector3> localTag in localTags)
			{
				float3 localPos = localTag.Value;
				int localId = localTag.Key;
				bool mutuallyFound = canonTags.TryGetValue(localId, out Pose canonPose);

				if (mutuallyFound)
				{
					sharedLocalPositions.Add(localPos);
					sharedCanonPositions.Add(canonPose.position);
				}
			}

			if (sharedLocalPositions.Count < 3)
				return;

			// Kabsch if more than 3 tags
			Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
				sharedLocalPositions.ToArray(), spaceMat,
				sharedCanonPositions.ToArray(), float4x4.identity);

			float lerp = isAligned ? tagLerp : 1f;
			Matrix4x4 aligned = delta * spaceMat;
			MainXRRig.Instance.AlignSpace(spaceMat, aligned, lerp);

			Vector3 spacePos = space.position;
			if (spacePos.magnitude > 10000f ||
			    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
			    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
			    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
				MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);

			if (!isAligned)
				Colocated.Invoke();

			isAligned = true;
		}
	}
}
