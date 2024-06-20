using Anaglyph.LaserTag;
using Anaglyph.LaserTag.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
    public class ControlPoint : NetworkBehaviour
    {
		public NetworkVariable<int> controllingTeam = new(-1, writePerm: NetworkVariableWritePermission.Owner);

		public NetworkVariable<float> radius = new(1.5f, writePerm: NetworkVariableWritePermission.Owner);
		public NetworkVariable<float> millisToTake = new(15000, writePerm: NetworkVariableWritePermission.Owner);

		public NetworkVariable<float> millisTaken = new(0, writePerm: NetworkVariableWritePermission.Owner);

		private bool stalemate;

		public UnityEvent<int> onConqueredByTeam;

		private void FixedUpdate()
		{
			if (!IsOwner)
				return;

			int conqueringTeam = -1;

			bool withinRadius = Vector3.Distance(PlayerLocal.Instance.LocalHeadTransform.position, transform.position) < radius.Value;

			foreach(Player player in Player.AllPlayers)
			{
				float playerHeadDistance = Vector3.Distance(PlayerLocal.Instance.LocalHeadTransform.position, transform.position);
				bool playerIsWithinRadius = playerHeadDistance < radius.Value;

				if(playerIsWithinRadius)
				{
					if (conqueringTeam == -1)
						conqueringTeam = player.Team;
					else if(player.Team != conqueringTeam)
					{
						// stalemate
						return;
					}
				}
			}

			if (conqueringTeam == -1 && conqueringTeam != controllingTeam.Value)
			{
				millisTaken.Value = Mathf.Max(0, millisTaken.Value - Time.fixedDeltaTime);
			}

			millisTaken.Value += Time.fixedDeltaTime * 1000;

			if(millisTaken.Value > millisToTake.Value)
			{
				Conquer(conqueringTeam);
			}
		}

		public void Conquer(int team)
		{
			if (!IsOwner)
				return;

			if (team == controllingTeam.Value)
				return;

			controllingTeam.Value = team;
			millisTaken.Value = 0;
			onConqueredByTeam.Invoke(team);
		}
	}
}
