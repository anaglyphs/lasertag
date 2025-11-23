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
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class TagColocator : NetworkBehaviour, IColocator
	{
		private HashSet<int> lockedTags = new();

		private Dictionary<int, Pose> canonTags = new();
		private Dictionary<int, Vector3> localTags = new();
		public HashSet<int> LockedTags => lockedTags;
		private WorldLockAnchor anchor;

		public ReadOnlyDictionary<int, Pose> CanonTags;
		public ReadOnlyDictionary<int, Vector3> LocalTags;

		private List<float3> sharedLocalPositions = new();
		private List<float3> sharedCanonPositions = new();

		public float tagSize = 0.1f;
		public float tagLerp = 0.1f;

		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;

		public bool ColocationActive => _colocationActive;

		[SerializeField] private GameObject worldLockAnchorPrefab;
		[SerializeField] private CameraReader cameraReader;
		[SerializeField] private AprilTagTracker tagTracker;
		public AprilTagTracker TagTracker => tagTracker;

		private static bool _isColocated;
		public event Action<bool> IsColocatedChange;

		public bool IsColocated
		{
			get => _isColocated;
			private set
			{
				var changed = value != _isColocated;
				_isColocated = value;
				if (changed)
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		private bool _colocationActive = false;

		private void Awake()
		{
			cameraReader = FindAnyObjectByType<CameraReader>();
			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			CanonTags = new ReadOnlyDictionary<int, Pose>(canonTags);
			LocalTags = new ReadOnlyDictionary<int, Vector3>(localTags);
		}

		public async void Colocate()
		{
			if (!XRSettings.enabled)
				return;

			if (_colocationActive)
				StopColocation();

			IsColocated = false;
			_colocationActive = true;

			OVRManager.display.RecenteredPose += OnRecentered;
			tagTracker.OnDetectTags += OnDetectTags;
			NetworkManager.OnClientConnectedCallback += OnClientConnected;

			await cameraReader.TryOpenCamera();
			tagTracker.tagSizeMeters = tagSize;

			if (anchor)
				return;

			anchor = Instantiate(worldLockAnchorPrefab).GetComponent<WorldLockAnchor>();
		}

		private void OnRecentered()
		{
			localTags.Clear();
		}

		public void StopColocation()
		{
			if (!_colocationActive)
				return;

			OVRManager.display.RecenteredPose -= OnRecentered;
			tagTracker.OnDetectTags -= OnDetectTags;
			NetworkManager.OnClientConnectedCallback -= OnClientConnected;

			cameraReader.CloseCamera();

			lockedTags.Clear();
			canonTags.Clear();
			localTags.Clear();

			if (anchor)
				Destroy(anchor.gameObject);

			_colocationActive = false;
			IsColocated = false;
		}

		public override void OnNetworkDespawn()
		{
			StopColocation();
		}

		private void OnClientConnected(ulong id)
		{
			if (!IsColocated)
				return;

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

		[Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
		private void RegisterCanonTagRpc(int id, Pose canonPose)
		{
			canonTags[id] = canonPose;
		}

		[Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Owner)]
		private void SyncCanonTagsRpc(int[] id, Pose[] poses, RpcParams rpcParams = default)
		{
			for (var i = 0; i < id.Length; i++)
				canonTags[id[i]] = poses[i];
		}

		[Rpc(SendTo.Everyone)]
		public void ClearCanonTagsRpc()
		{
			canonTags.Clear();
		}

		private bool TagIsWithinRegisterDistance(Vector3 globalPos)
		{
			var headPos = MainXRRig.Camera.transform.position;
			return Vector3.Distance(headPos, globalPos) < tagSize * lockDistanceScale;
		}

		private async void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			var space = MainXRRig.TrackingSpace;
			var spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			var headState = OVRPlugin.GetNodePoseStateAtTime(tagTracker.FrameTimestamp, OVRPlugin.Node.Head);

			var v = headState.Velocity;
			Vector3 vel = new(v.x, v.y, v.z);
			var headSpeed = vel.magnitude;

			var av = headState.AngularVelocity;
			Vector3 angVel = new(av.x, av.y, av.z);
			var angHeadSpeed = angVel.magnitude;

			var headIsStable = headSpeed < maxHeadSpeed && angHeadSpeed < maxHeadAngSpeed;

#if UNITY_EDITOR
			headIsStable = true;
#endif

			foreach (var result in results)
			{
				var globalPos = result.Position;
				var localPos = spaceMat.inverse.MultiplyPoint(globalPos);
				localTags[result.ID] = localPos;

				if (IsOwner)
				{
					var isCloseEnough = TagIsWithinRegisterDistance(localPos);
					if (isCloseEnough)
					{
						if (lockedTags.Contains(result.ID)) continue;

						var pose = new Pose(globalPos, result.Rotation);
						if (canonTags.TryGetValue(result.ID, out var tagPose))
							pose = tagPose.Lerp(pose, tagLerp);

						RegisterCanonTagRpc(result.ID, pose);
					}
					else if (canonTags.ContainsKey(result.ID))
					{
						lockedTags.Add(result.ID);
					}
				}
			}

			sharedLocalPositions.Clear();
			sharedCanonPositions.Clear();

			foreach (var id in localTags.Keys)
			{
				float3 localPos = localTags[id];
				var mutuallyFound = canonTags.TryGetValue(id, out var canonPose);

				if (mutuallyFound)
				{
					sharedLocalPositions.Add(localPos);
					sharedCanonPositions.Add(canonPose.position);
				}
			}

			if (sharedLocalPositions.Count < 3)
				return;

			var lerp = IsColocated ? tagLerp : 1f;

			// Kabsch if more than 3 tags
			Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
				sharedLocalPositions.ToArray(), spaceMat,
				sharedCanonPositions.ToArray(), float4x4.identity);

			var aligned = delta * spaceMat;

			MainXRRig.Instance.AlignSpace(spaceMat, aligned, lerp);

			// else
			// {
			// 	// else, simply lerp to match pose
			// 	foreach (var result in results)
			// 	{
			// 		var id = result.ID;
			// 		if (!canonTags.ContainsKey(id) || !localTags.ContainsKey(id))
			// 			continue;
			//
			// 		var one = Vector3.one;
			// 		var tagMat = Matrix4x4.TRS(result.Position, result.Rotation, one);
			// 		var canonTag = canonTags[id];
			// 		var canonTagMat = Matrix4x4.TRS(canonTag.position, canonTag.rotation, one);
			//
			// 		MainXRRig.Instance.AlignSpace(tagMat, canonTagMat, lerp);
			// 	}
			// }

			var spacePos = space.position;
			if (spacePos.magnitude > 10000f ||
			    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
			    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
			    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
				MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);

			await Awaitable.EndOfFrameAsync();
			anchor.target = anchor.transform.localToWorldMatrix;

			IsColocated = true;
		}
	}
}