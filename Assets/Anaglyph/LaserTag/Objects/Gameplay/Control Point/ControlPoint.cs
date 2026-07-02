using System.Collections.Generic;
using System.Threading;
using Anaglyph.Lasertag.Networking;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class ControlPoint : NetworkBehaviour
	{
		public const float MillisToTake = 10000;

		public UnityEvent<byte> ControllingTeamChanged = new();

		[SerializeField] private TeamOwner teamOwner;
		public byte HoldingTeam => teamOwner.Team;

		public byte CapturingTeam => capturingTeamSync.Value;
		private NetworkVariable<byte> capturingTeamSync = new(0);

		public float MillisCaptured => millisCapturedSync.Value;
		private NetworkVariable<float> millisCapturedSync = new(0);

		private MatchReferee referee => MatchReferee.Current;

		[SerializeField] private Image conquerTimeIndicator;

		private readonly HashSet<PlayerAvatar> playersInside = new();

		public static List<ControlPoint> AllControlPoints { get; private set; } = new();

		private void OnValidate()
		{
			TryGetComponent(out teamOwner);
		}

		private void Awake()
		{
			AllControlPoints.Add(this);
		}

		private void Start()
		{
			MatchReferee.StateChanged += OnMatchStateChanged;
			teamOwner.TeamChanged += OnTeamChanged;
		}

		public override void OnDestroy()
		{
			base.OnDestroy();
			MatchReferee.StateChanged -= OnMatchStateChanged;
			AllControlPoints.Remove(this);
		}

		internal void RemovePlayer(PlayerAvatar player)
		{
			playersInside.Remove(player);
		}

		private void OnTeamChanged(byte team)
		{
			if (IsOwner) millisCapturedSync.Value = 0;

			ControllingTeamChanged.Invoke(HoldingTeam);
		}

		private void OnMatchStateChanged(MatchState state)
		{
			if (IsOwner)
				switch (state)
				{
					case MatchState.Playing:
						ResetPointRpc();
						_ = ScoreLoop();
						break;
				}
		}

		public override void OnGainedOwnership()
		{
			if (MatchReferee.State == MatchState.Playing)
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
			CancellationToken ctkn = destroyCancellationToken;

			if (!IsOwner)
				return;

			while (MatchReferee.State == MatchState.Playing)
			{
				await Awaitable.WaitForSecondsAsync(1, ctkn);
				ctkn.ThrowIfCancellationRequested();

				if (teamOwner.Team != 0)
					referee.Score(teamOwner.Team, MatchReferee.Settings.pointsPerSecondHoldingPoint);
			}
		}


		private void OnTriggerEnter(Collider other)
		{
			if (!other.CompareTag(PlayerAvatar.Tag))
				return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player != null)
				playersInside.Add(player);
		}

		private void OnTriggerExit(Collider other)
		{
			if (!other.CompareTag(PlayerAvatar.Tag))
				return;

			PlayerAvatar player = other.GetComponentInParent<PlayerAvatar>();
			if (player != null)
				playersInside.Remove(player);
		}

		private bool CheckIfPlayerIsInside(PlayerAvatar player)
		{
			return player.IsAlive && playersInside.Contains(player);
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
					foreach (PlayerAvatar player in PlayerAvatar.All.Values)
						if (player.Team != 0 && player.Team != HoldingTeam && CheckIfPlayerIsInside(player))
						{
							capturingTeamSync.Value = player.Team;
							isSecure = false;
						}
				}
				else
				{
					foreach (PlayerAvatar player in PlayerAvatar.All.Values)
						if (CheckIfPlayerIsInside(player))
						{
							if (player.Team == CapturingTeam)
								capturingTeamIsInside = true;
							else
								isStalemated = true;
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