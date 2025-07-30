using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		private static readonly int TeamColorID = Shader.PropertyToID("_Color");

		[SerializeField] private byte defaultTeam;
		private TeamOwner teamOwner;
		private new Renderer renderer;
		private Image image;

		[SerializeField] float multiply = 1;

		public Color Color { get; private set; }

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			if(TryGetComponent(out renderer))
				renderer.material = new(renderer.material);
			TryGetComponent(out image);

			teamOwner = GetComponentInParent<TeamOwner>(true);
			
			if(teamOwner != null)
				teamOwner.OnTeamChange.AddListener(SetColor);
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
			Color = Teams.Colors[teamNumber] * multiply;

			if(renderer != null)
				renderer.material.SetColor(TeamColorID, Color);

			if(image != null)
				image.color = Color;

			OnColorSet.Invoke(Color);
		}
	}
}
