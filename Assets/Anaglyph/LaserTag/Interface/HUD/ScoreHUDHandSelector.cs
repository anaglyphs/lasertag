using Anaglyph.XR.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace Anaglyph.LaserTag.Interface.HUD
{
	public class ScoreHUDHandSelector : MonoBehaviour
	{
		[SerializeField] private HandHUDPositioner positioner;
		
		private VisualElement hud;
		private HandInput leftHand, rightHand;

		private void OnEnable()
		{
			hud = HUDElement.Require<VisualElement>(this, "hand-hud-root");
			HUDElement.SetDisplayed(hud, false);

			if (didStart) Setup();
		}

		private void Start()
		{
			Setup();
		}

		private void Setup()
		{
			leftHand = HandInput.Hands[Handedness.Left];
			rightHand = HandInput.Hands[Handedness.Right];

			leftHand.IsTrackingChanged += OnHandTrackingStateChanged;
			rightHand.IsTrackingChanged += OnHandTrackingStateChanged;

			SetHand();
		}

		private void OnDisable()
		{
			if (hud != null) HUDElement.SetDisplayed(hud, false);
			if(positioner != null) positioner.SetHand(null);
			
			if(leftHand != null) leftHand.IsTrackingChanged -= OnHandTrackingStateChanged;
			if(rightHand != null) rightHand.IsTrackingChanged -= OnHandTrackingStateChanged;
		}

		private void OnHandTrackingStateChanged(bool _) => SetHand();

		private void SetHand()
		{
			// choose hand
			HandInput follow = null;

			if (rightHand.IsTracking)
			{
				follow = rightHand;
			}
			else if(leftHand.IsTracking)
			{
				follow = leftHand;
			} 
			
			positioner.SetHand(follow);
			HUDElement.SetDisplayed(hud, follow != null);
		}
	}
}
