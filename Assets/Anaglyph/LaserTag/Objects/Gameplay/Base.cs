using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.LaserTag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		public const float Radius = 1;
		private int ColorID = Shader.PropertyToID("_Color");

		public int TeamNumber => teamNumberSync.Value;
		private NetworkVariable<int> teamNumberSync = new NetworkVariable<int>(1, writePerm: NetworkVariableWritePermission.Owner);

		[SerializeField] private MeshRenderer meshRenderer;

		public static List<Base> AllBases = new();

		private void Awake()
		{
			meshRenderer.material = new Material(meshRenderer.sharedMaterial);
		}

		public override void OnNetworkSpawn()
		{
			AllBases.Add(this);

			if (IsOwner)
				teamNumberSync.Value = MainPlayer.Instance.Team;
		}

		public override void OnNetworkDespawn() => AllBases.Remove(this);

		private void UpdateAppearance()
		{
			Color color = TeamNumber == MainPlayer.Instance.Team ? Color.green : Color.red;
			meshRenderer.material.SetColor(ColorID, color);
		}

		private void Update()
		{
			UpdateAppearance();
		}
	}
}