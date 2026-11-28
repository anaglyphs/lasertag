using Anaglyph.LaserTag.Weapons;
using Anaglyph.Netcode;
using Anaglyph.XR;
using Anaglyph.XR.Input;
using UnityEngine;

namespace Anaglyph.LaserTag.Interface
{
	/// <summary>
	/// While the main menu is open weapons are put away. Out of a session it also dims passthrough and always shows rays.
	/// </summary>
	[RequireComponent(typeof(MainMenuController))]
	public class MenuOpenPresentation : MonoBehaviour
	{
		[SerializeField, Range(0f, 1f)] private float passthroughBrightnessWhileOpen = 0.35f;
		[SerializeField, Min(0f)] private float passthroughFadeSeconds = 0.2f;

		private MainMenuController menu;
		private float passthroughBrightness = 1f;
		private bool appliedMenuOpenOutOfSession;

		private void Awake() => menu = GetComponent<MainMenuController>();

		private void Update()
		{
			bool menuOpen = menu.IsVisible;
			WeaponSwitcher.Instance?.SetWeaponsPutAway(this, menuOpen);

			bool menuOpenOutOfSession = menuOpen && NetcodeManagement.State != NetcodeState.Connected;
			if (menuOpenOutOfSession != appliedMenuOpenOutOfSession)
			{
				appliedMenuOpenOutOfSession = menuOpenOutOfSession;
				HandInput.RaysShownWithoutTarget = menuOpenOutOfSession;
			}

			float targetBrightness = menuOpenOutOfSession ? passthroughBrightnessWhileOpen : 1f;
			if (passthroughBrightness == targetBrightness) return;
			passthroughBrightness = passthroughFadeSeconds <= 0f ? targetBrightness
				: Mathf.MoveTowards(passthroughBrightness, targetBrightness, Time.unscaledDeltaTime / passthroughFadeSeconds);
			PassthroughStylingFeature.SetBrightness(passthroughBrightness);
		}

		private void OnDisable()
		{
			WeaponSwitcher.Instance?.SetWeaponsPutAway(this, false);
			HandInput.RaysShownWithoutTarget = false;
			appliedMenuOpenOutOfSession = false;
			passthroughBrightness = 1f;
			PassthroughStylingFeature.SetBrightness(1f);
		}
	}
}
