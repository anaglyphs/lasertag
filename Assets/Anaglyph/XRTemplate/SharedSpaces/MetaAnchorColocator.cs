using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using static Anaglyph.XRTemplate.SharedSpaces.AnchorGuidSaving;
using static OVRSpatialAnchor;

namespace Anaglyph.SharedSpaces
{
	public class MetaAnchorColocator : IColocator
	{
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
			var findInactive = FindObjectsInactive.Include;
			var sortMode = FindObjectsSortMode.None;
			NetworkedAnchor[] allAnchors = GameObject.FindObjectsByType<NetworkedAnchor>(findInactive, sortMode);

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

				GameObject prefab = Resources.Load<GameObject>("Networked Colocation Anchor");
				GameObject g = GameObject.Instantiate(prefab, spawnPos, spawnRot);
				g.TryGetComponent(out NetworkedAnchor networkedAnchor);
				networkedAnchor.NetworkObject.Spawn();

				// try to bind with an existing saved anchor
				SavedAnchorGuids savedAnchors = LoadSavedGuids();

				if (savedAnchors.guidStrings != null && savedAnchors.guidStrings.Count > 0)
				{
					List<UnboundAnchor> unboundAnchors = new();

					List<Guid> guidStrings = new(savedAnchors.guidStrings.Count);
					foreach (string uuidString in savedAnchors.guidStrings)
						guidStrings.Add(new Guid(uuidString));

					var loadResult = await LoadUnboundAnchorsAsync(guidStrings, unboundAnchors);
					if (loadResult.Success && unboundAnchors.Count > 0)
					{
						await networkedAnchor.LocalizeAndBindAsync(unboundAnchors[0]);
					}
				}

				await networkedAnchor.Share();
			}
			else
			{
				MainXROrigin.Transform.position = new Vector3(0, 1000, 0);
			}

			colocationActive = true;

			while (colocationActive)
			{
				await Awaitable.NextFrameAsync();

				if (NetworkedAnchor.AllAnchored.Count == 0)
					continue;

				// find closest anchor
				Vector3 headPos = MainXROrigin.Instance.Camera.transform.position;
				float maxDist = float.MaxValue;
				NetworkedAnchor closestAnchor = NetworkedAnchor.AllAnchored[0];

				for (int i = 1; i < NetworkedAnchor.AllAnchored.Count; i++)
				{
					NetworkedAnchor candidate = NetworkedAnchor.AllAnchored[i];

					float dist = Vector3.SqrMagnitude(headPos - candidate.transform.position);
					float newMaxDist = Mathf.Min(maxDist, dist);

					if (newMaxDist < maxDist)
						closestAnchor = candidate;
				}

				Pose anchorPose = closestAnchor.transform.GetWorldPose();
				Colocation.TransformTrackingSpace(anchorPose, closestAnchor.DesiredPose);
			}
		}

		public void StopColocation()
		{
			colocationActive = false;
			IsColocated = false;
		}
	}
}