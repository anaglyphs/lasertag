using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Anaglyph.Lasertag
{
	[ExecuteAlways]
	public class TeamColorer : MonoBehaviour
	{
		public const string ColorPerObjectName = "_Color";
		public static readonly int ColorID = Shader.PropertyToID(ColorPerObjectName);

		[SerializeField] private byte defaultTeam;
		private TeamOwner teamOwner;
		private new Renderer renderer;
		private MaterialPropertyBlock propertyBlock;
		private Graphic graphic;

		[SerializeField] float multiply = 1;

		public Color Color { get; private set; }

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			propertyBlock = new MaterialPropertyBlock();

			if(TryGetComponent(out renderer))
				renderer.GetPropertyBlock(propertyBlock);
			
			TryGetComponent(out graphic);

			teamOwner = GetComponentInParent<TeamOwner>(true);
			if(teamOwner)
				teamOwner.OnTeamChange.AddListener(SetColor);
		}

		private void Start() => UpdateColor();
		private void OnValidate() => UpdateColor();

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
				renderer.SetPropertyBlock(propertyBlock);
			}

			if(graphic)
				graphic.color = Color;

			OnColorSet.Invoke(Color);
		}
	}
}
