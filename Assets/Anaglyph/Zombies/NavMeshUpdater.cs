using Unity.AI.Navigation;
using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
    public class NavMeshUpdater : MonoBehaviour
    {
		private NavMeshSurface surface;
		private AsyncOperation surfaceUpdate;
        [SerializeField] private float updateEverySeconds = 2;

		private void Awake()
		{
			surface = GetComponent<NavMeshSurface>();
			surface.BuildNavMesh();
			surfaceUpdate = surface.UpdateNavMesh(surface.navMeshData);
		}

		private async void OnEnable()
		{
			while(enabled)
			{
				await Awaitable.WaitForSecondsAsync(updateEverySeconds);

				if(surfaceUpdate.isDone)
					surfaceUpdate = surface.UpdateNavMesh(surface.navMeshData);
			}
		}
	}
}
