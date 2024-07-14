using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Lasertag
{
	public class TeamColorer : MonoBehaviour
	{
		private static readonly int TeamColorID = Shader.PropertyToID("_Color");

		[SerializeField] private MeshRenderer[] renderers;

		private void OnValidate()
		{
			renderers = GetComponentsInChildren<MeshRenderer>(true);
		}

		public UnityEvent<Color> OnColorSet = new();

		private void Awake()
		{
			foreach (var renderer in renderers)
			{
				renderer.material = new(renderer.material);
			}

			SetColor(0);
		}

		public void SetColor(int teamNumber)
		{
			Color color = TeamManagement.TeamColors[teamNumber];

			foreach (var renderer in renderers)
			{
				renderer.material.SetColor(TeamColorID, color);
			}

			OnColorSet.Invoke(color);
		}
	}
}
