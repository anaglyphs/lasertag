using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		private static readonly int TeamColorID = Shader.PropertyToID("_Color");

		[SerializeField] private byte defaultTeam;
		[SerializeField] private new Renderer renderer;
		[SerializeField] private Image image;
		[SerializeField] private TeamOwner teamOwner;

		[SerializeField] float multiply = 1;

		public Color Color { get; private set; }

		private void OnValidate()
		{
			teamOwner = GetComponentInParent<TeamOwner>(true);

			TryGetComponent(out renderer);
			TryGetComponent(out image);
		}

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			teamOwner.OnTeamChange.AddListener(SetColor);
			renderer.material = new(renderer.material);
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
			Color = Teams.TeamColors[teamNumber] * multiply;

			renderer.material.SetColor(TeamColorID, Color);
			image.color = Color;

			OnColorSet.Invoke(Color);
		}
	}
}
