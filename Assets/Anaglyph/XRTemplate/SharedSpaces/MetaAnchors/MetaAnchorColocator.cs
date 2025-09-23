using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		public static MetaAnchorColocator Current { get; private set; }

		//[SerializeField] private float anchorRespawnDistance = 10;
		[SerializeField] private ColocationAnchor anchorPrefab;

		private bool colocationActive;

		private static bool _isColocated;
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

		private void Awake()
		{
			Current = this;
		}

		private void Start()
		{
			OVRManager.display.RecenteredPose += TryAlignPose;
		}

		private void OnDestroy()
		{
			if(OVRManager.display != null)
				OVRManager.display.RecenteredPose -= TryAlignPose;
		}

		public void InstantiateNewAnchor()
		{
			if(ColocationAnchor.Instance != null)
				ColocationAnchor.Instance.DespawnAndDestroyRpc();

			Transform head = MainXRRig.Camera.transform;

			Vector3 spawnPos = head.position;
			spawnPos.y -= 1.5f;

			Vector3 flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
			// Debug.Log($"Instantiated new anchor");

			g.TryGetComponent(out NetworkObject networkObject);
			networkObject.Spawn();

			g.TryGetComponent(out OVRSpatialAnchor anchor);
			
			if (anchor.Localized)
				TryAlignPose();
			else
				anchor.OnLocalize += OnLocalizeAnchor;
		}

		private async void OnLocalizeAnchor(OVRSpatialAnchor.OperationResult status)
		{
			if (status != OVRSpatialAnchor.OperationResult.Success)
				return;

			await Awaitable.EndOfFrameAsync();

			TryAlignPose();
		}

		public async void Colocate()
		{
			IsColocated = false;

			await Awaitable.EndOfFrameAsync();

			if (ColocationAnchor.Instance == null)
			{
				// spawn anchor
				InstantiateNewAnchor();

				// try to bind with an existing saved anchor
				// SavedAnchorGuids savedAnchors = LoadSavedGuids();

				//if (savedAnchors.guidStrings != null && savedAnchors.guidStrings.Count > 0)
				//{
				//	List<UnboundAnchor> unboundAnchors = new();

				//	List<Guid> guidStrings = new(savedAnchors.guidStrings.Count);
				//	foreach (string uuidString in savedAnchors.guidStrings)
				//		guidStrings.Add(new Guid(uuidString));

				//	var loadResult = await LoadUnboundAnchorsAsync(guidStrings, unboundAnchors);
				//	if (loadResult.Success && unboundAnchors.Count > 0)
				//	{
				//		await networkedAnchor.LocalizeAndBindAsync(unboundAnchors[0]);
				//	}
				//}
			}
			else
			{
				MainXRRig.TrackingSpace.position = new Vector3(0, 1000, 0);
			}

			colocationActive = true;
		}

		private void TryAlignPose()
		{
			var anchor = ColocationAnchor.Instance;

			if (!colocationActive || anchor == null)
				return;

			//if (anchor.IsOwner)
			//{
			//	Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;
			//	Vector3 anchorPos = anchor.transform.position;
			//	if (Vector3.Distance(headPos, anchorPos) > anchorRespawnDistance)
			//	{
			//		anchor.NetworkObject.Despawn();
			//		InstantiateNewAnchor();
			//	}
			//}

			if (anchor.IsLocalized)
			{
				Pose anchorPose = anchor.transform.GetWorldPose();
				MainXRRig.MatchPoseToTarget(anchorPose, anchor.DesiredPose);
				IsColocated = true;
			}
		}

		public void StopColocation()
		{
			colocationActive = false;
			IsColocated = false;
		}
	}
}
