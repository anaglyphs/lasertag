using Anaglyph.XRTemplate.CameraReader;
using AprilTag;
using EnvisionCenter.XRTemplate.QuestCV;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;

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

		[SerializeField] private Mesh indicatorMesh;
		[SerializeField] private Material indicatorMaterial;

		[SerializeField] private Mesh debugPointMesh;
		[SerializeField] private Material debugPointMaterial;

		private MaterialPropertyBlock mpb;
		private TagPose[] tags;

		private void Awake()
		{
			mpb = new MaterialPropertyBlock();
		}

		private void OnEnable()
		{
			ProceduralDrawFeature.Draw += RenderFoundTags;
		}

		private void OnDisable()
		{
			ProceduralDrawFeature.Draw -= RenderFoundTags;
		}

		private async void Start()
		{
			manager.OnClientConnectedCallback += OnClientConnected;
			await EnsureConfigured();
		}

		private void RenderFoundTags(RasterCommandBuffer cmd)
		{
			if (tags != null)
			{
				mpb.SetColor("_BaseColor", Color.white);
				foreach (TagPose tagPose in tags)
				{
					var model = Matrix4x4.TRS(tagPose.Position, tagPose.Rotation, Vector3.one * tagSize * 3);
					cmd.DrawMesh(indicatorMesh, model, indicatorMaterial, 0, 0, mpb);
				}
			}

			if (Anaglyph.DebugMode)
			{
				mpb.SetColor("_BaseColor", Color.green);
				foreach (Vector3 canonTagPos in canonTags.Values)
				{
					var model = Matrix4x4.TRS(canonTagPos, Quaternion.identity, Vector3.one * 0.02f);
					cmd.DrawMesh(debugPointMesh, model, debugPointMaterial, 0, 0, mpb);
				}

				mpb.SetColor("_BaseColor", Color.yellow);
				foreach (Vector3 localTagPos in localTags.Values)
				{
					var model = MainXROrigin.Transform.localToWorldMatrix * 
						Matrix4x4.TRS(localTagPos, Quaternion.identity, Vector3.one * 0.02f);
					cmd.DrawMesh(debugPointMesh, model, debugPointMaterial, 0, 0, mpb);
				}
			}
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
			if (!colocationActive)
				return;

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

			if (sharedLocalPositions.Count >= 4)
			{
				Matrix4x4 trackingSpace = MainXROrigin.Transform.localToWorldMatrix;

				Matrix4x4 delta = IterativeClosestPoint.FitCorresponding(
					sharedLocalPositions.ToArray(), trackingSpace,
					sharedCanonPositions.ToArray(), float4x4.identity);

				// delta = FlattenRotation(delta);
				trackingSpace = delta * trackingSpace;
				delta = FlattenRotation(delta);
				MainXROrigin.Transform.position = trackingSpace.GetPosition();
				MainXROrigin.Transform.rotation = trackingSpace.rotation;

				IsColocated = true;
			}

			var originPos = MainXROrigin.Transform.position;

			if (originPos.magnitude > 100000f ||
				float.IsNaN(originPos.x) || float.IsInfinity(originPos.x) ||
				float.IsNaN(originPos.y) || float.IsInfinity(originPos.y) ||
				float.IsNaN(originPos.z) || float.IsInfinity(originPos.z))
			{
				MainXROrigin.Transform.SetWorldPose(Pose.identity);
			}
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

			tags = ((List<TagPose>)results).ToArray();

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
