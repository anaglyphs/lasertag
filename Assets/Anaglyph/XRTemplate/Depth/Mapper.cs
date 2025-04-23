using Anaglyph.XRTemplate;
using System.Collections.Generic;
using UnityEngine;

public class Mapper : MonoBehaviour
{
	public static List<Chunk> chunks = new();

	private void OnEnable()
	{
		IntegrateLoop();
	}

	private async void IntegrateLoop()
	{
		while (enabled)
		{
			await Awaitable.WaitForSecondsAsync(0.5f);

			foreach (var chunk in chunks)
			{
				chunk.Integrate();
			}
		}
	}
}