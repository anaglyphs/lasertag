using Unity.AI.Navigation;
using UnityEngine;

namespace Anaglyph.LaserTag.NPCs
{
	public class NavMeshUpdater : MonoBehaviour
	{
		[SerializeField] private float updateFrequency = 1f;

		private NavMeshSurface surface;

		private void Awake()
		{
			TryGetComponent(out surface);
			surface.BuildNavMesh();
		}

		private void OnEnable()
		{
			UpdateLoop();
		}

		private async void UpdateLoop()
		{
			while (enabled)
			{
				surface.UpdateNavMesh(surface.navMeshData);

				await Awaitable.WaitForSecondsAsync(updateFrequency);
			}
		}
	}
}