using System;
using System.Threading;
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
			CancellationToken ctkn = destroyCancellationToken;

			try
			{
				while (enabled)
				{
					await Awaitable.WaitForSecondsAsync(0.1f, ctkn);

					await chunk.Mesh(ctkn);
				}
			}
			catch (OperationCanceledException _)
			{
			}
		}
	}
}