using Anaglyph.XRTemplate.AprilTags;
using Anaglyph.XRTemplate.DeviceCameras;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

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

		[SerializeField] private GameObject worldLockAnchorPrefab;
		[SerializeField] private CameraReader cameraReader;
		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		private WorldLockAnchor anchor;

		private bool isActive;
		public bool IsActive => isActive;
		private bool isAligned;

		public event Action Colocated = delegate { };

		private void Awake()
		{
			Instance = this;

			cameraReader = FindAnyObjectByType<CameraReader>();
			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			CanonTags = new ReadOnlyDictionary<int, Pose>(canonTags);
			LocalTags = new ReadOnlyDictionary<int, Vector3>(localTags);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				tagSizeSync.Value = tagSizeCmHostSetting;

			NetworkManager.OnClientConnectedCallback += OnClientConnected;
		}

		public override void OnNetworkDespawn()
		{
			NetworkManager.OnClientConnectedCallback -= OnClientConnected;
		}

		public async void StartColocation()
		{
			if (isActive) return;
			isActive = true;

			OVRManager.display.RecenteredPose += OnRecentered;
			tagTracker.OnDetectTags += OnDetectTags;

			await cameraReader.TryOpenCamera();
			tagTracker.tagSizeMeters = TagSizeCm / 100f;

			anchor = Instantiate(worldLockAnchorPrefab).GetComponent<WorldLockAnchor>();
		}

		public void StopColocation()
		{
			if (!isActive) return;
			isActive = false;
			isAligned = false;

			OVRManager.display.RecenteredPose -= OnRecentered;
			tagTracker.OnDetectTags -= OnDetectTags;

			cameraReader.CloseCamera();

			canonTags.Clear();
			localTags.Clear();

			Destroy(anchor.gameObject);
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

		private void OnRecentered()
		{
			localTags.Clear();
		}

		private void OnClientConnected(ulong id)
		{
			var thisClient = id == NetworkManager.LocalClientId;
			if (thisClient)
				return;

			if (IsOwner)
			{
				var keys = new int[canonTags.Count];
				var values = new Pose[canonTags.Count];

				canonTags.Keys.CopyTo(keys, 0);
				canonTags.Values.CopyTo(values, 0);

				var sendTo = RpcTarget.Single(id, RpcTargetUse.Temp);
				SyncCanonTagsRpc(keys, values, sendTo);
			}
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void SyncCanonTagsRpc(int[] id, Pose[] poses, RpcParams rpcParams = default)
		{
			for (var i = 0; i < id.Length; i++)
				canonTags[id[i]] = poses[i];
		}

		private async void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			var space = MainXRRig.TrackingSpace;
			var spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			// register local tags
			foreach (var r in results)
			{
				var globalPos = r.Position;
				var localPos = spaceMat.inverse.MultiplyPoint(globalPos);
				localTags[r.ID] = localPos;
			}

			// register canon tags
			if (IsOwner)
			{
				var timestamp = tagTracker.FrameTimestamp;
				var head = OVRPlugin.GetNodePoseStateAtTime(timestamp, OVRPlugin.Node.Head);
				var speed = head.Velocity.FromVector3f().magnitude;
				var angSpeed = head.AngularVelocity.FromVector3f().magnitude;
				var headIsStable = speed < maxHeadSpeed && angSpeed < maxHeadAngSpeed;

#if UNITY_EDITOR
				headIsStable = true;
#endif

				if (headIsStable)
				{
					var headPos = MainXRRig.Camera.transform.position;

					foreach (var r in results)
					{
						var alreadyRegistered = canonTags.ContainsKey(r.ID);
						if (alreadyRegistered)
							continue;

						var globalPos = r.Position;
						var dist = Vector3.Distance(headPos, globalPos);
						var isCloseEnough = dist < TagSizeCm * lockDistanceScale;

						if (isCloseEnough)
						{
							var pose = new Pose(r.Position, r.Rotation);
							RegisterCanonTagRpc(r.ID, pose);
						}
					}
				}
			}

			sharedLocalPositions.Clear();
			sharedCanonPositions.Clear();

			foreach (var localTag in localTags)
			{
				float3 localPos = localTag.Value;
				var localId = localTag.Key;
				var mutuallyFound = canonTags.TryGetValue(localId, out var canonPose);

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

			var lerp = isAligned ? tagLerp : 1f;
			var aligned = delta * spaceMat;
			MainXRRig.Instance.AlignSpace(spaceMat, aligned, lerp);

			var spacePos = space.position;
			if (spacePos.magnitude > 10000f ||
			    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
			    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
			    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
				MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);

			if (!isAligned)
				Colocated.Invoke();

			isAligned = true;

			await Awaitable.EndOfFrameAsync();
			anchor.target = anchor.transform.localToWorldMatrix;
		}

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void RegisterCanonTagRpc(int id, Pose canonPose)
		{
			canonTags[id] = canonPose;
		}
	}
}