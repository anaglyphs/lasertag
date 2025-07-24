using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private float spawnNewAnchorDistance = 10;
		[SerializeField] private GameObject networkedColocationAnchorPrefab;
		[SerializeField] private Transform selectedAnchorIndicator;

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

		private async void InstantiateNewAnchor()
		{
			Transform head = MainXROrigin.Instance.Camera.transform;

			Vector3 spawnPos = head.position;
			spawnPos.y -= 1.5f;

			Vector3 flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject g = Instantiate(networkedColocationAnchorPrefab, spawnPos, spawnRot);
			g.name = $"Colocation meta anchor {NetworkedAnchor.AllInstances.Count}";
			Debug.Log($"Instantiated {g.name}");
			g.TryGetComponent(out NetworkedAnchor networkedAnchor);
			networkedAnchor.NetworkObject.Spawn();

			await networkedAnchor.Share();
		}

		public async void Colocate()
		{
			IsColocated = false;

			await Awaitable.WaitForSecondsAsync(0.1f);

			var sort = FindObjectsSortMode.None;
			var inactive = FindObjectsInactive.Include;

			NetworkedAnchor[] anchors = FindObjectsByType<NetworkedAnchor>(inactive, sort);

			if (anchors.Length == 0)
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
			if (!colocationActive || NetworkedAnchor.AllAnchored.Count == 0)
				return;

			// find closest anchor
			Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;
			float maxDist = float.MaxValue;

			NetworkedAnchor closestAnchored = NetworkedAnchor.AllInstances[0];

			for (int i = 0; i < NetworkedAnchor.AllInstances.Count; i++)
			{
				NetworkedAnchor anchor = NetworkedAnchor.AllInstances[i];

				float dist = Vector3.Distance(headPos, anchor.transform.position);

				if (dist < maxDist)
				{
					maxDist = dist;
					if (anchor.IsAnchored)
						closestAnchored = anchor;
				}
			}

			Pose anchorPose = closestAnchored.transform.GetWorldPose();
			Colocation.TransformTrackingSpace(anchorPose, Colocation.FlattenPoseRotation(closestAnchored.DesiredPose));
			IsColocated = true;

			if (maxDist > spawnNewAnchorDistance)
				InstantiateNewAnchor();

			selectedAnchorIndicator?.SetPositionAndRotation(anchorPose.position + Vector3.up * 0.01f, anchorPose.rotation);
		}

		public void StopColocation()
		{
			colocationActive = false;
			IsColocated = false;
		}
	}
}
