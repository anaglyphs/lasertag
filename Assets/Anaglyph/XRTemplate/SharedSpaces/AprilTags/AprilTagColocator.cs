using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class AprilTagColocator : NetworkBehaviour, IColocator
	{
		private HashSet<int> tagsLocked = new();
		private Dictionary<int, Vector3> canonTags = new();
		private Dictionary<int, Vector3> localTags = new();

		[SerializeField] private Vector2Int texSize = new(1280, 960);

		public float lockDistanceScale = 10;
		[Tooltip("In meters/second")]
		public float maxHeadSpeed = 2f;
		[Tooltip("In radians/second")]
		public float maxHeadAngSpeed = 2f;

		private static NetworkManager manager => NetworkManager.Singleton;

		private async void Start()
		{
			manager.OnClientConnectedCallback += OnClientConnected;
			await EnsureConfigured();
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
			if (IsOwner && id != manager.LocalClientId)
			{
				int[] keys = new int[canonTags.Count];
				Vector3[] values = new Vector3[canonTags.Count];

				canonTags.Keys.CopyTo(keys, 0);
				canonTags.Values.CopyTo(values, 0);

				SyncCanonTagsRpc(keys, values, RpcTarget.Single(id, RpcTargetUse.Temp));
			}
		}

		private async Task EnsureConfigured()
		{
			if (!CameraManager.Instance.IsConfigured)
				await CameraManager.Instance.Configure(1, texSize.x, texSize.y);
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

			await CameraManager.Instance.TryOpenCamera();
			AprilTagTracker.Instance.tagSizeMeters = tagSize;
			AprilTagTracker.Instance.OnDetectTags += OnDetectTags;
		}

		private List<float3> sharedLocalPositions = new();
		private List<float3> sharedCanonPositions = new();

		private void LateUpdate()
		{
			if (IsOwner)
			{
				var headPos = MainXROrigin.Instance.Camera.transform.position;

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

			if (sharedLocalPositions.Count < 3)
				return;

			float4x4 trackingSpace = MainXROrigin.Transform.localToWorldMatrix;

			Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
				sharedLocalPositions.ToArray(), trackingSpace, 
				sharedCanonPositions.ToArray(), Matrix4x4.identity);
			// delta = FlattenRotation(delta);
			var newMat = MainXROrigin.Transform.localToWorldMatrix * delta;

			MainXROrigin.Transform.position = newMat.GetPosition();
			MainXROrigin.Transform.rotation = newMat.rotation;
		}

		private bool TagIsWithinRegisterDistance(Vector3 globalPos)
		{
			Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;
			return Vector3.Distance(headPos, globalPos) < tagSize * lockDistanceScale;
		}

		public static Matrix4x4 FlattenRotation(Matrix4x4 m)
		{
			// Extract translation
			Vector3 pos = m.GetColumn(3);

			// Extract and normalize forward
			Vector3 forward = m.GetColumn(2);
			forward.y = 0f;

			if (forward.sqrMagnitude < 1e-6f)
			{
				// Forward was almost vertical, fallback to right vector projection
				Vector3 right = m.GetColumn(0);
				forward = new Vector3(right.x, 0f, right.z);
				if (forward.sqrMagnitude < 1e-6f)
					forward = Vector3.forward;
			}

			forward.Normalize();

			// Build new orthonormal basis
			Vector3 rightNew = Vector3.Normalize(Vector3.Cross(Vector3.up, forward));
			Vector3 upNew = Vector3.Cross(forward, rightNew);

			// Assemble new matrix (pure rotation + original translation)
			Matrix4x4 result = Matrix4x4.identity;
			result.SetColumn(0, new Vector4(rightNew.x, rightNew.y, rightNew.z, 0f));
			result.SetColumn(1, new Vector4(upNew.x, upNew.y, upNew.z, 0f));
			result.SetColumn(2, new Vector4(forward.x, forward.y, forward.z, 0f));
			result.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1f));

			return result;
		}

		private void OnDetectTags(IReadOnlyList<TagPose> results)
		{
			if (!colocationActive)
				return;

			foreach (TagPose result in results)
			{
				Vector3 globalPos = result.Position;

				Matrix4x4 worldToTracking = MainXROrigin.Transform.worldToLocalMatrix;
				Vector3 localPos = worldToTracking.MultiplyPoint(globalPos);

				localTags[result.ID] = localPos;

				if (IsOwner)
				{
					bool locked = tagsLocked.Contains(result.ID);

					if(!locked && TagIsWithinRegisterDistance(globalPos))
					{
						RegisterCanonTagRpc(result.ID, globalPos);
					}
				}
			}
		}

		public void StopColocation()
		{
			CameraManager.Instance.CloseCamera();
			AprilTagTracker.Instance.OnDetectTags -= OnDetectTags;

			colocationActive = false;
			IsColocated = false;

			tagsLocked.Clear();
			canonTags.Clear();
			localTags.Clear();
	}
	}
}
