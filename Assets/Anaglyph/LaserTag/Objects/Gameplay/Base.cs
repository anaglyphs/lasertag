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

		public int Team => teamSync.Value;
		private NetworkVariable<int> teamSync = new(1);

		public static List<Base> AllBases { get; private set; } = new ();

		[SerializeField] private MeshRenderer meshRenderer;

		public UnityEvent<int> OnTeamChange;

		private void Awake()
		{
			AllBases.Add(this);

			teamSync.OnValueChanged += delegate
			{
				OnTeamChange.Invoke(teamSync.Value);
			};
		}

		public override void OnNetworkSpawn()
		{
			if (IsOwner)
				teamSync.Value = MainPlayer.Instance.Team;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}
	}
}