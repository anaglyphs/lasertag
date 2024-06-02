using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Anaglyph.LaserTag.Networking
{
    [DefaultExecutionOrder(500)]
    public class Base : NetworkBehaviour
    {
        [SerializeField]
        public int TeamNumber = -1;

        private NetworkVariable<int> teamNumberSync = new NetworkVariable<int>(-1, writePerm: NetworkVariableWritePermission.Owner);

        [SerializeField]
        private Text teamNumberText;

        [SerializeField]
        private Text overheadTeamNumberText;

        [SerializeField]
        private GameObject overheadTeamTextObject;

        public static List<Base> AllBases = new();

        public override void OnNetworkSpawn()
        {
            AllBases.Add(this);
        }

        private void LateUpdate()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
            {
                teamNumberSync.Value = TeamNumber;
            }
            else
            {
                TeamNumber = teamNumberSync.Value;
            }

            teamNumberText.text = $"Team\n{TeamNumber}";
            overheadTeamNumberText.text = $"Team {TeamNumber}";

            //overheadTeamTextObject.transform.LookAt(PlayerLocal.Instance.networkPlayer.HeadTransform.position);
            //overheadTeamTextObject.transform.rotation = Quaternion.Euler(0, overheadTeamTextObject.transform.rotation.eulerAngles.y, 0);
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            AllBases.Remove(this);
        }

        public void IncrementTeamNumber()
        {
            if (!IsOwner)
            {
                TakeOwnershipRpc(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                TeamNumber++;
            }
        }

        public void DecrementTeamNumber()
        {
            if (!IsOwner)
            {
                TakeOwnershipRpc(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                TeamNumber--;
            }
        }

        [Rpc(SendTo.Server)]
        public void TakeOwnershipRpc(ulong clientId)
        {
            this.NetworkObject.ChangeOwnership(clientId);
        }
    }
}