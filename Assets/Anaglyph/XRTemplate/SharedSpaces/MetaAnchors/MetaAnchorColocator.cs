using System;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Anaglyph.XRTemplate.SharedSpaces
{
	public class MetaAnchorColocator : MonoBehaviour, IColocator
	{
		public static MetaAnchorColocator Current { get; private set; }

		//[SerializeField] private float anchorRespawnDistance = 10;
		[SerializeField] private ColocationAnchor anchorPrefab;

		private static bool _isColocated;
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

		private void Awake()
		{
			Current = this;
		}

		public void InstantiateNewAnchor()
		{
			if (ColocationAnchor.Instance != null)
				ColocationAnchor.Instance.DespawnAndDestroyRpc();

			var head = MainXRRig.Camera.transform;

			var spawnPos = head.position;
			spawnPos.y -= 1.5f;

			var flatForward = head.transform.forward;
			flatForward.y = 0;
			flatForward.Normalize();
			var spawnRot = Quaternion.LookRotation(flatForward, Vector3.up);

			var g = Instantiate(anchorPrefab.gameObject, spawnPos, spawnRot);
			// Debug.Log($"Instantiated new anchor");

			g.TryGetComponent(out NetworkObject networkObject);
			networkObject.Spawn();
		}

		public async void Colocate()
		{
			if (!XRSettings.enabled)
				return;

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
				if (!IsColocated)
					MainXRRig.TrackingSpace.position = new Vector3(0, 1000, 0);
			}
		}

		public async void AlignTo(ColocationAnchor anchor)
		{
			if (!XRSettings.enabled)
				return;

			await Awaitable.EndOfFrameAsync();

			if (!anchor.IsLocalized)
				return;


			var anchorMat = anchor.transform.localToWorldMatrix;
			var dpose = anchor.DesiredPose;
			var targetMat = Matrix4x4.TRS(dpose.position, dpose.rotation, Vector3.one);
			MainXRRig.Instance.AlignSpace(anchorMat, targetMat);
			IsColocated = true;
		}

		public void StopColocation()
		{
			IsColocated = false;
		}
	}
}