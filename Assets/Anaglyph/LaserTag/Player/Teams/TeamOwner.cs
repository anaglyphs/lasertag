using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
    public class TeamOwner : NetworkBehaviour
    {
        public NetworkVariable<byte> teamSync;

        public byte Team => teamSync.Value;

        public event Action<byte> TeamChanged = delegate { };
		[SerializeField] private UnityEvent<byte> OnTeamChange = new();

		private void Awake()
		{
			teamSync.OnValueChanged += OnValueChanged;
		}
		
		public override void OnNetworkSpawn()
		{
			OnValueChanged(0, Team);
		}
		
		private void OnValueChanged(byte previousValue, byte newValue)
		{
			TeamChanged.Invoke(Team);
			OnTeamChange.Invoke(Team);
		}
	}
}
