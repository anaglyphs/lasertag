using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		private static readonly int TeamColorID = Shader.PropertyToID("_Color");

		[SerializeField] private byte defaultTeam;
		[SerializeField] private Renderer[] renderers;
		[SerializeField] private TeamOwner teamOwner;

		[SerializeField] float multiply = 1;

		public Color Color { get; private set; }

		private void OnValidate()
		{
			renderers = GetComponentsInChildren<MeshRenderer>(true);

			teamOwner = GetComponentInParent<TeamOwner>(true);
		}

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			teamOwner.OnTeamChange.AddListener(SetColor);
			foreach (var renderer in renderers)
			{
				renderer.material = new(renderer.material);
			}
		}

		private void Start()
		{

			if(teamOwner == null)
				SetColor(defaultTeam);
			else
				SetColor(teamOwner.Team);
		}

		public void SetColor(byte teamNumber)
		{
			Color = TeamManagement.TeamColors[teamNumber] * multiply;

			foreach (var renderer in renderers)
			{
				renderer.material.SetColor(TeamColorID, Color);
			}

			OnColorSet.Invoke(Color);
		}
	}
}
