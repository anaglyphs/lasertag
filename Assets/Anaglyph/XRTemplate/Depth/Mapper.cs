using Anaglyph.XRTemplate;
using System.Collections.Generic;
using UnityEngine;

public class Mapper : MonoBehaviour
{
	public static List<Chunk> chunks = new();
	public float frequency = 30f;

	private void OnEnable()
	{
		IntegrateLoop();
	}

	private async void IntegrateLoop()
	{
		while (enabled)
		{
			await Awaitable.WaitForSecondsAsync(1f / frequency);

			foreach (var chunk in chunks)
			{
				chunk.Integrate();
			}
		}
	}
}