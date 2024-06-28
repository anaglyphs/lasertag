using Anaglyph.LaserTag.Networking;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.LaserTag
{
	public class ControlPoint : NetworkBehaviour
	{
		public const float Radius = 1.5f;
		public const float MillisToTake = 10000;
		private int ColorID = Shader.PropertyToID("_Color");

		public UnityEvent<int> onConqueredByTeam;

		[SerializeField] private MeshRenderer meshRenderer;
		[SerializeField] private Image conquerTimeIndicator;

		public int ControllingTeam => controllingTeamSync.Value;
		private NetworkVariable<int> controllingTeamSync = new(-1, writePerm: NetworkVariableWritePermission.Owner);

		public int ConqueringTeam => conqueringTeamSync.Value;

		private NetworkVariable<int> conqueringTeamSync = new(-1, writePerm: NetworkVariableWritePermission.Owner);

		public float MillisTaken => millisTakenSync.Value;
		private NetworkVariable<float> millisTakenSync = new(0, writePerm: NetworkVariableWritePermission.Owner);

		private void Awake()
		{
			controllingTeamSync.OnValueChanged += OnConqueredByTeam;
			meshRenderer.material = new Material(meshRenderer.sharedMaterial);
		}

		private void UpdateAppearance()
		{
			Color color = Color.red;
			if (ControllingTeam == MainPlayer.Instance.Team)
				color = Color.green;
			else if (ConqueringTeam == MainPlayer.Instance.Team)
				color = Color.yellow;

			meshRenderer.material.SetColor(ColorID, color);
			conquerTimeIndicator.color = color;
		}

		private void Update()
		{
			UpdateAppearance();
		}

		private void OnConqueredByTeam(int prevValue, int value)
		{
			onConqueredByTeam.Invoke(ControllingTeam);
		}

		private bool PlayerIsInsidePoint(Player player)
		{
			if (!player.IsAlive)
				return false;

			Vector3 playerHeadPos = player.HeadTransform.position;
			return Geo.PointIsInCylinder(transform.position, Radius, 3, playerHeadPos);
		}

		private void UpdateOwner()
		{
			if (ConqueringTeam == ControllingTeam)
			{
				foreach (Player player in Player.AllPlayers)
				{
					if (PlayerIsInsidePoint(player) && player.Team != ControllingTeam)
						conqueringTeamSync.Value = player.Team;
				}
			}
			else
			{
				bool conqueringTeamIsInside = false;
				bool otherTeamIsInside = false;

				foreach (Player player in Player.AllPlayers)
				{
					if (PlayerIsInsidePoint(player))
					{
						if (player.Team == ConqueringTeam)
							conqueringTeamIsInside = true;
						else
							otherTeamIsInside = true;
					}
				}

				if (conqueringTeamIsInside)
				{
					if (!otherTeamIsInside)
					{
						millisTakenSync.Value += Time.fixedDeltaTime * 1000;

						if (millisTakenSync.Value > MillisToTake)
							Conquer(ConqueringTeam);
					}
				}
				else
				{
					millisTakenSync.Value = Mathf.Max(0, millisTakenSync.Value - Time.fixedDeltaTime * 1000);

					if (millisTakenSync.Value < 0.001)
						conqueringTeamSync.Value = ControllingTeam;
				}
			}
		}

		private void FixedUpdate()
		{
			conquerTimeIndicator.fillAmount = 1 - MillisTaken / MillisToTake;

			if (IsOwner)
				UpdateOwner();
		}

		public void Conquer(int team)
		{
			if (!IsOwner)
				return;

			if (team == controllingTeamSync.Value)
				return;

			controllingTeamSync.Value = team;
			millisTakenSync.Value = 0;
			onConqueredByTeam.Invoke(team);
		}
	}
}
