using Anaglyph.LaserTag.Logistics;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Player : NetworkBehaviour
	{
		public Transform HeadTransform;
		public Transform LeftHandTransform;
		public Transform RightHandTransform;
		//public Transform ChestTransform;

		[SerializeField] private GameObject[] deactivateIfOwner;
		[SerializeField] private MonoBehaviour[] enabledIfOwner;

		public UnityEvent onRespawn = new();
		public UnityEvent onKilled = new();
		public UnityEvent onDamaged = new();

		public NetworkVariable<bool> isAliveSync = new NetworkVariable<bool>
			(true, writePerm: NetworkVariableWritePermission.Owner);

		private NetworkVariable<NetworkPose> headPoseSync = new NetworkVariable<NetworkPose>
			(writePerm: NetworkVariableWritePermission.Owner);

		private NetworkVariable<NetworkPose> leftHandPoseSync = new NetworkVariable<NetworkPose>
			(writePerm: NetworkVariableWritePermission.Owner);

		private NetworkVariable<NetworkPose> rightHandPoseSync = new NetworkVariable<NetworkPose>
			(writePerm: NetworkVariableWritePermission.Owner);

		private NetworkVariable<int> team = new NetworkVariable<int> 
			(writePerm: NetworkVariableWritePermission.Owner);

		public int Team => team.Value;

		//private NetworkVariable<NetworkPose> chestPoseSync = new NetworkVariable<NetworkPose>
		//	(writePerm: NetworkVariableWritePermission.Owner);

		public static List<Player> AllPlayers = new();

        private void Awake()
		{
            isAliveSync.OnValueChanged += (wasAlive, isAlive) =>
			{
				if (isAlive)
				{
					onRespawn.Invoke();
				}
				else
				{
					onKilled.Invoke();
				}
			};
		}

		public override void OnNetworkSpawn()
        {
			isAliveSync.Value = true;
			isAliveSync.OnValueChanged.Invoke(isAliveSync.Value, isAliveSync.Value);

			foreach (MonoBehaviour m in enabledIfOwner)
			{
				m.enabled = IsOwner;
			}

			foreach(GameObject g in deactivateIfOwner)
			{
				g.SetActive(!IsOwner);
			}

			if(IsOwner)
			{
				PlayerLocal.Instance.networkPlayer = this;
            }

            AllPlayers.Add(this);
        }

		private void Update()
		{
			if (!IsSpawned)
				return;

			if (IsOwner)
			{
				headPoseSync.Value = new NetworkPose(HeadTransform);
				leftHandPoseSync.Value = new NetworkPose(LeftHandTransform);
				rightHandPoseSync.Value = new NetworkPose(RightHandTransform);
				//chestPoseSync.Value = new NetworkPose(ChestTransform);
            }
			else
			{
				HeadTransform.SetPositionAndRotation(headPoseSync.Value.position, headPoseSync.Value.rotation);
				LeftHandTransform.SetPositionAndRotation(leftHandPoseSync.Value.position, leftHandPoseSync.Value.rotation);
				RightHandTransform.SetPositionAndRotation(rightHandPoseSync.Value.position, rightHandPoseSync.Value.rotation);
				//ChestTransform.SetPositionAndRotation(chestPoseSync.Value.position, chestPoseSync.Value.rotation);
			}
		}

		[Rpc(SendTo.Everyone)]
		public void HitRpc(float damage)
		{
			onDamaged.Invoke();

			if (IsOwner)
			{
				PlayerLocal.Instance.TakeDamage(damage);
			}
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            AllPlayers.Remove(this);
        }
    }
}