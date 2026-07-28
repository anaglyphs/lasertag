using UnityEngine;

namespace Anaglyph.Input
{
	public class DeactivateUntracked : MonoBehaviour
	{
		[SerializeField] private HandSubject handSubject;

		private void Awake()
		{
			if (!handSubject)
				handSubject = GetComponent<HandSubject>();
		}

		private void Start()
		{
			if (!handSubject)
			{
				Debug.LogError("DeactivateUntracked requires a HandSubject.", this);
				return;
			}

			handSubject.IsTrackingChanged += gameObject.SetActive;
			gameObject.SetActive(handSubject.IsTracking);
		}

		private void OnDestroy()
		{
			if (handSubject)
				handSubject.IsTrackingChanged -= gameObject.SetActive;
		}
	}
}
