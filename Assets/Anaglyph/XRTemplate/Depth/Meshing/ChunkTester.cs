using System;
using Anaglyph.DepthKit.Meshing;
using UnityEngine;

namespace Anaglyph.DepthKit
{
	public class ChunkTester : MonoBehaviour
	{
		private MeshChunk chunk;

		private void OnEnable()
		{
			TryGetComponent(out chunk);
			IntegrateLoop();
		}

		private async void IntegrateLoop()
		{
			try
			{
				while (enabled)
				{
					await Awaitable.WaitForSecondsAsync(0.1f);

					await chunk.Mesh(destroyCancellationToken);
				}
			}
			catch (OperationCanceledException _)
			{
			}
		}
	}
}