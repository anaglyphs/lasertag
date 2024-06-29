using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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

		private void Awake()
		{
			meshRenderer.material = new Material(meshRenderer.sharedMaterial);
			AllBases.Add(this);
		}

		private void UpdateAppearance()
		{
			Color color = Team == MainPlayer.Instance.Team ? Color.green : Color.red;
			meshRenderer.material.SetColor(ColorID, color);
		}

		private void Update()
		{
			UpdateAppearance();
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