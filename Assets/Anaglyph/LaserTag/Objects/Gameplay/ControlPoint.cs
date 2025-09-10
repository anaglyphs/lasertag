using System.Threading.Tasks;
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

		public UnityEvent<byte> ControllingTeamChanged = new();

		[SerializeField] private TeamOwner teamOwner;
		public byte HoldingTeam => teamOwner.Team;

		public byte CapturingTeam => capturingTeamSync.Value;
		private NetworkVariable<byte> capturingTeamSync = new(0);

		public float MillisCaptured => millisCapturedSync.Value;
		private NetworkVariable<float> millisCapturedSync = new(0);

		private MatchReferee referee => MatchReferee.Instance;

		[SerializeField] private Image conquerTimeIndicator;

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Start()
		{
			teamOwner.teamSync.OnValueChanged += delegate
			{

				if (IsOwner)
					millisCapturedSync.Value = 0;

				ControllingTeamChanged.Invoke(HoldingTeam);
			};

			referee.StateChanged += OnMatchStateChanged;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();

			if(referee != null)
				referee.StateChanged -= OnMatchStateChanged;
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
			{
				switch (state)
				{
					case MatchState.Playing:
						ResetPointRpc();
						_ = ScoreLoop();
						break;
				}
			}
		}

		public override void OnGainedOwnership()
		{
			if (referee.State == MatchState.Playing)
				_ = ScoreLoop();
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

		private async Task ScoreLoop()
		{
			if (!IsOwner)
				return;

			while (referee.State == MatchState.Playing)
			{
				await Awaitable.WaitForSecondsAsync(1, destroyCancellationToken);
				destroyCancellationToken.ThrowIfCancellationRequested();

				if (teamOwner.Team != 0)
					referee.ScoreTeamRpc(teamOwner.Team, referee.Settings.pointsPerSecondHoldingPoint);
			}
		}


		private bool CheckIfPlayerIsInside(Networking.PlayerAvatar player)
		{
			if (!player.IsAlive)
				return false;

			Vector3 playerHeadPos = player.HeadTransform.position;
			return Geo.PointIsInCylinder(transform.position, Radius, 3, playerHeadPos);
		}

		private void Update()
		{
			if (IsOwner)
			{
				bool isSecure = CapturingTeam == HoldingTeam;
				bool capturingTeamIsInside = false;
				bool isStalemated = false;

				if (isSecure)
				{
					// check for new capturing players
					foreach (Networking.PlayerAvatar player in Networking.PlayerAvatar.All.Values)
					{
						if (player.Team != 0 && player.Team != HoldingTeam && CheckIfPlayerIsInside(player))
						{
							capturingTeamSync.Value = player.Team;
							isSecure = false;
						}
					}
				}
				else
				{
					foreach (Networking.PlayerAvatar player in Networking.PlayerAvatar.All.Values)
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