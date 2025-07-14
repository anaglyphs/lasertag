using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		[SerializeField] private NetworkedAnchor anchorPrefab;

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

		public async void Colocate()
		{
			IsColocated = false;

			var findInactive = FindObjectsInactive.Include;
			var sortMode = FindObjectsSortMode.None;
			NetworkedAnchor[] allAnchors = FindObjectsByType<NetworkedAnchor>(findInactive, sortMode);

			if (allAnchors.Length == 0)
			{
				// spawn anchor
				Transform head = MainXROrigin.Instance.Camera.transform;

				Vector3 spawnPos = head.position;
				spawnPos.y = 0;

				Vector3 flatForward = head.transform.forward;
				flatForward.y = 0;
				flatForward.Normalize();
				Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

				GameObject g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
				g.TryGetComponent(out NetworkedAnchor networkedAnchor);
				networkedAnchor.NetworkObject.Spawn();

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

				await networkedAnchor.Share();
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
			NetworkedAnchor closestAnchor = NetworkedAnchor.AllAnchored[0];

			for (int i = 1; i < NetworkedAnchor.AllAnchored.Count; i++)
			{
				NetworkedAnchor candidate = NetworkedAnchor.AllAnchored[i];

				float dist = Vector3.SqrMagnitude(headPos - candidate.transform.position);

				if (dist < maxDist)
				{
					maxDist = dist;
					closestAnchor = candidate;
				}
			}

			Pose anchorPose = closestAnchor.transform.GetWorldPose();
			Colocation.TransformOrigin(anchorPose, Colocation.VerticallyAlignPose(closestAnchor.DesiredPose));
			IsColocated = true;
		}

		public void StopColocation()
		{
			colocationActive = false;
			IsColocated = false;
		}
	}
}
