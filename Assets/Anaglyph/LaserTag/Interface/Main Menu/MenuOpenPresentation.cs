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
		private WeaponSwitcher weaponSwitcher;
		private float passthroughBrightness = 1f;
		private float targetPassthroughBrightness = 1f;

		private void Awake() => menu = GetComponent<MainMenuController>();

		private void Start()
		{
			weaponSwitcher = WeaponSwitcher.Instance;
			Subscribe();
		}

		private void OnEnable()
		{
			if (didStart)
				Subscribe();
		}

		private void Subscribe()
		{
			menu.VisibilityChanged += ApplyMenuState;
			NetcodeManagement.StateChanged += HandleNetcodeStateChange;
			ApplyMenuState();
		}

		private void HandleNetcodeStateChange(NetcodeState state) => ApplyMenuState();

		private void ApplyMenuState()
		{
			bool menuOpen = menu.IsVisible;
			weaponSwitcher.SetWeaponsPutAway(this, menuOpen);

			bool menuOpenOutOfSession = menuOpen && NetcodeManagement.State != NetcodeState.Connected;
			HandInput.RaysShownWithoutTarget = menuOpenOutOfSession;
			targetPassthroughBrightness = menuOpenOutOfSession ? passthroughBrightnessWhileOpen : 1f;
		}

		private void Update()
		{
			if (passthroughBrightness == targetPassthroughBrightness) return;
			passthroughBrightness = passthroughFadeSeconds <= 0f ? targetPassthroughBrightness
				: Mathf.MoveTowards(passthroughBrightness, targetPassthroughBrightness, Time.unscaledDeltaTime / passthroughFadeSeconds);
			PassthroughStylingFeature.SetBrightness(passthroughBrightness);
		}

		private void OnDisable()
		{
			if (!didStart) return;

			menu.VisibilityChanged -= ApplyMenuState;
			NetcodeManagement.StateChanged -= HandleNetcodeStateChange;
			weaponSwitcher.SetWeaponsPutAway(this, false);
			HandInput.RaysShownWithoutTarget = false;
			passthroughBrightness = targetPassthroughBrightness = 1f;
			PassthroughStylingFeature.SetBrightness(1f);
		}
	}
}
