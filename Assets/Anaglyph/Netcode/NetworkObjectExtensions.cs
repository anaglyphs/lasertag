using System.Threading;
using Unity.Netcode;
using UnityEngine;

namespace Anaglyph.Netcode
{
	/// <summary>Despawning helpers for network objects.</summary>
	public static class NetworkObjectExtensions
	{
		public static async Awaitable DespawnAfterSeconds(this NetworkObject networkObject, float seconds, CancellationToken token)
		{
			await Awaitable.WaitForSecondsAsync(seconds, token);
			networkObject.Despawn(true);
		}
	}
}
