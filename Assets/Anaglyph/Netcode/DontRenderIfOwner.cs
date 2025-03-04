using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
    public class DontRenderIfOwner : NetworkBehaviour
    {
		[SerializeField] private new Renderer renderer;

		private void OnValidate()
		{
			TryGetComponent(out renderer);
		}

		private void Start() => Handle();
		public override void OnNetworkSpawn() => Handle();
		protected override void OnOwnershipChanged(ulong a, ulong b) => Handle();

		private void Handle() => renderer.enabled = !IsOwner;
	}
}
