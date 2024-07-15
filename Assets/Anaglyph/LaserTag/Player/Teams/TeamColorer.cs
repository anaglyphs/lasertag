using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		private static readonly int TeamColorID = Shader.PropertyToID("_Color");

		[SerializeField] private MeshRenderer[] renderers;
		[SerializeField] private TeamOwner teamOwner;

		public Color Color { get; private set; }

		private void OnValidate()
		{
			renderers = GetComponentsInChildren<MeshRenderer>(true);

			this.SetComponetFromParent(ref teamOwner);
			teamOwner?.OnTeamChange.AddPersistentListenerOnce(SetColor);
		}

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			foreach (var renderer in renderers)
			{
				renderer.material = new(renderer.material);
			}
		}

		private void Start()
		{
			if(teamOwner != null)
			SetColor(teamOwner.Team);
		}

		public void SetColor(byte teamNumber)
		{
			Color = TeamManagement.TeamColors[teamNumber];

			foreach (var renderer in renderers)
			{
				renderer.material.SetColor(TeamColorID, Color);
			}

			OnColorSet.Invoke(Color);
		}
	}
}
