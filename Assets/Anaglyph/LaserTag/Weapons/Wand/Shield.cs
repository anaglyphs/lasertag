using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class Shield : NetworkBehaviour
    {
		[SerializeField] private float lifetime;

		public override void OnNetworkSpawn()
		{
			if(IsOwner)
				StartCoroutine(TimedDespawn());
		}

		private IEnumerator TimedDespawn()
		{
			yield return new WaitForSeconds(lifetime);
			NetworkObject.Despawn();
		}
    }
}
