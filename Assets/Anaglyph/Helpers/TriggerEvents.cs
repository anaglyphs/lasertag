using UnityEngine;
using UnityEngine.Events;

namespace Anaglyph
{
    public class TriggerEvents : MonoBehaviour
    {
		public string TagFilter;

		public UnityEvent<Collider> onTriggerEnter;
		public UnityEvent<Collider> onTriggerExit;

		private void OnTriggerEnter(Collider other)
		{
			if(TagFilter == string.Empty || other.CompareTag(TagFilter))
			{
				onTriggerEnter.Invoke(other);
			}
		}

		private void OnTriggerExit(Collider other)
		{
			if (TagFilter == string.Empty || other.CompareTag(TagFilter))
			{
				onTriggerExit.Invoke(other);
			}
		}
	}
}
