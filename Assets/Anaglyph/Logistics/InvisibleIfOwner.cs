using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag
{
    public class InvisibleIfOwner : NetworkBehaviour
    {
		[SerializeField] private new Renderer renderer;

		private void OnValidate()
		{
			this.SetDefaultComponent(ref renderer);
		}

		private void Start() => Handle();
		public override void OnNetworkSpawn() => Handle();
		protected override void OnOwnershipChanged(ulong a, ulong b) => Handle();

		private void Handle() => renderer.enabled = !IsOwner;
	}
}
