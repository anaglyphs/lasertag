using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SharedSpacesXR
{
	public struct NetworkGuid : INetworkSerializeByMemcpy
	{
		public NetworkGuid(Guid guid)
		{
			this.guid = guid;
		}

		public Guid guid;
	}

	public class NetworkedSpatialAnchor : NetworkBehaviour
	{
		public static readonly List<NetworkedSpatialAnchor> allLocalizedAnchorManagers = new();

		[SerializeField] private OVRSpatialAnchor OVRSpatialAnchorPrefab;
		[NonSerialized] public OVRSpatialAnchor AttachedSpatialAnchor;

		//private Guid serverUuid;
		public NetworkVariable<NetworkGuid> Uuid = new NetworkVariable<NetworkGuid>(new(Guid.Empty));

		private void Awake()
		{
			Uuid.OnValueChanged += OnGuidChanged;
		}

		private void OnGuidChanged(NetworkGuid previous, NetworkGuid current)
		{
			RequestShareFromOwner();
		}

		private OVRSpatialAnchor CreateChildOVRSpatialAnchor()
		{
			AttachedSpatialAnchor = Instantiate(OVRSpatialAnchorPrefab, transform.position, transform.rotation);
			return AttachedSpatialAnchor;
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
			{
				AttachedSpatialAnchor = CreateChildOVRSpatialAnchor();

				StartCoroutine(WaitForAnchorLocalizationCoroutine(AttachedSpatialAnchor, delegate
				{
					allLocalizedAnchorManagers.Add(this);

					StartCoroutine(SaveCoroutine());
				}));
			}
			else
			{
				RequestShareFromOwner();
			}
		}

		public void RequestShareFromOwner()
		{
			if (!IsOwner && Uuid.Value.guid != Guid.Empty)
			{
				RequestShareRpc(NetworkManager.Singleton.LocalClientId, PlatformData.OculusUser.ID);
			}
		}

		[Rpc(SendTo.Owner)]
		private void RequestShareRpc(ulong returnTo, ulong targetOculusId)
		{
			StartCoroutine(ShareCoroutine(returnTo, new OVRSpaceUser(targetOculusId)));
		}

		[Rpc(SendTo.SpecifiedInParams)]
		private void ShareSuccessRpc(string uuid, RpcParams rpcParams)
		{
			StartCoroutine(DownloadAndLocalizeCoroutine(Guid.Parse(uuid)));
		}

		public void SetAnchorActive(bool active)
		{
			AttachedSpatialAnchor?.gameObject.SetActive(active);
		}

		private IEnumerator SaveCoroutine()
		{
			bool saveSuccess = false;
			bool currentlySaving = false;

			while (!saveSuccess)
			{
				if (!currentlySaving)
				{
					currentlySaving = true;

					OVRSpatialAnchor.SaveOptions saveOptions = new()
					{
						Storage = OVRSpace.StorageLocation.Cloud,
					};

					Debug.Log($"Saving anchor {AttachedSpatialAnchor.Uuid}...");

					OVRSpatialAnchor.SaveAsync(new List<OVRSpatialAnchor> { AttachedSpatialAnchor }, saveOptions).ContinueWith(result =>
					{
						if (result.IsSuccess())
						{
							Debug.Log($"Successfully saved anchor {AttachedSpatialAnchor.Uuid}");

							saveSuccess = true;
						}
						else
						{
							Debug.LogError($"Failed to save anchor {AttachedSpatialAnchor.Uuid}: {result.ToString()}");
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

			Uuid.Value = new(AttachedSpatialAnchor.Uuid);
		}

		private IEnumerator ShareCoroutine(ulong sendTo, OVRSpaceUser user)
		{
			bool shareSuccess = false;
			bool currentlySharing = false;

			while (!shareSuccess)
			{
				if (!currentlySharing)
				{
					currentlySharing = true;

					Debug.Log($"Sharing anchor {AttachedSpatialAnchor.Uuid} with {user.Id}...");

					OVRSpatialAnchor.ShareAsync(new List<OVRSpatialAnchor> { AttachedSpatialAnchor }, new List<OVRSpaceUser> { user }).ContinueWith(result =>
					{
						if (result.IsSuccess())
						{
							Debug.Log($"Successfully shared anchor {AttachedSpatialAnchor.Uuid} with {user.Id}");

							shareSuccess = true;
						}
						else
						{
							Debug.LogError($"Failed to share anchor {AttachedSpatialAnchor.Uuid} with {user.Id}: {result.ToString()}");
						}

						currentlySharing = false;
					});

					while (currentlySharing)
						yield return null;
				}

				if(!shareSuccess)
				{
					Debug.Log("Share failed. Waiting to retry...");
					yield return new WaitForSeconds(5);
				}
			}

			ShareSuccessRpc(AttachedSpatialAnchor.Uuid.ToString(), RpcTarget.Single(sendTo, RpcTargetUse.Temp));
		}

		private IEnumerator DownloadAndLocalizeCoroutine(Guid uuid)
		{
			bool downloadSuccess = false;
			bool currentlyDownloading = false;

			OVRSpatialAnchor.UnboundAnchor unboundAnchor = default;

			while (!downloadSuccess)
			{
				if (!currentlyDownloading)
				{
					currentlyDownloading = true;

					OVRSpatialAnchor.LoadOptions loadOptions = new()
					{
						Timeout = 0,
						StorageLocation = OVRSpace.StorageLocation.Cloud,
						Uuids = new Guid[] { uuid }
					};

					Debug.Log($"Loading anchor {uuid}...");

					OVRSpatialAnchor.LoadUnboundAnchorsAsync(loadOptions).ContinueWith(loadedAnchors =>
					{
						if (loadedAnchors == null || loadedAnchors.Length == 0)
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

							AttachedSpatialAnchor = CreateChildOVRSpatialAnchor();

							unboundAnchor.BindTo(AttachedSpatialAnchor);

							localizeSuccess = true;

							allLocalizedAnchorManagers.Add(this);

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

		public override void OnDestroy()
		{
			base.OnDestroy();

			allLocalizedAnchorManagers.Remove(this);

			if (AttachedSpatialAnchor != null)
			{
				Destroy(AttachedSpatialAnchor.gameObject);
			}
		}

		public IEnumerator WaitForAnchorLocalizationCoroutine(OVRSpatialAnchor anchor, Action onLocalized)
		{
			while(!anchor.Localized) yield return new WaitForEndOfFrame();

			onLocalized.Invoke();
		}

		public static void DespawnAll()
		{

			for (int i = 0; i < allLocalizedAnchorManagers.Count; i++)
			{
				// destruction is not immediate, so this works
				allLocalizedAnchorManagers[i].NetworkObject.Despawn();
			}
		}
	}
}