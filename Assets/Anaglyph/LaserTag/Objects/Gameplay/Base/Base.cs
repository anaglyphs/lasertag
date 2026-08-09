using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		public const float Radius = 1;
		public const float Height = 3;

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		[SerializeField] private MeshRenderer meshRenderer;

		public const string Tag = "Base";

		public static List<Base> AllBases { get; private set; } = new();

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Init() => AllBases = new List<Base>();

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Awake()
		{
			gameObject.tag = Tag;
			AllBases.Add(this);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}

		/// <summary>Bases are volumes, not colliders - a dead player has no hitbox but
		/// still has to be able to walk into one to respawn.</summary>
		public bool Contains(Vector3 point)
		{
			Vector3 local = point - transform.position;

			if (local.y < 0 || local.y > Height)
				return false;

			return new Vector2(local.x, local.z).sqrMagnitude < Radius * Radius;
		}
	}
}
