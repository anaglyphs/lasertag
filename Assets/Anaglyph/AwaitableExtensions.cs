using System;
using UnityEngine;

namespace Anaglyph
{
	/// <summary>Starts asynchronous work that nothing awaits.</summary>
	public static class AwaitableExtensions
	{
		/// <summary>Cancellation ends the work silently. Any other exception reaches the console.</summary>
		public static async void RunUntilCancelled(this Awaitable work)
		{
			try
			{
				await work;
			}
			catch (OperationCanceledException)
			{
			}
		}
	}
}
