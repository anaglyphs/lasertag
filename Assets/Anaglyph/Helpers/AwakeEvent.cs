using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph
{
    public class AwakeEvent : MonoBehaviour
    {
        public UnityEvent onAwake = new();

		private void Awake()
		{
			onAwake.Invoke();
		}
	}
}
