using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		public const float Radius = 1;

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		[SerializeField] private MeshRenderer meshRenderer;

		public const string Tag = "Base";

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Awake()
		{
			gameObject.tag = Tag;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			// OnTriggerExit isn't guaranteed to fire when a collider is destroyed
			// (e.g. this base gets deleted via the map editor), so proactively evict
			// this base from any player that's still holding a reference to it.
			foreach (PlayerAvatar player in PlayerAvatar.All.Values)
				player.ExitBase(this);
		}

		private void OnTriggerEnter(Collider other)
		{
			if (!other.CompareTag(PlayerAvatar.Tag)) return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player != null)
				player.EnterBase(this);
		}

		private void OnTriggerExit(Collider other)
		{
			if (!other.CompareTag(PlayerAvatar.Tag)) return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player != null)
				player.ExitBase(this);
		}
	}
}