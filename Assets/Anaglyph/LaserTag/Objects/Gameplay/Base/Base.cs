using System.Collections.Generic;
using Anaglyph.LaserTag.Player;
using Anaglyph.LaserTag.Player.Teams;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Objects.Gameplay.Base
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		[SerializeField] private MeshRenderer meshRenderer;
		[SerializeField] private Collider volume;

		public const string Tag = "Base";

		public static List<Base> AllBases { get; private set; } = new();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init() => AllBases = new List<Base>();

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
			TryGetComponent(out volume);
		}

		private void Awake()
		{
			gameObject.tag = Tag;
			AllBases.Add(this);
		}

		// Only shooters collide their own bullets and grenades, so each peer's bases need only
		// be solid against its own player: open while their head is inside, so they can fire
		// out, and closed otherwise, so they can't fire in
		private void Update()
		{
			PlayerAvatar local = PlayerAvatar.Local;
			volume.isTrigger = local != null && local.HasSpatialPresence
				&& Contains(local.HeadTransform.position);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}

		/// <summary>Membership is tested against the volume rather than by contact - a dead
		/// player has no hitbox but still has to be able to walk into one to respawn.</summary>
		public bool Contains(Vector3 point)
		{
			// ClosestPoint returns the point itself when it's inside
			return volume.ClosestPoint(point) == point;
		}
	}
}
