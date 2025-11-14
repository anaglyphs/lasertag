using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag.Networking
{
	[DefaultExecutionOrder(500)]
	public class Base : NetworkBehaviour
	{
		public const float Radius = 1;

		[SerializeField] private TeamOwner teamOwner;
		public TeamOwner TeamOwner => teamOwner;
		public byte Team => teamOwner.Team;

		public static List<Base> AllBases { get; private set; } = new ();

		[SerializeField] private MeshRenderer meshRenderer;

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Awake()
		{
			AllBases.Add(this);
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			AllBases.Remove(this);
		}
	}
}