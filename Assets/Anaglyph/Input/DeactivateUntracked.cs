using UnityEngine;

namespace Anaglyph.Input
{
	public class DeactivateUntracked : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject; 

		private void Start()
		{
			handSubject.IsTrackingChanged += gameObject.SetActive;
			gameObject.SetActive(handSubject.IsTracking);
		}

		private void OnDestroy()
		{
			handSubject.IsTrackingChanged -= gameObject.SetActive;
		}
	}
}