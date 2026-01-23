using Anaglyph.DepthKit.Meshing;
using UnityEngine;

namespace Anaglyph.DepthKit
{
	public class Test : MonoBehaviour
	{
		[SerializeField] private MeshChunk chunk;

		private void OnEnable()
		{
			IntegrateLoop();
		}

		private async void IntegrateLoop()
		{
			while (enabled)
			{
				await Awaitable.WaitForSecondsAsync(0.1f);

				await chunk.Mesh();
			}
		}
	}
}