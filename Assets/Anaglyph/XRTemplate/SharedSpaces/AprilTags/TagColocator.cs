using Anaglyph.XRTemplate.AprilTags;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class TagColocator : NetworkBehaviour, IColocator
	{
		public static TagColocator Instance { get; private set; }

		public float tagSizeCmHostSetting;
		private readonly NetworkVariable<float> tagSizeSync = new();
		public float TagSizeCm => tagSizeSync.Value;

		private readonly Dictionary<int, Pose> canonTags = new();
		private readonly Dictionary<int, Vector3> localTags = new();

		public ReadOnlyDictionary<int, Pose> CanonTags;
		public ReadOnlyDictionary<int, Vector3> LocalTags;

		private readonly List<float3> sharedLocalPositions = new();
		private readonly List<float3> sharedCanonPositions = new();

		public float tagLerp = 0.1f;
		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;

		// [SerializeField] private GameObject worldLockAnchorPrefab;
		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		// private WorldLockAnchor anchor;

		private bool isActive;
		public bool IsActive => isActive;
		private bool isAligned;

		public event Action Colocated = delegate { };

		private void Awake()
		{
			Instance = this;

			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			CanonTags = new ReadOnlyDictionary<int, Pose>(canonTags);
			LocalTags = new ReadOnlyDictionary<int, Vector3>(localTags);
		}

		private void Start()
		{
			tagTracker.enabled = false;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				tagSizeSync.Value = tagSizeCmHostSetting;
		}

		protected override void OnSynchronize<T>(ref BufferSerializer<T> serializer)
		{
			int count = 0;
			int key = 0;
			Pose value = default;

			if (serializer.IsWriter)
			{
				count = canonTags.Count;
				serializer.SerializeValue(ref count);

				foreach (KeyValuePair<int, Pose> kvp in canonTags)
				{
					key = kvp.Key;
					value = kvp.Value;
					serializer.SerializeValue(ref key);
					serializer.SerializeValue(ref value);
				}
			}
			else
			{
				serializer.SerializeValue(ref count);

				canonTags.EnsureCapacity(count);
				canonTags.Clear();

				for (int i = 0; i < count; i++)
				{
					serializer.SerializeValue(ref key);
					serializer.SerializeValue(ref value);
					canonTags[key] = value;
				}
			}
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

			// anchor = Instantiate(worldLockAnchorPrefab).GetComponent<WorldLockAnchor>();

			tagTracker.enabled = true;
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;
			isAligned = false;

			foreach (XRInputSubsystem sub in xrSubsystems)
				sub.trackingOriginUpdated -= OnTrackingOriginUpdated;
			xrSubsystems.Clear();

			tagTracker.OnDetectTags -= OnDetectTags;

			tagTracker.enabled = false;

			canonTags.Clear();
			localTags.Clear();

			// Destroy(anchor.gameObject);
		}

		public void RealignEveryone()
		{
			ClearAllTagsRpc();
		}

		[Rpc(SendTo.Everyone)]
		private void ClearAllTagsRpc()
		{
			canonTags.Clear();
			localTags.Clear();
			isAligned = false;
		}

		private void OnTrackingOriginUpdated(XRInputSubsystem _)
		{
			localTags.Clear();
		}

		private async void OnDetectTags(IReadOnlyList<TagPose> results)
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
			if (IsOwner)
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
						{
							Pose pose = new(r.Position, r.Rotation);
							RegisterCanonTagRpc(r.ID, pose);
						}
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

			await Awaitable.EndOfFrameAsync();
			// anchor.target = anchor.transform.localToWorldMatrix;
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void RegisterCanonTagRpc(int id, Pose canonPose)
		{
			canonTags[id] = canonPose;
		}
	}
}