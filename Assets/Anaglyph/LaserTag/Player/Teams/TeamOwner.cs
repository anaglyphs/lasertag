using Unity.Netcode;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
    public class TeamOwner : NetworkBehaviour
    {
        public NetworkVariable<byte> teamSync;

        public byte Team => teamSync.Value;

        public UnityEvent<byte> OnTeamChange = new();

		private void Awake()
		{
			teamSync.OnValueChanged += delegate
			{
				OnTeamChange.Invoke(Team);
			};
		}

		private void Start()
		{
			OnTeamChange.Invoke(Team);
		}
	}
}
