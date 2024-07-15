using Anaglyph.Lasertag;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.LaserTag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		public const float Radius = 1;
		private static int ColorID = Shader.PropertyToID("_Color");

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		public static List<Base> AllBases { get; private set; } = new ();

		[SerializeField] private MeshRenderer meshRenderer;

		public UnityEvent<byte> OnTeamChange => teamOwner.OnTeamChange;

		private void Awake()
		{
			AllBases.Add(this);
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				teamOwner.teamSync.Value = MainPlayer.Instance.team;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}
	}
}