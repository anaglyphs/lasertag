using Anaglyph.XRTemplate;
using UnityEngine;

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
