using UnityEngine;

namespace Anaglyph.XR.Input
{
	public class DeactivateUntracked : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		private void Awake()
		{
			if (handSubject == null)
				handSubject = GetComponent<HandSubject>();
		}

		private void Start()
		{
			if (handSubject == null)
			{
				Debug.LogError("DeactivateUntracked requires a HandSubject.", this);
				return;
			}

			handSubject.IsTrackingChanged += gameObject.SetActive;
			gameObject.SetActive(handSubject.IsTracking);
		}

		private void OnDestroy()
		{
			if (handSubject != null)
				handSubject.IsTrackingChanged -= gameObject.SetActive;
		}
	}
}
