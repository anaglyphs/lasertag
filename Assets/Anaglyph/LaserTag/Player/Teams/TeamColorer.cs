using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.LaserTag.Player.Teams
{
	[ExecuteAlways]
	public class TeamColorer : MonoBehaviour
	{
		public static readonly int ColorID = Shader.PropertyToID("_Color");
		public static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");

		[SerializeField] internal byte defaultTeam;
		private TeamOwner teamOwner;
		private new Renderer renderer;
		private MaterialPropertyBlock propertyBlock;
		private Graphic graphic;

		[SerializeField] private float multiply = 1;

		public Color Color { get; private set; }

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			propertyBlock = new MaterialPropertyBlock();

			if (TryGetComponent(out renderer))
				renderer.GetPropertyBlock(propertyBlock);

			TryGetComponent(out graphic);

			teamOwner = GetComponentInParent<TeamOwner>(true);
			if (teamOwner)
				teamOwner.TeamChanged += SetColor;
		}

		private void Start()
		{
			UpdateColor();
		}

		private void OnValidate()
		{
			teamOwner = GetComponentInParent<TeamOwner>(true);
			if (teamOwner)
				defaultTeam = teamOwner.Team;
			
			UpdateColor();
		}

		private void UpdateColor()
		{
			SetColor(teamOwner ? teamOwner.Team : defaultTeam);
		}

		public void SetColor(byte teamNumber)
		{
			Color = Teams.Colors[teamNumber] * multiply;

			if (renderer)
			{
				propertyBlock?.SetColor(ColorID, Color);
				propertyBlock?.SetColor(BaseColorID, Color);
				renderer.SetPropertyBlock(propertyBlock);
			}

			if (graphic)
				graphic.color = Color;

			OnColorSet.Invoke(Color);
		}
	}
}