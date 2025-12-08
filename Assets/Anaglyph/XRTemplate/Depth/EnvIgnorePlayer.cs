using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
	public class EnvIgnorePlayer : MonoBehaviour
	{
		private void Awake()
		{
			EnvironmentMapper.Instance?.playerHeads.Add(transform);
		}

		private void OnDestroy()
		{
			EnvironmentMapper.Instance?.playerHeads.Remove(transform);
		}
	}
}