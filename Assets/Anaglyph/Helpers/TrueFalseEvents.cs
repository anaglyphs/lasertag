using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph.Helpers
{
    public class TrueFalseEvents : MonoBehaviour
    {
        public UnityEvent OnTrue = new();
		public UnityEvent OnFalse = new();

        public void Call(bool b)
        {
            if(b)
                OnTrue.Invoke();
            else
                OnFalse.Invoke();
        }
	}
}
