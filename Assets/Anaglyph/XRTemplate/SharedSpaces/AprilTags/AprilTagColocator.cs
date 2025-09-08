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
	public class AprilTagColocator : NetworkBehaviour, IColocator
	{
		private HashSet<int> tagsLocked = new();
		private Dictionary<int, Vector3> canonTags = new();
		private Dictionary<int, Vector3> localTags = new();

		public ReadOnlyDictionary<int, Vector3> CanonTags;
		public ReadOnlyDictionary<int, Vector3> LocalTags;

		[SerializeField] private float tagLerp = 0.1f;

		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")]
		public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")]
		public float maxHeadAngSpeed = 2f;

		public bool ColocationActive => colocationActive;

		[SerializeField] private CameraReader cameraReader;
		[SerializeField] private AprilTagTracker tagTracker;

		public CameraReader CameraReader => cameraReader;
		public AprilTagTracker TagTracker => tagTracker;

		private void Awake()
		{
			cameraReader = FindAnyObjectByType<CameraReader>();
			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			CanonTags = new(canonTags);
			localTags = new(localTags);
		}

		private void Start()
		{
			NetworkManager.OnClientConnectedCallback += OnClientConnected;
		}

		[Rpc(SendTo.Everyone)]
		private void RegisterCanonTagRpc(int id, Vector3 canonicalPosition)
		{
			canonTags[id] = canonicalPosition;
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void SyncCanonTagsRpc(int[] id, Vector3[] positions, RpcParams rpcParams = default)
		{
			for (int i = 0; i < id.Length; i++)
				canonTags[id[i]] = positions[i];
		}

		private void OnClientConnected(ulong id)
		{
			if (IsOwner && id != NetworkManager.LocalClientId)
			{
				int[] keys = new int[canonTags.Count];
				Vector3[] values = new Vector3[canonTags.Count];

				canonTags.Keys.CopyTo(keys, 0);
				canonTags.Values.CopyTo(values, 0);

				SyncCanonTagsRpc(keys, values, RpcTarget.Single(id, RpcTargetUse.Temp));
			}
		}

		private bool _isColocated;
		public event Action<bool> IsColocatedChange;
		public bool IsColocated
		{
			get => _isColocated;
			private set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed)
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		public float tagSize = 0.1f;
		private bool colocationActive = false;

		public async void Colocate()
		{
			IsColocated = false;
			colocationActive = true;

			tagTracker.tagSizeMeters = tagSize;
			tagTracker.OnDetectTags += OnDetectTags;

			await cameraReader.TryOpenCamera();
		}

		private List<float3> sharedLocalPositions = new();
		private List<float3> sharedCanonPositions = new();

		private void LateUpdate()
		{
			if (!colocationActive)
				return;

			if (IsOwner)
			{
				var headPos = MainXRRig.Camera.transform.position;

				foreach (int id in canonTags.Keys)
				{
					Vector3 canonPos = canonTags[id];
					if (!TagIsWithinRegisterDistance(canonPos))
						tagsLocked.Add(id);
				}
			}

			sharedLocalPositions.Clear();
			sharedCanonPositions.Clear();

			foreach (int id in localTags.Keys)
			{
				float3 localPos = localTags[id];
				bool mutuallyFound = canonTags.TryGetValue(id, out Vector3 canonPos);

				if (mutuallyFound)
				{
					sharedLocalPositions.Add(localPos);
					sharedCanonPositions.Add(canonPos);
				}
			}

			if (sharedLocalPositions.Count >= 3)
			{
				Matrix4x4 trackingSpace = MainXRRig.TrackingSpace.localToWorldMatrix;

				Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
					sharedLocalPositions.ToArray(), trackingSpace,
					sharedCanonPositions.ToArray(), float4x4.identity);

				trackingSpace = delta * trackingSpace;

				MainXRRig.TrackingSpace.position = trackingSpace.GetPosition();
				MainXRRig.TrackingSpace.rotation = trackingSpace.rotation;

				IsColocated = true;
			}

			var originPos = MainXRRig.TrackingSpace.position;

			if (originPos.magnitude > 100000f ||
				float.IsNaN(originPos.x) || float.IsInfinity(originPos.x) ||
				float.IsNaN(originPos.y) || float.IsInfinity(originPos.y) ||
				float.IsNaN(originPos.z) || float.IsInfinity(originPos.z))
			{
				MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);
			}
		}

		private bool TagIsWithinRegisterDistance(Vector3 globalPos)
		{
			Vector3 headPos = MainXRRig.Camera.transform.position;
			return Vector3.Distance(headPos, globalPos) < tagSize * lockDistanceScale;
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!colocationActive)
				return;

			// tags = ((List<TagPose>)results).ToArray();

			foreach (TagPose result in results)
			{
				Vector3 globalPos = result.Position;

				Matrix4x4 worldToTracking = MainXRRig.TrackingSpace.worldToLocalMatrix;
				Vector3 localPos = worldToTracking.MultiplyPoint(globalPos);

				if (localTags.TryGetValue(result.ID, out Vector3 value))
					localPos = Vector3.Lerp(value, localPos, tagLerp);

				localTags[result.ID] = localPos;

				if (IsOwner)
				{
					var headState = OVRPlugin.GetNodePoseStateAtTime(tagTracker.FrameTimestamp, OVRPlugin.Node.Head);
					var v = headState.Velocity;
					Vector3 vel = new(v.x, v.y, v.z);
					float headSpeed = vel.magnitude;
					var av = headState.AngularVelocity;
					Vector3 angVel = new(av.x, av.y, av.z);
					float angHeadSpeed = angVel.magnitude;

					bool headIsStable = headSpeed < maxHeadSpeed && angHeadSpeed < maxHeadAngSpeed;

					if (headIsStable)
					{
						bool locked = tagsLocked.Contains(result.ID);
						bool isCloseEnough = TagIsWithinRegisterDistance(globalPos);

						if (!locked && isCloseEnough)
						{
							RegisterCanonTagRpc(result.ID, globalPos);
						}
					}
				}
			}
		}

		public void StopColocation()
		{
			cameraReader.CloseCamera();
			tagTracker.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;

			tagsLocked.Clear();
			canonTags.Clear();
			localTags.Clear();
		}
	}
}
