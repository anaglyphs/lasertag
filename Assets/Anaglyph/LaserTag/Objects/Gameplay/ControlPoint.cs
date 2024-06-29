using Anaglyph.LaserTag.Networking;
using NUnit.Framework;
using System.Collections.Generic;
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
		private static int ColorID = Shader.PropertyToID("_Color");

		public static List<ControlPoint> AllControlPoints { get; private set; } = new();

		[SerializeField] private MeshRenderer meshRenderer;
		[SerializeField] private Image conquerTimeIndicator;

		public UnityEvent onControllingTeamChange = new();

		public int ControllingTeam => controllingTeamSync.Value;
		private NetworkVariable<int> controllingTeamSync = new(-1);

		public int CapturingTeam => capturingTeamSync.Value;
		private NetworkVariable<int> capturingTeamSync = new(-1);

		public float MillisCaptured => millisCapturedSync.Value;
		private NetworkVariable<float> millisCapturedSync = new(0);

		private void Awake()
		{
			meshRenderer.material = new Material(meshRenderer.sharedMaterial);
			AllControlPoints.Add(this);

			controllingTeamSync.OnValueChanged += delegate { 

				if(IsOwner)
					millisCapturedSync.Value = 0;
				
				onControllingTeamChange.Invoke();
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
				foreach (Player player in Player.AllPlayers)
				{
					if (CheckIfPlayerIsInside(player) && player.Team != ControllingTeam)
						capturingTeamSync.Value = player.Team;
				}
			}
			else
			{
				bool capturingTeamIsInside = false;
				bool anyOtherTeamIsInside = false;

				foreach (Player player in Player.AllPlayers)
				{
					if (CheckIfPlayerIsInside(player))
					{
						if (player.Team == CapturingTeam)
							capturingTeamIsInside = true;
						else
							anyOtherTeamIsInside = true;
					}
				}

				if (capturingTeamIsInside)
				{
					if (!anyOtherTeamIsInside)
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
			Color color = Color.red;
			if (ControllingTeam == MainPlayer.Instance.Team)
				color = Color.green;
			else if (CapturingTeam == MainPlayer.Instance.Team)
				color = Color.yellow;

			meshRenderer.material.SetColor(ColorID, color);
			conquerTimeIndicator.color = color;

			conquerTimeIndicator.fillAmount = 1 - MillisCaptured / MillisToTake;
		}

		public void Capture(int team)
		{
			if (!IsOwner)
				return;

			if (team == controllingTeamSync.Value)
				return;

			controllingTeamSync.Value = team;
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
