using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph
{
    public class DebugBuildCheck : MonoBehaviour
    {
		public UnityEvent<bool> OnAwake;
		private void Awake()
		{
			OnAwake.Invoke(Debug.isDebugBuild);
		}
	}
}
