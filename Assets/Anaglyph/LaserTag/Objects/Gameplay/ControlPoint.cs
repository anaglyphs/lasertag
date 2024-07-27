using Anaglyph.Lasertag.Networking;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class ControlPoint : NetworkBehaviour
	{
		public const float Radius = 1.5f;
		public const float MillisToTake = 10000;

		public static List<ControlPoint> AllControlPoints { get; private set; } = new();

		public UnityEvent<byte> onControllingTeamChange = new();

		[SerializeField] private TeamOwner teamOwner;
		public byte ControllingTeam => teamOwner.Team;

		public byte CapturingTeam => capturingTeamSync.Value;
		private NetworkVariable<byte> capturingTeamSync = new(0);

		public float MillisCaptured => millisCapturedSync.Value;
		private NetworkVariable<float> millisCapturedSync = new(0);

		[SerializeField] private Image conquerTimeIndicator;
		[SerializeField] private TeamColorer teamColorer;

		public bool IsBeingCaptured;
		public bool IsStalemated;

		private void OnValidate()
		{
			this.SetComponent(ref teamOwner);
		}

		private void Awake()
		{
			AllControlPoints.Add(this);

			teamOwner.teamSync.OnValueChanged += delegate { 

				if(IsOwner)
					millisCapturedSync.Value = 0;
				
				onControllingTeamChange.Invoke(ControllingTeam);
			};
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			AllControlPoints.Remove(this);
		}

		private bool CheckIfPlayerIsInside(Player player)
		{
			if (!player.IsAlive)
				return false;

			Vector3 playerHeadPos = player.HeadTransform.position;
			return Geo.PointIsInCylinder(transform.position, Radius, 3, playerHeadPos);
		}

		private void ManagePointCapture()
		{
			if (!IsOwner)
				return;

			bool pointIsSecure = CapturingTeam == ControllingTeam;

			if (pointIsSecure)
			{
				// check for new capturing players
				foreach (Player player in Player.AllPlayers.Values)
				{
					if (CheckIfPlayerIsInside(player) && player.Team != ControllingTeam)
						capturingTeamSync.Value = player.Team;
				}
			}
			else
			{
				foreach (Player player in Player.AllPlayers.Values)
				{
					if (CheckIfPlayerIsInside(player))
					{
						if (player.Team == CapturingTeam)
							IsBeingCaptured = true;
						else
							IsStalemated = true;
					}
				}

				if (IsBeingCaptured)
				{
					if (!IsStalemated)
					{
						millisCapturedSync.Value += Time.fixedDeltaTime * 1000;

						if (millisCapturedSync.Value > MillisToTake)
							Capture(CapturingTeam);
					}
				}
				else
				{
					// capturing team loses capture over time
					if (millisCapturedSync.Value <= 0)
						capturingTeamSync.Value = ControllingTeam;

					millisCapturedSync.Value = Mathf.Max(0, millisCapturedSync.Value - Time.fixedDeltaTime * 1000);
				}
			}
		}

		private void UpdateAppearance()
		{
			if (IsBeingCaptured && IsStalemated)
				teamColorer.SetColor(0);
			else
				teamColorer.SetColor(ControllingTeam);

			conquerTimeIndicator.fillAmount = 1 - MillisCaptured / MillisToTake;
		}

		public void Capture(byte team)
		{
			if (!IsOwner)
				return;

			if (team == ControllingTeam)
				return;

			teamOwner.teamSync.Value = team;
			millisCapturedSync.Value = 0;
		}

		private void Update()
		{
			UpdateAppearance();
		}

		private void FixedUpdate()
		{
			if (IsOwner)
				ManagePointCapture();
		}
	}
}
