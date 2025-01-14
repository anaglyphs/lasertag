using Anaglyph.Netcode;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.SharedSpaces
{
	[Serializable]
	public struct NetworkGuid : INetworkSerializeByMemcpy
	{
		public NetworkGuid(Guid guid)
		{
			this.guid = guid;
		}

		public Guid guid;
	}

	[RequireComponent(typeof(OVRSpatialAnchor))]
	public class NetworkedSpatialAnchor : NetworkBehaviour
	{
		[SerializeField] private OVRSpatialAnchor spatialAnchor;
		public OVRSpatialAnchor Anchor => spatialAnchor;

		//private Guid serverUuid;
		public NetworkVariable<NetworkPose> OriginalPoseSync = new NetworkVariable<NetworkPose>();
		public NetworkVariable<NetworkGuid> Uuid = new NetworkVariable<NetworkGuid>(new(Guid.Empty));
		public bool Localized => spatialAnchor.Localized;

		private void OnValidate()
		{
			TryGetComponent(out spatialAnchor);
		}

		private void Awake()
		{
			Uuid.OnValueChanged += OnGuidChanged;
			spatialAnchor.enabled = false;
		}

		private void OnGuidChanged(NetworkGuid previous, NetworkGuid current)
		{
			if (IsOwner || Uuid.Value.guid == Guid.Empty)
				return;

			StartCoroutine(DownloadAndLocalizeCoroutine(Uuid.Value.guid));
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				OriginalPoseSync.Value = new NetworkPose(transform);
				StartCoroutine(SaveCoroutine());
			}
			else
			{
				if (Uuid.Value.guid == Guid.Empty)
					return;

				StartCoroutine(DownloadAndLocalizeCoroutine(Uuid.Value.guid));
			}
		}

		private IEnumerator SaveCoroutine()
		{
			spatialAnchor.enabled = true;

			while (!spatialAnchor.Localized) yield return null;

			bool saveSuccess = false;
			bool currentlySaving = false;

			while (!saveSuccess)
			{
				if (!currentlySaving)
				{
					currentlySaving = true;

					Debug.Log($"Saving anchor {spatialAnchor.Uuid}...");

					OVRSpatialAnchor.SaveAnchorsAsync(new List<OVRSpatialAnchor> { spatialAnchor });

					var anchors = new List<OVRSpatialAnchor> { spatialAnchor };

					OVRSpatialAnchor.ShareAsync(anchors, spatialAnchor.Uuid).ContinueWith(result =>
					{
						if (result.Success)
						{
							Debug.Log($"Successfully saved anchor {spatialAnchor.Uuid}");
							saveSuccess = true;
						}
						else
						{
							Debug.LogError($"Failed to save anchor {spatialAnchor.Uuid}: {result.ToString()}");
						}

						currentlySaving = false;
					});

					while(currentlySaving)
						yield return null;
				}


				if (!saveSuccess)
				{
					Debug.Log("Save failed. Waiting to retry...");
					yield return new WaitForSeconds(5);
				}
			}

			Uuid.Value = new(spatialAnchor.Uuid);
		}

		private IEnumerator DownloadAndLocalizeCoroutine(Guid uuid)
		{
			if (uuid == Guid.Empty)
				yield break;

			bool downloadSuccess = false;
			bool currentlyDownloading = false;

			OVRSpatialAnchor.UnboundAnchor unboundAnchor = default;

			while (!downloadSuccess)
			{
				if (!currentlyDownloading)
				{
					currentlyDownloading = true;

					Debug.Log($"Loading anchor {uuid}...");

					List<OVRSpatialAnchor.UnboundAnchor> loadedAnchors = new();
					OVRSpatialAnchor.LoadUnboundSharedAnchorsAsync(Uuid.Value.guid, loadedAnchors).ContinueWith(result =>
					{
						if (loadedAnchors == null || loadedAnchors.Count == 0)
						{
							Debug.LogError($"Failed to loaded anchor {uuid}");
						}
						else
						{
							Debug.Log($"Loaded anchor {loadedAnchors[0].Uuid}");

							downloadSuccess = true;

							unboundAnchor = loadedAnchors[0];
						}

						currentlyDownloading = false;
					});

					while (currentlyDownloading)
						yield return null;
				}

				if (!downloadSuccess)
				{
					Debug.Log("Download failed. Waiting to retry...");
					yield return new WaitForSeconds(5);
				}
			}

			bool localizeSuccess = false;
			bool currentlyLocalizing = false;

			while (!localizeSuccess)
			{
				if (!currentlyLocalizing)
				{
					currentlyLocalizing = true;

					Debug.Log($"Attempting to localize and bind {unboundAnchor.Uuid}...");

					unboundAnchor.LocalizeAsync(10).ContinueWith(delegate (bool success)
					{
						if (success)
						{
							Debug.Log($"Localized anchor {unboundAnchor.Uuid} successfully");

							unboundAnchor.BindTo(spatialAnchor);
							spatialAnchor.enabled = true;

							localizeSuccess = true;
						}
						else
						{
							Debug.LogError($"Failed to localize anchor {unboundAnchor.Uuid}, will try again soon...");

							currentlyLocalizing = false;
						}
					});
				}

				yield return null;
			}
		}
	}
}