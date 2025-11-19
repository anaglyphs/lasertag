using Anaglyph.XRTemplate.AprilTags;
using Anaglyph.XRTemplate.DeviceCameras;
using AprilTag;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Schema;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : NetworkBehaviour, IColocator
	{
		private HashSet<int> lockedTags = new();
		private Dictionary<int, Pose> canonTags = new();
		// these are local to the XR tracking space!!!
		// stop forgetting!!!
		private Dictionary<int, Vector3> localTags = new();
		private Transform originAnchor = null;
		private Matrix4x4 originAnchorTarget = Matrix4x4.identity;

		public ReadOnlyDictionary<int, Pose> CanonTags;
		public ReadOnlyDictionary<int, Vector3> LocalTags;
		public HashSet<int> LockedTags => lockedTags;

		[SerializeField] private float tagLerp = 0.1f;

		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")] public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")] public float maxHeadAngSpeed = 2f;
		
		public float minSnapPosDelta = 1f;
		public float minSnapRotDelta = Mathf.PI / 4f;

		public bool ColocationActive => colocationActive;

		[SerializeField] private CameraReader cameraReader;
		[SerializeField] private AprilTagTracker tagTracker;

		public CameraReader CameraReader => cameraReader;
		public AprilTagTracker TagTracker => tagTracker;

		private void Awake()
		{
			cameraReader = FindAnyObjectByType<CameraReader>();
			tagTracker = FindAnyObjectByType<AprilTagTracker>();

			CanonTags = new ReadOnlyDictionary<int, Pose>(canonTags);
			LocalTags = new ReadOnlyDictionary<int, Vector3>(localTags);
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

		private void OnRecenter()
		{
			var anchorMat = originAnchor.localToWorldMatrix;
			MainXRRig.Instance.AlignSpace(anchorMat, originAnchorTarget);
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

		private bool _isColocated;
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

		public float tagSize = 0.1f;
		private bool colocationActive = false;

		public async void Colocate()
		{
			if (!colocationActive)
			{
				NetworkManager.OnClientConnectedCallback += OnClientConnected;
				OVRManager.display.RecenteredPose += OnRecenter;
			}
			
			IsColocated = false;
			colocationActive = true;

			await cameraReader.TryOpenCamera();
			tagTracker.tagSizeMeters = tagSize;
			tagTracker.OnDetectTags += OnDetectTags;

			if (originAnchor)
				return;

			GameObject g = CreateAnchor(Vector3.zero, Quaternion.identity);
			originAnchor = g.transform;
		}

		private List<float3> sharedLocalPositions = new();
		private List<float3> sharedCanonPositions = new();

		private bool TagIsWithinRegisterDistance(Vector3 globalPos)
		{
			var headPos = MainXRRig.Camera.transform.position;
			return Vector3.Distance(headPos, globalPos) < tagSize * lockDistanceScale;
		}

		private void FixedUpdate()
		{
			if (!XRSettings.enabled)
				return;

			if (IsOwner)
				foreach (var id in canonTags.Keys)
				{
					var canonPose = canonTags[id];
					if (!TagIsWithinRegisterDistance(canonPose.position))
						lockedTags.Add(id);
				}
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!colocationActive)
				return;

			foreach (var result in results)
			{
				var globalPos = result.Position;

				var worldToTracking = MainXRRig.TrackingSpace.worldToLocalMatrix;
				var localPos = worldToTracking.MultiplyPoint(globalPos);
				localTags[result.ID] = localPos;

				if (IsOwner)
				{
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

					if (headIsStable)
					{
						var locked = lockedTags.Contains(result.ID);
						var isCloseEnough = TagIsWithinRegisterDistance(globalPos);
						if (!locked && isCloseEnough)
						{
							var pose = new Pose(globalPos, result.Rotation);
							RegisterCanonTagRpc(result.ID, pose);
						}
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

			var space = MainXRRig.TrackingSpace;
			var spaceMat = MainXRRig.TrackingSpace.localToWorldMatrix;

			var lerp = IsColocated ? 0.2f : 1f;

			switch (sharedLocalPositions.Count)
			{
				case 0:
					return;

				case >= 3:
				{
					Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
						sharedLocalPositions.ToArray(), spaceMat,
						sharedCanonPositions.ToArray(), float4x4.identity);

					var aligned = delta * spaceMat;
					
					MainXRRig.Instance.AlignSpace(spaceMat, aligned, lerp);
					break;
				}
				default:
				{
					foreach (var result in results)
					{
						int id = result.ID;
						if (!canonTags.ContainsKey(id) || !localTags.ContainsKey(id))
							return;
						
						var one = Vector3.one;
						var tagMat = Matrix4x4.TRS(result.Position, result.Rotation, one);
						var canonTag = canonTags[id];
						var canonTagMat = Matrix4x4.TRS(canonTag.position, canonTag.rotation, one);

						MainXRRig.Instance.AlignSpace(tagMat, canonTagMat, lerp);
					}
					break;
				}
			}

			var spacePos = space.position;
			if (spacePos.magnitude > 10000f ||
			    float.IsNaN(spacePos.x) || float.IsInfinity(spacePos.x) ||
			    float.IsNaN(spacePos.y) || float.IsInfinity(spacePos.y) ||
			    float.IsNaN(spacePos.z) || float.IsInfinity(spacePos.z))
				MainXRRig.TrackingSpace.SetWorldPose(Pose.identity);
			
			originAnchorTarget = MainXRRig.TrackingSpace.worldToLocalMatrix;

			IsColocated = true;
		}

		public void StopColocation()
		{
			cameraReader.CloseCamera();
			tagTracker.OnDetectTags -= OnDetectTags;
			NetworkManager.OnClientConnectedCallback -= OnClientConnected;
			OVRManager.display.RecenteredPose -= OnRecenter;

			colocationActive = false;
			IsColocated = false;

			lockedTags.Clear();
			canonTags.Clear();
			localTags.Clear();
			
			if(originAnchor != null)
				Destroy(originAnchor.gameObject);
		}

		public override void OnNetworkDespawn()
		{
			if(colocationActive)
				StopColocation();
		}

		private static GameObject CreateAnchor(Vector3 pos, Quaternion rot)
		{
			GameObject g = new GameObject("Origin Anchor");
			g.transform.SetPositionAndRotation(pos, rot);
			g.AddComponent<OVRSpatialAnchor>();
			return g;
		}
	}
}