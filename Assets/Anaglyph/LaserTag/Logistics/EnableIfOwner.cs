using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class EnableIfOwner : NetworkBehaviour
    {
		[SerializeField] private Behaviour[] enabledIfOwner;
		[SerializeField] private Behaviour[] disableIfOwner;

		private void Start() => Handle();
		public override void OnNetworkSpawn() => Handle();
		protected override void OnOwnershipChanged(ulong a, ulong b) => Handle();

		private void Handle()
		{
			for (int i = 0; i < enabledIfOwner.Length; i++)
				enabledIfOwner[i].enabled = IsOwner;

			for (int i = 0; i < disableIfOwner.Length; i++)
				enabledIfOwner[i].enabled = !IsOwner;
		}
	}
}
