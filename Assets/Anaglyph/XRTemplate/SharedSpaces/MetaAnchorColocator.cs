using Anaglyph.XRTemplate;
using Anaglyph.XRTemplate.SharedSpaces;
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using static Anaglyph.XRTemplate.SharedSpaces.AnchorGuidSaving;
using static OVRSpatialAnchor;

namespace Anaglyph.SharedSpaces
{
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		public static MetaAnchorColocator Instance { get; private set; }

		[SerializeField] private GameObject sharedAnchorPrefab;
		private NetworkedAnchor networkedAnchor;

		private Transform spawnTarget;

		private bool _isColocated;
		public event Action<bool> IsColocatedChange;
		private void SetIsColocated(bool b) => IsColocated = b;
		public bool IsColocated
		{
			get => _isColocated;
			set
			{
				bool changed = value != _isColocated;
				_isColocated = value;
				if (changed) 
					IsColocatedChange?.Invoke(_isColocated);
			}
		}

		private void Awake()
		{
			Instance = this;
			WorldLock.ActiveLockChange += OnActiveAnchorChange;
		}

		private void OnDestroy()
		{
			WorldLock.ActiveLockChange -= OnActiveAnchorChange;
		}

		private void OnActiveAnchorChange(WorldLock anchor)
		{
			IsColocated = anchor != null;
		}

		private void Start()
		{
			spawnTarget = Camera.main.transform;
		}

		public async void Colocate()
		{
			if (NetworkManager.Singleton.IsHost)
			{
				SpawnPrefab();

				SavedAnchors savedAnchors = GetSavedAnchors();

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
				MainXROrigin.TrackingSpace.position = new Vector3(0, 1000, 0);
			}
		}

		public void StopColocation()
		{
			IsColocated = false;

			if (networkedAnchor != null && networkedAnchor.IsSpawned)
				networkedAnchor.NetworkObject.Despawn();
		}

		public void SpawnPrefab()
		{
			Vector3 spawnPos = spawnTarget.position;
			spawnPos.y = 0;

			Vector3 flatForward = spawnTarget.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			Quaternion spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			GameObject g = Instantiate(sharedAnchorPrefab, spawnPos, spawnRot);
			g.TryGetComponent(out networkedAnchor);
			networkedAnchor.NetworkObject.Spawn();
		}
	}
}