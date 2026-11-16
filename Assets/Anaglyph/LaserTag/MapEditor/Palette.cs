using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.MapEditor
{
	public class Palette : MonoBehaviour
	{
		private HandSubject handSubject;

		private void Awake()
		{
			TryGetComponent(out handSubject);
		}

		private void Start()
		{
			MapEditor.ActiveChanged += gameObject.SetActive;
			gameObject.SetActive(MapEditor.IsActive);

			Dominance.Changed += FollowOffHand;
			FollowOffHand();
		}

		private void OnDestroy()
		{
			MapEditor.ActiveChanged -= gameObject.SetActive;
			Dominance.Changed -= FollowOffHand;
		}

		private void FollowOffHand()
		{
			handSubject.Assign(HandInput.Get(Dominance.Hand == Handedness.Left ? Handedness.Right : Handedness.Left));
		}
	}
}