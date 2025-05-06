using Anaglyph.XRTemplate;
using UnityEngine;

namespace Anaglyph.XRTemplate.DepthKit
{
	public class EnvIgnorePlayer : MonoBehaviour
	{
		private void Awake()
		{
			EnvironmentMapper.Instance.PlayerHeads.Add(transform);
		}

		private void OnDestroy()
		{
			EnvironmentMapper.Instance.PlayerHeads.Remove(transform);
		}
	}
}