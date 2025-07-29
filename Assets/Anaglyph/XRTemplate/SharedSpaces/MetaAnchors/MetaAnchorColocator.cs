using System;
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

		public void InstantiateNewAnchor()
		{
			var currentAnchor = ColocationAnchor.Instance;

			if (currentAnchor != null)
			{
				ColocationAnchor.Instance.NetworkObject.Despawn(true);
			}

			Transform head = MainXROrigin.Instance.Camera.transform;

			Vector3 spawnPos = head.position;
			spawnPos.y -= 1.5f;

			Vector3 flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
			Debug.Log($"Instantiated new anchor");

			g.TryGetComponent(out NetworkObject networkObject);
			networkObject.Spawn();
		}

		public void Colocate()
		{
			IsColocated = false;

			var inactive = FindObjectsInactive.Include;

			NetworkedAnchor anchors = FindAnyObjectByType<NetworkedAnchor>(inactive);

			if (anchors == null)
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
				MainXROrigin.Transform.position = new Vector3(0, 1000, 0);
			}

			colocationActive = true;
		}

		private void LateUpdate()
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

			if (anchor.IsAnchored)
			{
				Pose anchorPose = anchor.transform.GetWorldPose();
				Colocation.TransformTrackingSpace(anchorPose, anchor.DesiredPose);
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
