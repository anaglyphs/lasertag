using System;
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
		public byte HoldingTeam => teamOwner.Team;

		public byte CapturingTeam => capturingTeamSync.Value;
		private NetworkVariable<byte> capturingTeamSync = new(0);

		public float MillisCaptured => millisCapturedSync.Value;
		private NetworkVariable<float> millisCapturedSync = new(0);

		[SerializeField] private Image conquerTimeIndicator;

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void SetComponent(ref TeamOwner teamOwner)
		{
			throw new NotImplementedException();
		}

		private void Awake()
		{
			AllControlPoints.Add(this);

			teamOwner.teamSync.OnValueChanged += delegate
			{

				if (IsOwner)
					millisCapturedSync.Value = 0;

				onControllingTeamChange.Invoke(HoldingTeam);
			};
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			AllControlPoints.Remove(this);
		}

		private bool CheckIfPlayerIsInside(Networking.Avatar player)
		{
			if (!player.IsAlive)
				return false;

			Vector3 playerHeadPos = player.HeadTransform.position;
			return Geo.PointIsInCylinder(transform.position, Radius, 3, playerHeadPos);
		}

		[Rpc(SendTo.Owner)]
		public void CaptureOwnerRpc(byte team)
		{
			if (team == HoldingTeam)
				return;

			teamOwner.teamSync.Value = team;
			millisCapturedSync.Value = 0;
		}

		[Rpc(SendTo.Owner)]
		public void ResetPointRpc()
		{
			teamOwner.teamSync.Value = 0;
			millisCapturedSync.Value = 0;
			capturingTeamSync.Value = 0;
		}

		private void FixedUpdate()
		{
			if (IsOwner)
			{
				bool isSecure = CapturingTeam == HoldingTeam;
				bool capturingTeamIsInside = false;
				bool isStalemated = false;

				if (isSecure)
				{
					// check for new capturing players
					foreach (Networking.Avatar player in Networking.Avatar.AllPlayers.Values)
					{
						if (player.Team != 0 && player.Team != HoldingTeam && CheckIfPlayerIsInside(player))
						{
							capturingTeamSync.Value = player.Team;
							isSecure = false;
						}
					}
				}
				
				if(!isSecure)
				{
					foreach (Networking.Avatar player in Networking.Avatar.AllPlayers.Values)
					{
						if (CheckIfPlayerIsInside(player))
						{
							if (player.Team == CapturingTeam)
								capturingTeamIsInside = true;
							else
								isStalemated = true;
						}
					}

					if (capturingTeamIsInside)
					{
						if (!isStalemated)
						{
							millisCapturedSync.Value += Time.fixedDeltaTime * 1000;

							if (millisCapturedSync.Value > MillisToTake)
								CaptureOwnerRpc(CapturingTeam);
						}
					}
					else
					{
						millisCapturedSync.Value = Mathf.Max(0, millisCapturedSync.Value - Time.fixedDeltaTime * 1000);

						// capturing team loses capture over time
						if (millisCapturedSync.Value <= 0)
							capturingTeamSync.Value = HoldingTeam;
					}
				}
			}

			conquerTimeIndicator.fillAmount = 1 - MillisCaptured / MillisToTake;
		}
	}
}
