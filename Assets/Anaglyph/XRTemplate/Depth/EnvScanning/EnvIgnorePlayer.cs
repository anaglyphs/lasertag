using UnityEngine;

namespace Anaglyph.DepthKit.EnvScanning
{
	// registers this transform as a player head
	// to mask out of environment scanning
	public class EnvIgnorePlayer : MonoBehaviour
	{
		private void OnEnable()
		{
			EnvScanner.PlayerHeads.Add(transform);
		}

		private void OnDisable()
		{
			EnvScanner.PlayerHeads.Remove(transform);
		}
	}
}
