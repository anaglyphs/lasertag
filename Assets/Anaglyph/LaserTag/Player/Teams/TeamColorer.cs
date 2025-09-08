using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		public const string ColorPerObjectName = "_Color";
		public static readonly int ColorID = Shader.PropertyToID(ColorPerObjectName);

		[SerializeField] private byte defaultTeam;
		private TeamOwner teamOwner;
		private new Renderer renderer;
		private MaterialPropertyBlock propertyBlock;
		private Image image;

		[SerializeField] float multiply = 1;

		public Color Color { get; private set; }

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			propertyBlock = new();

			if(TryGetComponent(out renderer))
				renderer.GetPropertyBlock(propertyBlock);
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

			propertyBlock.SetColor(ColorID, Color);

			if(renderer != null)
				renderer.SetPropertyBlock(propertyBlock);

			if(image != null)
				image.color = Color;

			OnColorSet.Invoke(Color);
		}
	}
}
