using Anaglyph.LaserTag.Weapons;
using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>While the main menu is open: passthrough fades darker, weapons are put away and rays always show.</summary>
	[RequireComponent(typeof(MainMenuController))]
	public class MenuOpenPresentation : MonoBehaviour
	{
		[SerializeField, Range(0f, 1f)] private float passthroughBrightnessWhileOpen = 0.35f;
		[SerializeField, Min(0f)] private float passthroughFadeSeconds = 0.2f;

		private MainMenuController menu;
		private float passthroughBrightness = 1f;
		private bool appliedMenuOpen;

		private void Awake() => menu = GetComponent<MainMenuController>();

		private void Update()
		{
			bool menuOpen = menu.IsVisible;
			WeaponSwitcher.Instance?.SetWeaponsPutAway(this, menuOpen);

			if (menuOpen != appliedMenuOpen)
			{
				appliedMenuOpen = menuOpen;
				HandInput.RaysShownWithoutTarget = menuOpen;
			}

			float targetBrightness = menuOpen ? passthroughBrightnessWhileOpen : 1f;
			if (passthroughBrightness == targetBrightness) return;
			passthroughBrightness = passthroughFadeSeconds <= 0f ? targetBrightness
				: Mathf.MoveTowards(passthroughBrightness, targetBrightness, Time.unscaledDeltaTime / passthroughFadeSeconds);
			PassthroughStylingFeature.SetBrightness(passthroughBrightness);
		}

		private void OnDisable()
		{
			WeaponSwitcher.Instance?.SetWeaponsPutAway(this, false);
			HandInput.RaysShownWithoutTarget = false;
			appliedMenuOpen = false;
			passthroughBrightness = 1f;
			PassthroughStylingFeature.SetBrightness(1f);
		}
	}
}
