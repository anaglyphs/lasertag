using UnityEngine;

namespace Anaglyph.XRTemplate
{
    public class IgnorePlayerTransform : MonoBehaviour
    {
		private void Awake()
		{
			EnvironmentMapper.ignoredPlayerTransforms.Add(transform);
		}

		private void OnDestroy()
		{
			EnvironmentMapper.ignoredPlayerTransforms.Remove(transform);
		}
	}
}
